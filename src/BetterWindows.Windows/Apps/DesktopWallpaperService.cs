using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native;

/// <summary>
/// Reads and sets the desktop wallpaper per monitor through <c>IDesktopWallpaper</c> (CLSID_DesktopWallpaper, Windows 8+), the supported replacement for
/// SystemParametersInfo(SPI_SETDESKWALLPAPER): it addresses monitors by device path, which we map to <c>MonitorInfo.id</c> with <see cref="MonitorIdentity"/>.
/// The COM object lives on a dedicated STA thread and is created per call (cheap) so a failure never leaves a stale proxy behind.
/// Solid colour and slideshow backgrounds read as "no file" (null).
/// </summary>
public sealed class DesktopWallpaperService : IWallpaperService, IDisposable
{
    private static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(10);
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".jfif", ".png", ".bmp", ".dib", ".gif", ".tif", ".tiff", ".webp", ".heic", ".avif", ".wdp", ".jxr",
    };

    private readonly ILogger _log;
    private readonly object _gate = new();
    private StaWorker? _sta;
    private bool _disposed;

    /// <summary>Creates the service.</summary>
    public DesktopWallpaperService(ILogger<DesktopWallpaperService>? logger = null)
    {
        _log = (ILogger?)logger ?? NullLogger.Instance;
    }

    /// <inheritdoc />
    public string? GetWallpaperPath(string monitorId)
    {
        if (string.IsNullOrWhiteSpace(monitorId)) return null;
        try { return Run(() => GetCore(monitorId)); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading the wallpaper failed");
            return null;
        }
    }

    /// <inheritdoc />
    public void SetWallpaper(string path, string? monitorId)
    {
        try
        {
            var normalized = ValidateImagePath(path);
            if (normalized == null)
            {
                _log.LogWarning("Refusing to set wallpaper: not an existing absolute local image file");
                return;
            }
            Run(() => SetCore(normalized, monitorId));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Setting the wallpaper failed");
        }
    }

    /// <summary>Absolute local path of an existing image file, or null. Pure apart from the existence probe.</summary>
    internal static string? ValidateImagePath(string? path, Func<string, bool>? fileExists = null)
    {
        var p = AppIdentity.NormalizeWindowsPath(path);
        if (p == null || !ImageExtensions.Contains(AppIdentity.ExtensionOf(p))) return null;
        return (fileExists ?? File.Exists)(p) ? p : null;
    }

    private string? GetCore(string monitorId)
    {
        var dw = CreateWallpaper();
        if (dw == null) return null;
        try
        {
            var devicePath = FindDevicePath(dw, monitorId);
            if (devicePath == null) return null;
            int hr = dw.GetWallpaper(devicePath, out var p);
            var wallpaper = hr >= 0 ? AppsCom.TakeString(p) : null;
            // Empty string = solid colour (or a slideshow that has no current file); a stale path from the registry may point at a deleted file.
            return string.IsNullOrWhiteSpace(wallpaper) || !File.Exists(wallpaper) ? null : wallpaper;
        }
        finally
        {
            AppsCom.Release(dw);
        }
    }

    private bool SetCore(string path, string? monitorId)
    {
        var dw = CreateWallpaper();
        if (dw == null) return false;
        try
        {
            string? devicePath = null;
            if (monitorId != null)
            {
                devicePath = FindDevicePath(dw, monitorId);
                if (devicePath == null)
                {
                    _log.LogWarning("Unknown monitor id for SetWallpaper; nothing changed");
                    return false;
                }
            }
            // Position is a global (not per-monitor) setting; Fill = cover the monitor, crop the overflow (what a macOS wallpaper does).
            int hr = dw.SetPosition(AppsShellIds.DwposFill);
            if (hr < 0) _log.LogDebug("SetPosition failed: 0x{Hr:X8}", hr);
            // monitorID = NULL applies to every monitor.
            hr = dw.SetWallpaper(devicePath, path);
            if (hr < 0)
            {
                _log.LogWarning("IDesktopWallpaper.SetWallpaper failed: 0x{Hr:X8}", hr);
                return false;
            }
            return true;
        }
        finally
        {
            AppsCom.Release(dw);
        }
    }

    private IAppsDesktopWallpaper? CreateWallpaper()
    {
        var dw = AppsCom.CreateInstance<IAppsDesktopWallpaper>(AppsShellIds.ClsidDesktopWallpaper, typeof(IAppsDesktopWallpaper).GUID);
        if (dw == null) _log.LogInformation("IDesktopWallpaper is not available on this system");
        return dw;
    }

    /// <summary>Maps MonitorInfo.id back to the device path IDesktopWallpaper wants by hashing every monitor's path the same way the monitor provider did.</summary>
    private static string? FindDevicePath(IAppsDesktopWallpaper dw, string monitorId)
    {
        if (dw.GetMonitorDevicePathCount(out uint count) < 0) return null;
        for (uint i = 0; i < count; i++)
        {
            if (dw.GetMonitorDevicePathAt(i, out var p) < 0) continue;
            var devicePath = AppsCom.TakeString(p);
            // Detached monitors report an empty path; they have no id.
            if (string.IsNullOrEmpty(devicePath)) continue;
            if (string.Equals(MonitorIdentity.IdFromDevicePath(devicePath), monitorId, StringComparison.Ordinal)) return devicePath;
        }
        return null;
    }

    private T Run<T>(Func<T> work)
    {
        StaWorker sta;
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            sta = _sta ??= new StaWorker("BW.Wallpaper.STA");
        }
        var task = sta.RunAsync(work);
        if (!task.Wait(CallTimeout))
        {
            lock (_gate)
            {
                if (ReferenceEquals(_sta, sta)) _sta = null;
            }
            sta.Dispose();
            throw new TimeoutException("IDesktopWallpaper call timed out");
        }
        return task.GetAwaiter().GetResult();
    }

    /// <summary>Releases the STA thread.</summary>
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            _sta?.Dispose();
            _sta = null;
        }
    }
}
