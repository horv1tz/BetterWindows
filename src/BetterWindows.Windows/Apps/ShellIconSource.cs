using System.Collections.Concurrent;
using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.System.Threading;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native;

/// <summary>
/// App / file / window icons as PNG. Apps and paths go through <c>IShellItemImageFactory::GetImage</c> (the same source Explorer and Start use, so packaged apps
/// get their tile and classic exes their best embedded size); windows use <c>WM_GETICON</c> -> class icon -> exe icon. All shell work runs on a small pool of
/// STA threads (2 concurrent, 3 s per call; a hung icon handler costs one retired thread, not the whole service). Results are small PNGs, cached in memory.
/// </summary>
public sealed class ShellIconSource : IIconSource, IDisposable
{
    private const int MaxConcurrency = 2;
    private const int MinSize = 16;
    private const int MaxSize = 1024;
    /// <summary>From this requested size on, image files get a real thumbnail instead of the generic file-type icon (preview use).</summary>
    internal const int ThumbnailMinSize = 192;
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan CacheTtl = TimeSpan.FromMinutes(10);
    private const int CacheCapacity = 256;

    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".webp", ".tif", ".tiff",
    };

    private readonly ILogger _log;
    private readonly StaPool _pool = new("BW.Icon.STA", MaxConcurrency);
    private readonly ConcurrentDictionary<string, CacheEntry> _cache = new(StringComparer.Ordinal);

    private sealed record CacheEntry(byte[] Png, DateTime StoredUtc);

    /// <summary>Creates the icon source.</summary>
    public ShellIconSource(ILogger<ShellIconSource>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public async Task<byte[]?> GetIconPngAsync(IconKey key, int size, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(key);
        ct.ThrowIfCancellationRequested();
        size = Math.Clamp(size, MinSize, MaxSize);

        var cacheKey = CacheKey(key, size);
        if (cacheKey != null && _cache.TryGetValue(cacheKey, out var hit) && DateTime.UtcNow - hit.StoredUtc < CacheTtl)
            return hit.Png;

        try
        {
            var png = await _pool.RunAsync(() => RenderSafe(key, size), CallTimeout, ct).ConfigureAwait(false);
            if (png != null && cacheKey != null) Store(cacheKey, png);
            return png;
        }
        catch (TimeoutException)
        {
            _log.LogWarning("Icon request timed out ({Key}, {Size}px)", Describe(key), size);
            return null;
        }
        catch (OperationCanceledException)
        {
            throw; // the caller cancelled: let it observe that
        }
        catch (ObjectDisposedException)
        {
            return null;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Icon request failed ({Key}, {Size}px)", Describe(key), size);
            return null;
        }
    }

    // ---------------------------------------------------------------- cache

    /// <summary>Window icons are not cached (handles are recycled and the app can change its icon at any time).</summary>
    internal static string? CacheKey(IconKey key, int size)
    {
        if (!string.IsNullOrWhiteSpace(key.Aumid)) return $"a|{key.Aumid!.Trim()}|{size}";
        if (!string.IsNullOrWhiteSpace(key.Path))
            return ShellTargetPolicy.SpecialParsingName(key.Path) != null ? null : $"p|{key.Path!.Trim().ToLowerInvariant()}|{size}"; // the Recycle Bin icon changes with its content
        return null;
    }

    private void Store(string cacheKey, byte[] png)
    {
        _cache[cacheKey] = new CacheEntry(png, DateTime.UtcNow);
        if (_cache.Count <= CacheCapacity) return;
        // Over capacity: drop the oldest quarter. Rare and cheap; correctness never depends on the cache.
        foreach (var kv in _cache.OrderBy(kv => kv.Value.StoredUtc).Take(CacheCapacity / 4))
            _cache.TryRemove(kv.Key, out _);
    }

    private static string Describe(IconKey key)
        => key.Aumid != null ? "aumid:" + key.Aumid : key.Path != null ? "path:" + key.Path : "window:" + key.WindowHandle;

    // ---------------------------------------------------------------- rendering (STA pool thread)

    private byte[]? RenderSafe(IconKey key, int size)
    {
        try { return Render(key, size); }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Rendering icon failed ({Key})", Describe(key));
            return null;
        }
    }

    private byte[]? Render(IconKey key, int size)
    {
        // "Exactly one of the identifying members is used, in this order": Aumid, Path, WindowHandle.
        if (!string.IsNullOrWhiteSpace(key.Aumid) && AppIdentity.IsValidAumid(key.Aumid))
        {
            var png = RenderShellItem(AppIdentity.AppsFolderParsingName(key.Aumid), size, IconFlags(size, isImage: false), trimPlate: true);
            if (png != null || string.IsNullOrWhiteSpace(key.Path)) return png;
            // The Apps-folder item vanished (uninstalled) but the caller also knows the exe: fall through to the path.
        }
        if (!string.IsNullOrWhiteSpace(key.Path))
        {
            // "This PC" / "Recycle Bin" desktop items: their icon (the bin's changes with its content) comes from the namespace item.
            var special = ShellTargetPolicy.SpecialParsingName(key.Path);
            if (special != null) return RenderShellItem(special, size, IconFlags(size, isImage: false), trimPlate: false);

            // Absolute local paths only: UNC paths would make the shell touch the network (slow, and an NTLM leak when the UI is compromised).
            var path = AppIdentity.NormalizeWindowsPath(key.Path);
            if (path == null) return null;
            bool image = size >= ThumbnailMinSize && IsImageFile(path);
            return RenderShellItem(path, size, IconFlags(size, image), trimPlate: false);
        }
        if (key.WindowHandle is long hwnd) return RenderWindowIcon(hwnd, size);
        return null;
    }

    internal static bool IsImageFile(string path) => ImageExtensions.Contains(AppIdentity.ExtensionOf(path));

    /// <summary>
    /// SIIGBF_ICONONLY | SIIGBF_BIGGERSIZEOK (SIIGBF_RESIZETOFIT is the 0 default): icon, never a thumbnail, and the shell may hand back a larger cached size
    /// that we scale down ourselves with a proper filter rather than getting an upscaled small icon. For image files at preview sizes, drop ICONONLY to get the thumbnail
    /// (the shell falls back to the icon by itself when no thumbnail can be produced).
    /// </summary>
    internal static uint IconFlags(int size, bool isImage)
        => isImage && size >= ThumbnailMinSize
            ? AppsShellIds.SiigbfResizeToFit | AppsShellIds.SiigbfBiggerSizeOk
            : AppsShellIds.SiigbfResizeToFit | AppsShellIds.SiigbfBiggerSizeOk | AppsShellIds.SiigbfIconOnly;

    private byte[]? RenderShellItem(string parsingName, int size, uint flags, bool trimPlate)
    {
        IAppsShellItemImageFactory? factory = AppsCom.CreateImageFactory(parsingName, out int hr);
        if (factory == null)
        {
            _log.LogDebug("No shell item for {Name}: 0x{Hr:X8}", parsingName, hr);
            return null;
        }
        nint hbm = 0;
        try
        {
            hr = factory.GetImage(new AppsSize { Cx = size, Cy = size }, flags, out hbm);
            if (hr < 0 || hbm == 0)
            {
                _log.LogDebug("GetImage failed for {Name}: 0x{Hr:X8}", parsingName, hr);
                return null;
            }
            if (!AppsGdi.TryReadBitmap(hbm, out int w, out int h, out byte[] bgra)) return null;
            return Finish(bgra, w, h, size, alreadyPremultiplied: null, trimPlate);
        }
        finally
        {
            AppsGdi.DeleteBitmap(hbm); // GetImage hands over ownership of the HBITMAP
            AppsCom.Release(factory);
        }
    }

    /// <summary>
    /// Raw pixels -> final PNG. <paramref name="alreadyPremultiplied"/> null means "unknown shell bitmap: detect the alpha convention"; true means the buffer is premultiplied BGRA.
    /// Pipeline: alpha normalisation -> optional plate trim -> box downscale to the requested size (never upscale) -> straight alpha -> PNG.
    /// </summary>
    internal static byte[]? Finish(byte[] bgra, int w, int h, int targetSize, bool? alreadyPremultiplied, bool trimPlate)
    {
        if (w <= 0 || h <= 0 || bgra.Length < w * h * 4) return null;

        if (alreadyPremultiplied != true)
            IconImage.NormalizeToPremultiplied(bgra, IconImage.DetectAlpha(bgra));

        if (trimPlate && IconImage.TryFindFlatPlate(bgra, w, h, out int cx, out int cy, out int cw, out int ch))
        {
            bgra = IconImage.Crop(bgra, w, cx, cy, cw, ch);
            w = cw;
            h = ch;
        }

        int longest = Math.Max(w, h);
        if (longest > targetSize)
        {
            int tw = Math.Max(1, (int)Math.Round((double)w * targetSize / longest));
            int th = Math.Max(1, (int)Math.Round((double)h * targetSize / longest));
            bgra = IconImage.ResizeBoxPremultiplied(bgra, w, h, tw, th, out w, out h);
        }

        IconImage.Unpremultiply(bgra);
        return PngEncoder.EncodeBgra(bgra, w, h);
    }

    // ---------------------------------------------------------------- windows

    private byte[]? RenderWindowIcon(long handle, int size)
    {
        var hwnd = new HWND((nint)handle);
        if (!PInvoke.IsWindow(hwnd)) return null;

        // 1. The icon the window itself advertises. WM_GETICON goes to the target's thread, so it must not block on a hung app: SMTO_ABORTIFHUNG + short timeout.
        nint icon = QueryIconMessage(hwnd, 1 /* ICON_BIG */);
        if (icon == 0) icon = QueryIconMessage(hwnd, 2 /* ICON_SMALL2: the system-chosen small icon, falls back to the class icon */);
        if (icon == 0) icon = QueryIconMessage(hwnd, 0 /* ICON_SMALL */);
        // 2. The window class icons.
        if (icon == 0) icon = (nint)PInvoke.GetClassLongPtr(hwnd, GET_CLASS_LONG_INDEX.GCLP_HICON);
        if (icon == 0) icon = (nint)PInvoke.GetClassLongPtr(hwnd, GET_CLASS_LONG_INDEX.GCLP_HICONSM);

        if (icon != 0 && AppsGdi.TryRenderIcon(icon, out int w, out int h, out byte[] premul))
            return Finish(premul, w, h, size, alreadyPremultiplied: true, trimPlate: false);

        // 3. The icon of the process image (the HICON from the window is owned by the window and must not be destroyed by us; the shell item path has no such ownership issue).
        var exe = TryGetProcessImagePath(hwnd);
        if (exe != null && !AppIdentity.FileNameOf(exe).Equals("ApplicationFrameHost.exe", StringComparison.OrdinalIgnoreCase))
        {
            var path = AppIdentity.NormalizeWindowsPath(exe);
            if (path != null) return RenderShellItem(path, size, IconFlags(size, isImage: false), trimPlate: false);
        }
        return null;
    }

    private static unsafe nint QueryIconMessage(HWND hwnd, int iconType)
    {
        nuint result = 0;
        LRESULT sent = PInvoke.SendMessageTimeout(
            hwnd, PInvoke.WM_GETICON, new WPARAM((nuint)iconType), new LPARAM(0),
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_NORMAL, 250, &result);
        return sent.Value == 0 ? 0 : (nint)result; // 0 = failed or timed out
    }

    private static unsafe string? TryGetProcessImagePath(HWND hwnd)
    {
        uint pid = 0;
        if (PInvoke.GetWindowThreadProcessId(hwnd, &pid) == 0 || pid == 0) return null;
        // PROCESS_QUERY_LIMITED_INFORMATION is enough for QueryFullProcessImageName and is granted even for most elevated/protected processes.
        HANDLE process = PInvoke.OpenProcess(PROCESS_ACCESS_RIGHTS.PROCESS_QUERY_LIMITED_INFORMATION, false, pid);
        if (process.IsNull) return null;
        try
        {
            Span<char> buffer = stackalloc char[1024];
            uint len = (uint)buffer.Length;
            fixed (char* p = buffer)
            {
                if (!PInvoke.QueryFullProcessImageName(process, PROCESS_NAME_FORMAT.PROCESS_NAME_WIN32, new PWSTR(p), &len)) return null;
            }
            return len == 0 ? null : new string(buffer[..(int)len]);
        }
        finally
        {
            PInvoke.CloseHandle(process);
        }
    }

    /// <summary>Releases the STA pool.</summary>
    public void Dispose() => _pool.Dispose();
}
