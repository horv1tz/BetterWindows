using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Resources;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Services;

/// <summary>
/// Serves the host's in-memory <c>/dyn/*</c> URL space (docs/IPC.md section 8): icons (catalog / window / path), window thumbnails, media cover art, wallpapers and tray icons.
/// Rules enforced here: strict URL parsing (anything unknown is null = 404), window handles must be tracked by <see cref="IWindowTracker"/> (own windows never are), wallpapers come
/// ONLY from settings / the OS wallpaper service (never a path from the URL) and need a matching extension, matching magic bytes and a size cap, only png/jpeg/webp/gif/bmp are
/// ever served (decided from the bytes), sizes are bucketed (icons 32..512, thumbnails 160..960 px), cover art and wallpapers pass through an <see cref="IImageSanitizer"/>
/// (re-encoding; the default only lets through PNG/JPEG), thumbnails are served only while the overlay is (or just was) visible, and path/exe icon work has a 3 s timeout off the
/// caller's thread. Platform ports are resolved lazily and individually, so one broken port only takes down its own URLs. Never throws: failures return null and are logged.
/// </summary>
public sealed class DynamicResourceRouter : IDynamicResourceProvider
{
    /// <summary>Icon generation (shell calls) gives up after this long.</summary>
    public static readonly TimeSpan IconTimeout = TimeSpan.FromSeconds(3);
    /// <summary>Thumbnails are still served this long after the overlay hid (the exit animation, in-flight requests).</summary>
    public static readonly TimeSpan OverlayGrace = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan ImmutableAge = TimeSpan.FromDays(365), ShortAge = TimeSpan.FromSeconds(60);
    private const long MaxWallpaperBytes = 64L * 1024 * 1024;
    private const int MaxIconBytes = 4 * 1024 * 1024, MaxPathChars = 4096, MaxConcurrentIconJobs = 4;
    private const int ImagePreviewSize = 192;

    private readonly IServiceProvider _services;
    private readonly IAppCatalog _catalog;
    private readonly IWindowTracker _tracker;
    private readonly ISettingsService _settings;
    private readonly TimeProvider _time;
    private readonly IOverlayState _overlay;
    private readonly IImageSanitizer _sanitizer;
    private readonly ILogger _log;
    private readonly IconStore _icons;
    private readonly ThumbnailStore _thumbs;
    private readonly SemaphoreSlim _iconJobs = new(MaxConcurrentIconJobs, MaxConcurrentIconJobs);
    private readonly HashSet<Type> _reportedBroken = new();
    private readonly object _overlayLock = new();
    private bool _overlayVisible;
    private long _overlayHiddenAt;
    private bool _overlayEverHidden;

    public DynamicResourceRouter(
        IServiceProvider services, IAppCatalog catalog, IWindowTracker tracker, ISettingsService settings, IAppPaths paths, TimeProvider time, IOverlayState overlay,
        ILogger<DynamicResourceRouter>? log = null, IImageSanitizer? sanitizer = null)
    {
        _services = services;
        _catalog = catalog;
        _tracker = tracker;
        _settings = settings;
        _time = time;
        _overlay = overlay;
        _sanitizer = sanitizer ?? services.GetService<IImageSanitizer>() ?? new PassThroughImageSanitizer();
        _log = (ILogger?)log ?? NullLogger.Instance;
        _icons = new IconStore(paths, time, _log);
        _thumbs = new ThumbnailStore(time, () => Port<IWindowCapture>(), h => _tracker.Find(h) is not null, _log);
        _overlayVisible = overlay.Current.Visible;
        _overlay.Changed += OnOverlayChanged;
    }

    /// <summary>When true (default, per docs/IPC.md section 8) thumbnails are only served while the overlay is visible or was hidden less than <see cref="OverlayGrace"/> ago.</summary>
    public bool RequireVisibleOverlayForThumbnails { get; set; } = true;

    // ---- entry points --------------------------------------------------------------------------------------------------------------------------------

    public async Task<DynResource?> GetAsync(string pathAndQuery, CancellationToken ct = default)
    {
        try
        {
            if (!DynRequestParser.TryParse(pathAndQuery, out var request)) return null;
            return request switch
            {
                AppIconRequest r => await AppIconAsync(r, ct).ConfigureAwait(false),
                WindowIconRequest r => await WindowIconAsync(r, ct).ConfigureAwait(false),
                PathIconRequest r => await PathIconAsync(r, ct).ConfigureAwait(false),
                ThumbRequest r => await ThumbAsync(r, ct).ConfigureAwait(false),
                ArtRequest r => await ArtAsync(r, ct).ConfigureAwait(false),
                WallpaperRequest r => await WallpaperAsync(r, ct).ConfigureAwait(false),
                TrayIconRequest r => await TrayIconAsync(r, ct).ConfigureAwait(false),
                _ => null,
            };
        }
        catch (OperationCanceledException) { return null; }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Serving {Url} failed", Truncate(pathAndQuery));
            return null;
        }
    }

    public async Task PrewarmWindowThumbnailAsync(long handle, CancellationToken ct = default)
    {
        try { await _thumbs.PrewarmAsync(handle, ct).ConfigureAwait(false); }
        catch (Exception ex) { _log.LogDebug(ex, "Thumbnail prewarm failed"); }
    }

    // ---- icons ---------------------------------------------------------------------------------------------------------------------------------------

    private async Task<DynResource?> AppIconAsync(AppIconRequest r, CancellationToken ct)
    {
        if (!InputPolicy.TryParseAppId(r.AppId, out var kind, out var value)) return null;
        IconKey? key = null;
        string? stampPath = null;
        var identity = r.AppId.ToLowerInvariant();
        if (kind == AppIdKind.Directory)
        {
            key = new IconKey { Path = value };
            stampPath = value;
        }
        else
        {
            var entry = _catalog.FindById(kind == AppIdKind.ExePath ? "exe:" + value : r.AppId);
            if (entry is not null)
            {
                key = new IconKey { Aumid = entry.Aumid, Path = entry.Path };
                if (key.Aumid is null && key.Path is null) key = null;
                identity = entry.Id.ToLowerInvariant();
                stampPath = entry.Path;
            }
            else
            {
                // not installed (or not scanned yet) but running: use what the window tracker knows about the process
                var running = _tracker.Snapshot.Windows.FirstOrDefault(w => string.Equals(w.AppId, r.AppId, StringComparison.OrdinalIgnoreCase));
                if (running is not null)
                {
                    key = running.Raw.Aumid is null && running.Raw.ExePath is null
                        ? new IconKey { WindowHandle = running.Handle }
                        : new IconKey { Aumid = running.Raw.Aumid, Path = running.Raw.ExePath };
                    stampPath = running.Raw.ExePath;
                }
            }
        }
        if (key is null) return null;

        var cacheKey = $"app|{identity}|{r.Size}|{Stamp(stampPath)}";
        var png = _icons.TryGet(cacheKey, useDisk: true);
        if (png is null)
        {
            png = await GenerateIconAsync(key, r.Size, ct).ConfigureAwait(false);
            if (png is null) return null;
            _icons.Put(cacheKey, png, useDisk: true);
        }
        return IconResource(png, r.Rev);
    }

    private async Task<DynResource?> WindowIconAsync(WindowIconRequest r, CancellationToken ct)
    {
        if (_tracker.Find(r.Handle) is null) return null; // unknown handles and our own windows
        var png = await GenerateIconAsync(new IconKey { WindowHandle = r.Handle }, r.Size, ct).ConfigureAwait(false);
        return png is null ? null : new DynResource(png, ImageFormats.Sniff(png)!, null); // window icons change at any time: no-store
    }

    private async Task<DynResource?> PathIconAsync(PathIconRequest r, CancellationToken ct)
    {
        if (r.Path.Length > MaxPathChars || !InputPolicy.TryNormalizeLocalPath(r.Path, out var path)) return null; // relative, UNC, device and traversal-to-nowhere paths end here
        // File-system probes can stall on dead network drives: they run inside the same timeout as the shell call.
        using var timeout = new CancellationTokenSource(IconTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        byte[]? png;
        var imagePreview = r.Size >= ImagePreviewSize && ImageFormats.IsImageFileName(path); // the shell returns a thumbnail of the picture itself: served unchanged, never kept on disk
        try
        {
            var probe = Task.Run(() => (Exists: File.Exists(path) || Directory.Exists(path), Stamp: Stamp(path)), CancellationToken.None);
            var (exists, stamp) = await probe.WaitAsync(linked.Token).ConfigureAwait(false);
            if (!exists) return null;
            var cacheKey = $"path|{path.ToLowerInvariant()}|{r.Size}|{stamp}";
            png = _icons.TryGet(cacheKey, useDisk: !imagePreview);
            if (png is null)
            {
                png = await GenerateIconAsync(new IconKey { Path = path }, r.Size, linked.Token).ConfigureAwait(false);
                if (png is null) return null;
                _icons.Put(cacheKey, png, useDisk: !imagePreview);
            }
        }
        catch (OperationCanceledException) { return null; }
        return IconResource(png, r.Rev);
    }

    private static DynResource IconResource(byte[] png, string? rev) => new(png, ImageFormats.Sniff(png)!, rev is null ? ShortAge : ImmutableAge);

    /// <summary>Asks the icon port off the caller's thread with a timeout and a concurrency limit; the result must be an allowlisted image or it is dropped.</summary>
    private async Task<byte[]?> GenerateIconAsync(IconKey key, int size, CancellationToken ct)
    {
        var source = Port<IIconSource>();
        if (source is null) return null;
        using var timeout = new CancellationTokenSource(IconTimeout, _time);
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, timeout.Token);
        try
        {
            await _iconJobs.WaitAsync(linked.Token).ConfigureAwait(false);
            try
            {
                var work = Task.Run(() => source.GetIconPngAsync(key, size, linked.Token), CancellationToken.None);
                byte[]? bytes;
                try { bytes = await work.WaitAsync(linked.Token).ConfigureAwait(false); }
                catch (OperationCanceledException)
                {
                    _ = work.ContinueWith(t => _ = t.Exception, TaskContinuationOptions.OnlyOnFaulted); // observe a late failure of the abandoned job
                    throw;
                }
                if (bytes is null || bytes.Length == 0 || bytes.Length > MaxIconBytes) return null;
                if (ImageFormats.Sniff(bytes) is null) { _log.LogWarning("The icon source returned bytes that are not an allowed image type; dropped"); return null; }
                return bytes;
            }
            finally { _iconJobs.Release(); }
        }
        catch (OperationCanceledException)
        {
            if (!ct.IsCancellationRequested) _log.LogDebug("Icon generation timed out for {Key}", key);
            return null;
        }
    }

    /// <summary>Staleness stamp of a file-system target (changes when the file changes); "-" for things without a path.</summary>
    private static string Stamp(string? path)
    {
        if (string.IsNullOrEmpty(path)) return "-";
        try
        {
            var fi = new FileInfo(path);
            return fi.Exists ? $"{fi.LastWriteTimeUtc.Ticks}:{fi.Length}" : "-";
        }
        catch { return "-"; }
    }

    // ---- thumbnails ----------------------------------------------------------------------------------------------------------------------------------

    private async Task<DynResource?> ThumbAsync(ThumbRequest r, CancellationToken ct)
    {
        if (_tracker.Find(r.Handle) is null) return null; // only windows the tracker knows, never our own
        if (RequireVisibleOverlayForThumbnails && !OverlayAllowsThumbnails()) return null;
        var png = await _thumbs.GetAsync(r.Handle, r.Width, r.Rev, ct).ConfigureAwait(false);
        return png is null ? null : new DynResource(png, ImageFormats.Sniff(png)!, null);
    }

    private void OnOverlayChanged(OverlayState state)
    {
        lock (_overlayLock)
        {
            if (state.Visible) { _overlayVisible = true; return; }
            if (_overlayVisible || !_overlayEverHidden) { _overlayHiddenAt = _time.GetTimestamp(); _overlayEverHidden = true; }
            _overlayVisible = false;
        }
    }

    private bool OverlayAllowsThumbnails()
    {
        if (_overlay.Current.Visible) return true;
        lock (_overlayLock)
        {
            if (_overlayVisible) return true; // a show whose state update is still in flight
            return _overlayEverHidden && _time.GetElapsedTime(_overlayHiddenAt) < OverlayGrace;
        }
    }

    // ---- cover art / wallpaper / tray ----------------------------------------------------------------------------------------------------------------

    private async Task<DynResource?> ArtAsync(ArtRequest r, CancellationToken ct)
    {
        var media = Port<IMediaSource>();
        if (media is null) return null;
        var art = await media.GetArtAsync(ct).ConfigureAwait(false);
        if (art is not { } a || a.Data is null) return null;
        // The declared content type is ignored on purpose: media apps control these bytes.
        var clean = await _sanitizer.SanitizeAsync(a.Data, ImageLimits.CoverArt, ct).ConfigureAwait(false);
        return Serve(clean);
    }

    private async Task<DynResource?> WallpaperAsync(WallpaperRequest r, CancellationToken ct)
    {
        var monitors = Port<IMonitorProvider>();
        if (monitors is not null && !monitors.GetMonitors().Any(m => string.Equals(m.Id, r.MonitorId, StringComparison.Ordinal))) return null;

        // Candidates in priority order; the URL never names a file. Settings paths are UI-writable, so they need an image extension; the OS service path may be extension-less
        // (TranscodedWallpaper is a JPEG without extension) but is held to the same magic-byte check.
        var desktop = _settings.Current.Desktop;
        var candidates = new List<(string Path, bool FromSettings)>();
        if (desktop.Wallpapers.TryGetValue(r.MonitorId, out var perMonitor)) candidates.Add((perMonitor, true));
        if (!string.IsNullOrEmpty(desktop.WallpaperPath)) candidates.Add((desktop.WallpaperPath, true));
        var os = Port<IWallpaperService>();
        if (os is not null)
        {
            string? osPath = null;
            try { osPath = os.GetWallpaperPath(r.MonitorId); } catch (Exception ex) { _log.LogDebug(ex, "GetWallpaperPath failed"); }
            if (!string.IsNullOrEmpty(osPath)) candidates.Add((osPath, false));
        }

        foreach (var (raw, fromSettings) in candidates)
        {
            var served = await TryServeWallpaperAsync(raw, fromSettings, ct).ConfigureAwait(false);
            if (served is not null) return served;
        }
        return null;
    }

    private async Task<DynResource?> TryServeWallpaperAsync(string raw, bool fromSettings, CancellationToken ct)
    {
        if (raw.Length > MaxPathChars || !InputPolicy.TryNormalizeLocalPath(raw, out var path)) return null;
        var expected = ImageFormats.ContentTypeForExtension(path);
        if (fromSettings && expected is null) return null;
        var data = await Task.Run(() =>
        {
            var fi = new FileInfo(path);
            if (!fi.Exists || fi.Length == 0 || fi.Length > MaxWallpaperBytes) return null;
            return File.ReadAllBytes(path);
        }, ct).ConfigureAwait(false);
        if (data is null) return null;
        var sniffed = ImageFormats.Sniff(data);
        if (sniffed is null || (expected is not null && sniffed != expected)) { _log.LogWarning("Wallpaper {Path} is not a valid {Type}; refused", path, expected); return null; }
        var clean = await _sanitizer.SanitizeAsync(data, ImageLimits.Wallpaper, ct).ConfigureAwait(false);
        return Serve(clean);
    }

    private async Task<DynResource?> TrayIconAsync(TrayIconRequest r, CancellationToken ct)
    {
        var tray = Port<ITrayHost>();
        if (tray is null || !tray.IsSupported) return null;
        if (!tray.Icons.Any(i => string.Equals(i.Id, r.Id, StringComparison.Ordinal))) return null; // only icons the host itself reported
        var png = await tray.GetIconPngAsync(r.Id, ct).ConfigureAwait(false);
        if (png is null || png.Length == 0 || png.Length > MaxIconBytes || ImageFormats.Sniff(png) is not { } type) return null;
        return new DynResource(png, type, null);
    }

    private static DynResource? Serve(SanitizedImage? image) =>
        image is null || ImageFormats.Sniff(image.Data) is not { } type ? null : new DynResource(image.Data, type, null);

    // ---- plumbing ------------------------------------------------------------------------------------------------------------------------------------

    /// <summary>Resolves a platform port on demand; a port that cannot be constructed is reported once and treated as absent.</summary>
    private T? Port<T>() where T : class
    {
        try { return _services.GetService<T>(); }
        catch (Exception ex)
        {
            bool first;
            lock (_reportedBroken) first = _reportedBroken.Add(typeof(T));
            if (first) _log.LogError(ex, "Platform port {Port} could not be created; the URLs that need it return 404", typeof(T).Name);
            return null;
        }
    }

    private static string Truncate(string s) => InputPolicy.Truncate(s, 200);
}
