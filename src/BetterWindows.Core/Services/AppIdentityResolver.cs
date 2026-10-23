using System.Collections.Concurrent;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.WindowTracking;

namespace BetterWindows.Core.Services;

/// <summary>
/// Maps a window to a stable app id + display name. Order: the window's own AUMID -> catalog; else its exe path -> catalog; else a synthesised identity
/// ("aumid:&lt;x&gt;" if an AUMID is known, else "exe:&lt;lower-cased path&gt;", name from the exe file name). Two special cases: ApplicationFrameWindow without any AUMID
/// cannot be attributed to an app and is grouped by "frame:&lt;class&gt;:&lt;pid&gt;" with the window title as name; a window whose process cannot be queried (elevated) and
/// has no AUMID is grouped by "proc:&lt;pid&gt;". Results that only depend on (aumid, exe) are cached; the cache is dropped whenever the catalog changes.
/// </summary>
public sealed class AppIdentityResolver : IAppIdentityResolver, IDisposable
{
    private const int MaxCacheEntries = 4096;
    private readonly IAppCatalog _catalog;
    private readonly ConcurrentDictionary<string, AppIdentity> _cache = new(StringComparer.Ordinal);
    private int _generation;

    public AppIdentityResolver(IAppCatalog catalog)
    {
        _catalog = catalog;
        _catalog.Changed += OnCatalogChanged;
    }

    private void OnCatalogChanged()
    {
        Interlocked.Increment(ref _generation);
        _cache.Clear();
    }

    public void Dispose() => _catalog.Changed -= OnCatalogChanged;

    public AppIdentity Resolve(RawWindow window)
    {
        var aumid = Clean(window.Aumid);
        var exe = AppNames.CleanPath(window.ExePath);

        if (aumid is null && IsFrameHost(window, exe))
            return new AppIdentity($"frame:{window.ClassName}:{window.ProcessId}", TitleOr(window, "Application"), null);

        if (aumid is null && exe is null)
            return new AppIdentity($"proc:{window.ProcessId}", TitleOr(window, string.IsNullOrWhiteSpace(window.ClassName) ? "Application" : window.ClassName), null);

        var key = (aumid?.ToLowerInvariant() ?? "") + "|" + (exe?.ToLowerInvariant() ?? "");
        if (_cache.TryGetValue(key, out var cached)) return cached;

        var generation = Volatile.Read(ref _generation);
        AppEntry? entry = null;
        try
        {
            if (aumid is not null) entry = _catalog.FindByAumid(aumid);
            if (entry is null && exe is not null) entry = _catalog.FindByExePath(exe);
        }
        catch (Exception) { entry = null; } // a misbehaving catalog must not take window tracking down

        AppIdentity identity;
        if (entry is not null)
            identity = new AppIdentity(entry.Id, string.IsNullOrWhiteSpace(entry.Name) ? NameFor(aumid, exe, window) : entry.Name, entry);
        else if (aumid is not null)
        {
            // AUMID known but not in the catalog: with an exe the name comes from the file, otherwise from the window (not cached: titles change).
            identity = new AppIdentity("aumid:" + aumid, NameFor(aumid, exe, window), null);
            if (exe is null) return identity;
        }
        else
            identity = new AppIdentity("exe:" + exe!.ToLowerInvariant(), NameFor(null, exe, window), null);

        // Only cache what was computed against the current catalog generation (a catalog refresh may have raced with the lookups above).
        if (generation == Volatile.Read(ref _generation))
        {
            if (_cache.Count >= MaxCacheEntries) _cache.Clear();
            _cache[key] = identity;
        }
        return identity;
    }

    private static string NameFor(string? aumid, string? exe, RawWindow w)
    {
        if (exe is not null) return AppNames.FromExePath(exe);
        var title = w.Title?.Trim();
        if (!string.IsNullOrEmpty(title)) return title;
        return aumid is not null ? AppNames.FromAumid(aumid) : "Application";
    }

    private static string TitleOr(RawWindow w, string fallback) => string.IsNullOrWhiteSpace(w.Title) ? fallback : w.Title.Trim();

    private static string? Clean(string? s)
    {
        var t = s?.Trim().Trim('"').Trim();
        return string.IsNullOrEmpty(t) ? null : t;
    }

    private static bool IsFrameHost(RawWindow w, string? exe) =>
        string.Equals(w.ClassName, "ApplicationFrameWindow", StringComparison.OrdinalIgnoreCase) || AppNames.IsFile(exe, "ApplicationFrameHost.exe");
}
