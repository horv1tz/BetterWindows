using System.Globalization;
using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native;

/// <summary>
/// What Explorer would show on the desktop, so that hiding Explorer's icon list (our own desktop layer draws icons instead) does not make those items unreachable:
/// the files and folders of <c>%USERPROFILE%\Desktop</c> and of the Public desktop, plus the two system icons "This PC" and "Recycle Bin".
/// Nothing is resolved: <c>Target</c> is the full path, a .lnk stays a .lnk (opening it through the shell follows the link). Hidden and system items and desktop.ini are skipped.
/// Names of .lnk/.url items have no extension, like in Explorer. Order: the two specials, then folders, then files, each in natural name order.
/// Changes of either folder raise <see cref="Changed"/> (debounced 300 ms).
/// </summary>
public sealed class ShellDesktopFolderSource : IDesktopFolderSource, IDisposable
{
    internal const string ThisPcId = "special:thisPc";
    internal const string RecycleBinId = "special:recycleBin";
    private const string ThisPcParsingName = "::{20D04FE0-3AEA-1069-A2D8-08002B30309D}";
    private const string RecycleBinParsingName = "::{645FF040-5081-101B-9F08-00AA002F954E}";
    private static readonly TimeSpan Debounce = TimeSpan.FromMilliseconds(300);

    private readonly ILogger _log;
    private readonly IReadOnlyList<string> _folders;
    private readonly Func<string, string?> _specialNameResolver;
    private readonly object _gate = new();
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _debounceTimer;
    private bool _watching;
    private bool _disposed;
    private Action? _changed;
    private string? _thisPcName;
    private string? _recycleBinName;
    private AppsStaWorker? _sta;

    /// <summary>Creates the source for the current user's desktop and the Public desktop.</summary>
    public ShellDesktopFolderSource(ILogger<ShellDesktopFolderSource>? logger = null)
        : this(logger, DesktopFoldersFromEnvironment(), null)
    {
    }

    /// <summary>Test seam: explicit folders and an optional resolver for the localised special names (null = ask the shell).</summary>
    internal ShellDesktopFolderSource(ILogger? logger, IReadOnlyList<string> folders, Func<string, string?>? specialNameResolver)
    {
        _log = logger ?? NullLogger.Instance;
        _folders = folders;
        _specialNameResolver = specialNameResolver ?? ResolveShellName;
    }

    private static IReadOnlyList<string> DesktopFoldersFromEnvironment()
    {
        var result = new List<string>(2);
        var user = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        if (!string.IsNullOrEmpty(user)) result.Add(user);
        var common = Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory);
        if (!string.IsNullOrEmpty(common) && !string.Equals(common, user, StringComparison.OrdinalIgnoreCase)) result.Add(common);
        return result;
    }

    /// <inheritdoc />
    public event Action? Changed
    {
        add
        {
            lock (_gate) _changed += value;
            EnsureWatching();
        }
        remove
        {
            lock (_gate) _changed -= value;
        }
    }

    /// <inheritdoc />
    public IReadOnlyList<DesktopEntry> Enumerate()
    {
        try
        {
            EnsureWatching();
            var entries = new List<DesktopEntry>
            {
                new(ThisPcId, SpecialName(ThisPcParsingName, ref _thisPcName, "This PC", "Этот компьютер"), ShellTargetPolicy.ThisPc, IsFolder: true, IsSpecial: true),
                new(RecycleBinId, SpecialName(RecycleBinParsingName, ref _recycleBinName, "Recycle Bin", "Корзина"), ShellTargetPolicy.RecycleBin, IsFolder: true, IsSpecial: true),
            };
            entries.AddRange(EnumerateFileSystemItems());
            return entries;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Enumerating the desktop failed");
            return Array.Empty<DesktopEntry>();
        }
    }

    private List<DesktopEntry> EnumerateFileSystemItems()
    {
        var items = new List<DesktopEntry>();
        foreach (var folder in _folders)
        {
            IEnumerable<FileSystemInfo> infos;
            try
            {
                if (!Directory.Exists(folder)) continue;
                infos = new DirectoryInfo(folder).EnumerateFileSystemInfos().ToList();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Cannot enumerate {Folder}", folder);
                continue;
            }
            foreach (var info in infos)
            {
                try
                {
                    var entry = ToEntry(info);
                    if (entry != null) items.Add(entry);
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Skipping unreadable desktop item");
                }
            }
        }
        // Folders first, then files; natural name order inside each group; the path breaks ties between the user and the public desktop.
        return items
            .OrderBy(e => e.IsFolder ? 0 : 1)
            .ThenBy(e => e.Name, AppsNaturalStringComparer.Instance)
            .ThenBy(e => e.Target, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>Maps one file-system item to an entry; null for items Explorer does not show.</summary>
    internal static DesktopEntry? ToEntry(FileSystemInfo info)
    {
        var fileName = info.Name;
        if (IsIgnoredName(fileName)) return null;
        var attributes = info.Attributes;
        if ((attributes & (FileAttributes.Hidden | FileAttributes.System)) != 0) return null;
        bool isFolder = (attributes & FileAttributes.Directory) != 0;
        var full = info.FullName;
        return new DesktopEntry("fs:" + full.ToLowerInvariant(), DisplayName(fileName, isFolder), full, isFolder, IsSpecial: false);
    }

    internal static bool IsIgnoredName(string fileName)
        => fileName.Equals("desktop.ini", StringComparison.OrdinalIgnoreCase) || fileName.Equals("thumbs.db", StringComparison.OrdinalIgnoreCase);

    /// <summary>Name as Explorer shows it: shortcuts (.lnk) and internet shortcuts (.url) without their extension, everything else verbatim.</summary>
    internal static string DisplayName(string fileName, bool isFolder)
    {
        if (isFolder) return fileName;
        var ext = Path.GetExtension(fileName);
        if (ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
        {
            var bare = fileName[..^ext.Length];
            return bare.Length > 0 ? bare : fileName;
        }
        return fileName;
    }

    // ------------------------------------------------------------------ localised names of the specials

    private string SpecialName(string parsingName, ref string? cache, string english, string russian)
    {
        if (cache != null) return cache;
        string? name = null;
        try { name = _specialNameResolver(parsingName); }
        catch (Exception ex) { _log.LogDebug(ex, "Cannot resolve the shell name of {Name}", parsingName); }
        // Fallback in the user's Windows display language (en / ru).
        name ??= CultureInfo.CurrentUICulture.TwoLetterISOLanguageName.Equals("ru", StringComparison.OrdinalIgnoreCase) ? russian : english;
        cache = name;
        return name;
    }

    /// <summary>The shell's own (localised) display name of a namespace item, resolved on an STA thread with a deadline.</summary>
    private string? ResolveShellName(string parsingName)
    {
        AppsStaWorker sta;
        lock (_gate)
        {
            if (_disposed) return null;
            sta = _sta ??= new AppsStaWorker("BW.DesktopFolder.STA");
        }
        var task = sta.RunAsync(() =>
        {
            var item = AppsCom.CreateShellItem(parsingName, out _);
            if (item == null) return null;
            try { return AppsCom.GetDisplayName(item, AppsShellIds.SigdnNormalDisplay); }
            finally { AppsCom.Release(item); }
        });
        return task.Wait(TimeSpan.FromSeconds(3)) ? task.Result : null;
    }

    // ------------------------------------------------------------------ change detection

    private void EnsureWatching()
    {
        lock (_gate)
        {
            if (_watching || _disposed) return;
            _watching = true;
            foreach (var folder in _folders)
            {
                try
                {
                    if (!Directory.Exists(folder)) continue;
                    var w = new FileSystemWatcher(folder)
                    {
                        IncludeSubdirectories = false,
                        // Names (create/delete/rename) and attributes (hidden toggled); content changes of files are irrelevant to the desktop listing.
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.Attributes,
                    };
                    w.Created += OnEvent;
                    w.Deleted += OnEvent;
                    w.Renamed += OnEvent;
                    w.Changed += OnEvent;
                    w.Error += (_, e) =>
                    {
                        _log.LogDebug(e.GetException(), "Desktop watcher error; scheduling a refresh");
                        Schedule();
                    };
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Cannot watch {Folder}", folder);
                }
            }
            _debounceTimer = new Timer(_ => Raise(), null, Timeout.Infinite, Timeout.Infinite);
        }
    }

    private void OnEvent(object sender, FileSystemEventArgs e)
    {
        if (e.Name != null && IsIgnoredName(Path.GetFileName(e.Name))) return; // Explorer rewrites desktop.ini constantly
        Schedule();
    }

    private void Schedule()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _debounceTimer?.Change(Debounce, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private void Raise()
    {
        Action? handlers;
        lock (_gate)
        {
            if (_disposed) return;
            handlers = _changed;
        }
        try { handlers?.Invoke(); }
        catch (Exception ex) { _log.LogError(ex, "A desktop Changed handler threw"); }
    }

    /// <summary>Stops watching.</summary>
    public void Dispose()
    {
        List<FileSystemWatcher> watchers;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            watchers = new List<FileSystemWatcher>(_watchers);
            _watchers.Clear();
            _debounceTimer?.Dispose();
            _sta?.Dispose();
            _changed = null;
        }
        foreach (var w in watchers)
        {
            try { w.EnableRaisingEvents = false; w.Dispose(); }
            catch (Exception) { }
        }
    }
}
