using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native;

/// <summary>
/// Enumerates the Start-menu app list: primarily <c>shell:AppsFolder</c> (every classic and packaged app with its AUMID, exactly what Start's "All apps" shows),
/// plus the .lnk files of both Start Menu folders as a fallback for shortcuts the Apps folder does not expose. All shell COM work happens on one dedicated STA thread.
/// Change detection: FileSystemWatchers on both Start Menu folders and a 10 minute re-scan (MSIX installs do not touch the folders), debounced to 1 s.
/// Never throws: failures are logged and the last good list (or an empty one) is returned.
/// </summary>
public sealed class ShellAppCatalogSource : IAppCatalogSource, IDisposable
{
    private static readonly TimeSpan Debounce = TimeSpan.FromSeconds(1);
    private static readonly TimeSpan PeriodicRecheck = TimeSpan.FromMinutes(10);

    private readonly ILogger _log;
    private readonly IReadOnlyList<string> _startMenuRoots;
    private readonly object _gate = new();
    private StaWorker? _sta;
    private readonly List<FileSystemWatcher> _watchers = new();
    private Timer? _debounceTimer;
    private Timer? _periodicTimer;
    private bool _monitoring;
    private bool _disposed;
    private IReadOnlyList<AppEntry>? _lastGood;
    private string? _lastSignature;
    private Action? _changed;

    /// <summary>Creates the source for the real Start Menu folders.</summary>
    public ShellAppCatalogSource(ILogger<ShellAppCatalogSource>? logger = null)
        : this(logger, StartMenuRootsFromEnvironment())
    {
    }

    internal ShellAppCatalogSource(ILogger? logger, IReadOnlyList<string> startMenuRoots)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
        _startMenuRoots = startMenuRoots;
    }

    /// <summary>Raised (debounced) when a Start Menu folder changes or the periodic re-scan finds a different list.</summary>
    public event Action? Changed
    {
        add
        {
            lock (_gate) _changed += value;
            EnsureMonitoring();
        }
        remove
        {
            lock (_gate) _changed -= value;
        }
    }

    /// <inheritdoc />
    public async Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default)
    {
        try
        {
            ct.ThrowIfCancellationRequested();
            EnsureMonitoring();
            var sta = GetWorker();
            var list = await sta.RunAsync(() => ScanCore(ct)).ConfigureAwait(false);
            var sig = Signature(list);
            lock (_gate)
            {
                _lastGood = list;
                _lastSignature = sig;
            }
            return list;
        }
        catch (OperationCanceledException)
        {
            return LastGoodOrEmpty();
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Scanning the Start menu app list failed");
            return LastGoodOrEmpty();
        }
    }

    private IReadOnlyList<AppEntry> LastGoodOrEmpty()
    {
        lock (_gate) return _lastGood ?? Array.Empty<AppEntry>();
    }

    private StaWorker GetWorker()
    {
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            return _sta ??= new StaWorker("BW.AppCatalog.STA");
        }
    }

    // ---------------------------------------------------------------- scanning (STA thread)

    private IReadOnlyList<AppEntry> ScanCore(CancellationToken ct)
    {
        var byId = new Dictionary<string, AppEntry>(StringComparer.OrdinalIgnoreCase);
        var knownPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var knownNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        try { ScanAppsFolder(ct, byId, knownPaths, knownNames); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.LogWarning(ex, "Enumerating shell:AppsFolder failed; falling back to Start menu shortcuts only"); }

        try { ScanShortcuts(ct, byId, knownPaths, knownNames); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { _log.LogWarning(ex, "Scanning Start menu shortcuts failed"); }

        return byId.Values
            .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
            .ThenBy(e => e.Id, StringComparer.Ordinal)
            .ToList();
    }

    private void ScanAppsFolder(CancellationToken ct, Dictionary<string, AppEntry> byId, HashSet<string> knownPaths, HashSet<string> knownNames)
    {
        var folderId = AppsShellIds.FolderIdAppsFolder;
        var iidItem = AppsShellIds.IidShellItem2;
        int hr = AppsShellNative.SHGetKnownFolderItem(ref folderId, 0, IntPtr.Zero, ref iidItem, out var pFolder);
        if (hr < 0)
        {
            _log.LogWarning("SHGetKnownFolderItem(AppsFolder) failed: 0x{Hr:X8}", hr);
            return;
        }

        IAppsShellItem2? folder = AppsCom.Wrap<IAppsShellItem2>(pFolder);
        IAppsEnumShellItems? enumerator = null;
        try
        {
            if (folder == null) return;
            var bhid = AppsShellIds.BhidEnumItems;
            var iidEnum = AppsShellIds.IidEnumShellItems;
            hr = folder.BindToHandler(IntPtr.Zero, ref bhid, ref iidEnum, out var pEnum);
            if (hr < 0)
            {
                _log.LogWarning("BindToHandler(BHID_EnumItems) on AppsFolder failed: 0x{Hr:X8}", hr);
                return;
            }
            enumerator = AppsCom.Wrap<IAppsEnumShellItems>(pEnum);
            if (enumerator == null) return;

            while (true)
            {
                ct.ThrowIfCancellationRequested();
                hr = enumerator.Next(1, out var pChild, out uint fetched);
                if (hr != 0 || fetched == 0 || pChild == IntPtr.Zero) // S_FALSE (1) = end of enumeration
                {
                    if (pChild != IntPtr.Zero) Marshal.Release(pChild);
                    break;
                }

                IAppsShellItem2? child = AppsCom.Wrap<IAppsShellItem2>(pChild);
                try
                {
                    if (child == null) continue;
                    var entry = ReadAppsFolderChild(child);
                    if (entry == null) continue;
                    // Duplicates of the same AUMID (or the same exe under two AUMIDs with identical name) are listed once; the first one wins.
                    if (!byId.TryAdd(entry.Id, entry)) continue;
                    if (entry.Path != null) knownPaths.Add(entry.Path);
                    knownNames.Add(entry.Name);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogDebug(ex, "Skipping an unreadable AppsFolder item");
                }
                finally
                {
                    AppsCom.Release(child);
                }
            }
        }
        finally
        {
            AppsCom.Release(enumerator);
            AppsCom.Release(folder);
        }
    }

    private static AppEntry? ReadAppsFolderChild(IAppsShellItem2 child)
    {
        var name = AppsCom.GetDisplayName(child, AppsShellIds.SigdnNormalDisplay) ?? AppsCom.GetString(child, AppsShellIds.ItemNameDisplay);
        var aumid = AppsCom.GetString(child, AppsShellIds.AppUserModelId);
        // Some items only expose their identity as the parsing name "shell:AppsFolder\<AUMID>" (or a file system path for plain shortcuts).
        var parsing = AppsCom.GetDisplayName(child, AppsShellIds.SigdnDesktopAbsoluteParsing);
        aumid ??= AppIdentity.AumidFromAppsFolderParsingName(parsing);

        var target = AppsCom.GetString(child, AppsShellIds.LinkTargetParsingPath);
        if (target == null && parsing != null && AppIdentity.AumidFromAppsFolderParsingName(parsing) == null
            && AppCatalogFilter.IsLaunchableTarget(parsing))
        {
            target = parsing; // the item IS an executable path
        }

        bool packaged = AppsCom.GetString(child, AppsShellIds.PackageFullName) != null
                        || AppsCom.GetString(child, AppsShellIds.PackageFamilyName) != null;
        return AppCatalogFilter.TryBuild(aumid, name, target, packaged);
    }

    private void ScanShortcuts(CancellationToken ct, Dictionary<string, AppEntry> byId, HashSet<string> knownPaths, HashSet<string> knownNames)
    {
        foreach (var root in _startMenuRoots)
        {
            var programs = Path.Combine(root, "Programs");
            if (!Directory.Exists(programs)) continue;

            IEnumerable<string> files;
            try
            {
                files = Directory.EnumerateFiles(programs, "*.lnk", new EnumerationOptions
                {
                    RecurseSubdirectories = true,
                    IgnoreInaccessible = true,
                    AttributesToSkip = FileAttributes.System,
                });
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Cannot enumerate {Folder}", programs);
                continue;
            }

            foreach (var lnk in files)
            {
                ct.ThrowIfCancellationRequested();
                try
                {
                    // The Start menu shows a shortcut under its file name: when AppsFolder already listed that name, loading the .lnk through COM would only find a duplicate.
                    if (knownNames.Contains(Path.GetFileNameWithoutExtension(lnk))) continue;
                    var entry = ReadShortcut(lnk);
                    if (entry == null) continue;
                    // Fallback only: skip anything AppsFolder already provided (same exe, same id, or the same display name).
                    if (entry.Path != null && knownPaths.Contains(entry.Path)) continue;
                    if (knownNames.Contains(entry.Name)) continue;
                    if (!byId.TryAdd(entry.Id, entry)) continue;
                    if (entry.Path != null) knownPaths.Add(entry.Path);
                    knownNames.Add(entry.Name);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    _log.LogDebug(ex, "Skipping unreadable shortcut {Lnk}", lnk);
                }
            }
        }
    }

    /// <summary>Reads one .lnk through IShellLinkW/IPersistFile. Only plain "run this exe" shortcuts (no arguments) count as apps.</summary>
    private static AppEntry? ReadShortcut(string lnkPath)
    {
        object? link = null;
        try
        {
            var iidLink = typeof(IAppsShellLink).GUID;
            var shellLink = AppsCom.CreateInstance<IAppsShellLink>(AppsShellIds.ClsidShellLink, iidLink);
            link = shellLink;
            if (shellLink == null) return null;
            var persist = (IAppsPersistFile)shellLink; // QueryInterface
            if (persist.Load(lnkPath, AppsShellIds.StgmRead) < 0) return null;

            // No Resolve(): it may search the disk or touch the network for moved targets. The stored path is what we want.
            var path = new StringBuilder(1024);
            if (shellLink.GetPath(path, path.Capacity, IntPtr.Zero, AppsShellIds.SlgpRawPath) < 0) return null;
            var args = new StringBuilder(1024);
            if (shellLink.GetArguments(args, args.Capacity) >= 0 && args.ToString().Trim().Length > 0) return null;

            var name = Path.GetFileNameWithoutExtension(lnkPath);
            return AppCatalogFilter.TryBuild(null, name, path.ToString(), packagedHint: false);
        }
        finally
        {
            AppsCom.Release(link);
        }
    }

    // ---------------------------------------------------------------- change detection

    private static IReadOnlyList<string> StartMenuRootsFromEnvironment()
    {
        var roots = new List<string>();
        void Add(Environment.SpecialFolder folder)
        {
            var baseDir = Environment.GetFolderPath(folder);
            if (!string.IsNullOrEmpty(baseDir)) roots.Add(Path.Combine(baseDir, "Microsoft", "Windows", "Start Menu"));
        }
        Add(Environment.SpecialFolder.CommonApplicationData); // %ProgramData%
        Add(Environment.SpecialFolder.ApplicationData);       // %AppData%
        return roots;
    }

    private void EnsureMonitoring()
    {
        lock (_gate)
        {
            if (_monitoring || _disposed) return;
            _monitoring = true;
            foreach (var root in _startMenuRoots)
            {
                try
                {
                    if (!Directory.Exists(root)) continue;
                    var w = new FileSystemWatcher(root)
                    {
                        IncludeSubdirectories = true,
                        NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite,
                    };
                    w.Created += OnFileEvent;
                    w.Deleted += OnFileEvent;
                    w.Renamed += OnFileEvent;
                    w.Changed += OnFileEvent;
                    w.Error += (_, e) =>
                    {
                        _log.LogDebug(e.GetException(), "Start menu watcher error; scheduling a re-scan");
                        ScheduleChanged();
                    };
                    w.EnableRaisingEvents = true;
                    _watchers.Add(w);
                }
                catch (Exception ex)
                {
                    _log.LogWarning(ex, "Cannot watch {Folder}", root);
                }
            }
            _debounceTimer = new Timer(_ => RaiseChanged(), null, Timeout.Infinite, Timeout.Infinite);
            _periodicTimer = new Timer(_ => _ = PeriodicRecheckAsync(), null, PeriodicRecheck, PeriodicRecheck);
        }
    }

    private void OnFileEvent(object sender, FileSystemEventArgs e)
    {
        // Only shortcut-ish changes matter; directory events (renamed folders) have no extension.
        var ext = Path.GetExtension(e.Name ?? "");
        if (ext.Length == 0 || ext.Equals(".lnk", StringComparison.OrdinalIgnoreCase) || ext.Equals(".url", StringComparison.OrdinalIgnoreCase))
            ScheduleChanged();
    }

    private void ScheduleChanged()
    {
        lock (_gate)
        {
            if (_disposed) return;
            try { _debounceTimer?.Change(Debounce, Timeout.InfiniteTimeSpan); }
            catch (ObjectDisposedException) { }
        }
    }

    private async Task PeriodicRecheckAsync()
    {
        try
        {
            string? previous;
            lock (_gate)
            {
                if (_disposed) return;
                previous = _lastSignature;
            }
            if (previous == null) return; // nobody has asked for the list yet: nothing can be stale
            var list = await GetWorker().RunAsync(() => ScanCore(CancellationToken.None)).ConfigureAwait(false);
            var sig = Signature(list);
            if (sig != previous) ScheduleChanged(); // Core then calls ScanAsync, which refreshes the cache
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Periodic Start menu re-check failed");
        }
    }

    private void RaiseChanged()
    {
        Action? handlers;
        lock (_gate)
        {
            if (_disposed) return;
            handlers = _changed;
        }
        try { handlers?.Invoke(); }
        catch (Exception ex) { _log.LogError(ex, "A catalog Changed handler threw"); }
    }

    /// <summary>Stable hash over what consumers see, so a re-scan that finds the same apps does not raise Changed.</summary>
    internal static string Signature(IReadOnlyList<AppEntry> list)
    {
        var sb = new StringBuilder();
        foreach (var e in list) sb.Append(e.Id).Append('\u001f').Append(e.Name).Append('\u001f').Append(e.Path).Append('\n');
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
    }

    /// <summary>Stops watching and releases the STA thread.</summary>
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
            _periodicTimer?.Dispose();
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
