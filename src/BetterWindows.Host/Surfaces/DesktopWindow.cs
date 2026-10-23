using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// The desktop layer of one monitor: an opaque, region-less, never-activated tool window that draws the wallpaper, the desktop items and widgets. It is parented into the
/// wallpaper layer through <see cref="IDesktopIconsController.TryAttachDesktopLayer"/>; when the shell layout is not recognised it stays at the bottom of the z-order.
/// </summary>
internal sealed class DesktopWindow : SurfaceWindowBase
{
    private readonly IDesktopIconsController _icons;
    private NativeRect _attachedBounds;
    private bool _attached;

    public DesktopWindow(SurfaceId id, string route, MonitorInfo monitor, TimeProvider time, ILogger log, IDesktopIconsController icons)
        : base(new SurfaceWindowSpec(id, route, Layered: false, NoActivate: true, ToolWindow: true, Topmost: false, CompositionControl: false,
                                     GlassBackdrop: false, ManagesRegion: false), time, log, monitor)
    {
        _icons = icons;
    }

    /// <summary>True when the window is parented into the wallpaper layer (false: bottom-of-z-order fallback).</summary>
    public bool IsInWallpaperLayer => _attached;

    /// <summary>Places the window over its whole monitor and (re)attaches it to the desktop layer when the bounds changed or the shell was restarted.</summary>
    public void Place(bool force = false)
    {
        if (IsShutdown || Hwnd == 0) return;
        var bounds = MonitorGeometry.BoundsPx(Monitor);
        PlaceWithDpiFix(_ => bounds);
        if (!force && _attached && bounds == _attachedBounds) return;
        Attach(bounds);
    }

    /// <summary>Forgets the attachment so the next <see cref="Place"/> re-parents the window (Explorer restarted).</summary>
    public void Invalidate() => _attached = false;

    private void Attach(NativeRect bounds)
    {
        try
        {
            _attached = _icons.TryAttachDesktopLayer(Hwnd, bounds);
            _attachedBounds = bounds;
            if (!_attached)
            {
                // Unknown shell layout: keep the window at the bottom of the z-order (below every normal window) without activating it.
                WindowStyling.SetZOrder(Hwnd, WindowStyling.HwndBottom);
                Log.LogWarning("{Surface}: desktop layer not recognised; using the bottom of the z-order", SurfaceId);
            }
        }
        catch (Exception ex)
        {
            _attached = false;
            Log.LogWarning(ex, "{Surface}: attaching to the desktop layer failed", SurfaceId);
        }
    }

    protected override void OnShuttingDown()
    {
        if (!_attached || Hwnd == 0) return;
        try { _icons.DetachDesktopLayer(Hwnd); }
        catch (Exception ex) { Log.LogWarning(ex, "{Surface}: detaching from the desktop layer failed", SurfaceId); }
        _attached = false;
    }
}
