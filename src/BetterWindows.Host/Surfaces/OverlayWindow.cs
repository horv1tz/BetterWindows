using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// The overlay: one fully transparent, layered, topmost window that covers a whole monitor and hosts Spotlight, Launchpad, Mission Control and every popover.
/// It is the ONLY activatable surface (keyboard focus for search). It is created hidden at startup so its WebView is ready when the first overlay.open arrives.
/// </summary>
internal sealed class OverlayWindow : SurfaceWindowBase
{
    public OverlayWindow(SurfaceId id, MonitorInfo monitor, TimeProvider time, ILogger log)
        : base(new SurfaceWindowSpec(id, "overlay", Layered: true, NoActivate: false, ToolWindow: true, Topmost: true, CompositionControl: true,
                                     GlassBackdrop: false, ManagesRegion: false), time, log, monitor)
    {
    }

    /// <summary>The overlay is dismissed by Alt+F4 through the owner (the window itself never closes).</summary>
    protected override bool AllowUserClose => false;

    /// <summary>Covers the whole monitor, shows it and gives it the keyboard. The region stays clear (the overlay is see-through and catches clicks everywhere).</summary>
    public void Present(MonitorInfo monitor)
    {
        if (IsShutdown) return;
        Monitor = monitor;
        ContentHidden = false;
        PlaceWithDpiFix(_ => MonitorGeometry.BoundsPx(monitor));
        try
        {
            if (!IsVisible) Show();
            Activate();
            ForegroundHelper.TryActivate(Hwnd);
        }
        catch (Exception ex) { Log.LogWarning(ex, "{Surface}: could not present the overlay", SurfaceId); }
    }

    /// <summary>Hides the window. The WebView keeps running so the next show is instant.</summary>
    public void Dismiss()
    {
        ContentHidden = true;
        HideWindow();
    }

    /// <summary>Asks the system again for the foreground (after a deactivation the grace period wants to undo).</summary>
    public void Reactivate()
    {
        if (IsShutdown || Hwnd == 0) return;
        try { Activate(); ForegroundHelper.TryActivate(Hwnd); }
        catch (Exception ex) { Log.LogDebug(ex, "{Surface}: reactivation failed", SurfaceId); }
    }

    /// <summary>True when this window is the foreground window of the system.</summary>
    public bool IsForeground => Hwnd != 0 && ForegroundHelper.GetForeground() == Hwnd;
}
