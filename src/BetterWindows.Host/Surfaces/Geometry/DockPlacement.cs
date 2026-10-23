using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>Everything the dock placement needs, in physical pixels.</summary>
/// <param name="Position">Docked edge (settings.dock.position).</param>
/// <param name="MonitorBoundsPx">Whole monitor; the window is always clamped inside it.</param>
/// <param name="CrossAxisAreaPx">Area used to centre the window ALONG the edge (normally the monitor work area: it excludes the menu bar for a left/right dock).</param>
/// <param name="EdgePx">Coordinate of the docked edge on the main axis (bottom: y of the bottom edge; left: x of the left edge; right: x of the right edge).</param>
/// <param name="WidthPx">Requested window width.</param>
/// <param name="HeightPx">Requested window height.</param>
internal readonly record struct DockPlacementInput(DockPosition Position, NativeRect MonitorBoundsPx, NativeRect CrossAxisAreaPx, int EdgePx, int WidthPx, int HeightPx);

/// <summary>
/// Pure dock geometry (docs/IPC.md section 5): the UI owns the window SIZE (surface.resize, bar + magnification headroom + tooltips), the host clamps it to the monitor
/// and keeps the window CENTRED on its edge. The AppBar reserves only the bar thickness; the headroom overlaps other windows on purpose.
/// </summary>
internal static class DockPlacement
{
    /// <summary>The bar thickness the UI uses when it has not (yet) reported one: icon size + 2 x 12 DIP padding.</summary>
    public static double DefaultBarThicknessDip(int iconSizeDip) => Math.Clamp(iconSizeDip, 32, 128) + 24.0;

    /// <summary>
    /// AppBar band thickness in physical px: ceil(barThickness x scale), at least 1 and at most half of the monitor along the docked axis (a hostile or buggy UI value must
    /// never reserve the whole screen).
    /// </summary>
    public static int ReservedThicknessPx(double barThicknessDip, double scale, DockPosition position, NativeRect monitorBoundsPx)
    {
        var raw = DpiMath.DipToPxCeil(barThicknessDip, scale);
        var axis = position == DockPosition.Bottom ? monitorBoundsPx.Height : monitorBoundsPx.Width;
        var max = Math.Max(1, axis / 2);
        return Math.Clamp(raw, 1, max);
    }

    /// <summary>Main-axis edge coordinate without an AppBar: the monitor bounds edge, or the work-area edge when the native taskbar stays visible (so the dock does not cover it).</summary>
    public static int EdgeWithoutAppBar(DockPosition position, NativeRect boundsPx, NativeRect workAreaPx, bool useWorkArea)
    {
        var r = useWorkArea ? workAreaPx : boundsPx;
        return position switch
        {
            DockPosition.Left => r.Left,
            DockPosition.Right => r.Right,
            _ => r.Bottom,
        };
    }

    /// <summary>Main-axis edge coordinate from the rectangle the AppBar returned from Dock(): its outer edge is where the window must sit.</summary>
    public static int EdgeFromAppBarRect(DockPosition position, NativeRect appBarRectPx) => position switch
    {
        DockPosition.Left => appBarRectPx.Left,
        DockPosition.Right => appBarRectPx.Right,
        _ => appBarRectPx.Bottom,
    };

    public static AppBarEdge ToAppBarEdge(DockPosition position) => position switch
    {
        DockPosition.Left => AppBarEdge.Left,
        DockPosition.Right => AppBarEdge.Right,
        _ => AppBarEdge.Bottom,
    };

    /// <summary>Window rectangle in virtual-screen px.</summary>
    public static NativeRect Compute(DockPlacementInput i)
    {
        var mon = i.MonitorBoundsPx;
        var w = Math.Clamp(i.WidthPx, 1, Math.Max(1, mon.Width));
        var h = Math.Clamp(i.HeightPx, 1, Math.Max(1, mon.Height));
        var area = MonitorGeometry.IsEmpty(i.CrossAxisAreaPx) ? mon : i.CrossAxisAreaPx;
        int left, top;
        switch (i.Position)
        {
            case DockPosition.Left:
                left = Math.Clamp(i.EdgePx, mon.Left, Math.Max(mon.Left, mon.Right - w));
                top = CentreStart(area.Top, area.Height, h);
                top = Math.Clamp(top, mon.Top, Math.Max(mon.Top, mon.Bottom - h));
                break;
            case DockPosition.Right:
                left = Math.Clamp(i.EdgePx - w, mon.Left, Math.Max(mon.Left, mon.Right - w));
                top = CentreStart(area.Top, area.Height, h);
                top = Math.Clamp(top, mon.Top, Math.Max(mon.Top, mon.Bottom - h));
                break;
            default:
                top = Math.Clamp(i.EdgePx - h, mon.Top, Math.Max(mon.Top, mon.Bottom - h));
                left = CentreStart(area.Left, area.Width, w);
                left = Math.Clamp(left, mon.Left, Math.Max(mon.Left, mon.Right - w));
                break;
        }
        return new NativeRect(left, top, left + w, top + h);
    }

    /// <summary>Start coordinate of a window of <paramref name="size"/> centred in [start, start+extent); rounds half away from zero so 1-px differences are symmetric.</summary>
    private static int CentreStart(int start, int extent, int size) => start + (int)Math.Round((extent - size) / 2.0, MidpointRounding.AwayFromZero);
}
