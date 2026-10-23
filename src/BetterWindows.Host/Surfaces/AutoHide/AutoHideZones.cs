using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>Pure geometry of the auto-hide pointer zones (physical px, virtual-screen space).</summary>
internal static class AutoHideZones
{
    /// <summary>Thickness of the reveal strip. 2 px because the cursor can never reach the very last pixel row/column beyond the monitor edge.</summary>
    public const int StripPx = 2;

    /// <summary>Margin around the dock window within which the pointer still counts as "on the dock" (40 DIP).</summary>
    public const double KeepMarginDip = 40.0;

    /// <summary>
    /// The reveal strip along the docked edge: StripPx thick, as wide as the dock window or the centre third of the monitor edge, whichever is wider (both centred on the
    /// window's centre), clamped to the monitor.
    /// </summary>
    public static NativeRect DockRevealZone(DockPosition position, NativeRect monitorBoundsPx, NativeRect dockWindowPx)
    {
        var mon = monitorBoundsPx;
        if (position == DockPosition.Bottom)
        {
            var third = Math.Max(1, mon.Width / 3);
            var centre = MidX(dockWindowPx, mon);
            var half = Math.Max(third, Math.Max(1, dockWindowPx.Width)) / 2;
            return new NativeRect(Math.Max(mon.Left, centre - half), mon.Bottom - StripPx, Math.Min(mon.Right, centre + half), mon.Bottom);
        }
        var thirdV = Math.Max(1, mon.Height / 3);
        var centreY = MidY(dockWindowPx, mon);
        var halfV = Math.Max(thirdV, Math.Max(1, dockWindowPx.Height)) / 2;
        var top = Math.Max(mon.Top, centreY - halfV);
        var bottom = Math.Min(mon.Bottom, centreY + halfV);
        return position == DockPosition.Left
            ? new NativeRect(mon.Left, top, mon.Left + StripPx, bottom)
            : new NativeRect(mon.Right - StripPx, top, mon.Right, bottom);
    }

    /// <summary>The dock window inflated by the keep margin, clamped to the monitor.</summary>
    public static NativeRect KeepZone(NativeRect dockWindowPx, NativeRect monitorBoundsPx, double scale)
    {
        var m = DpiMath.DipToPxCeil(KeepMarginDip, scale);
        return MonitorGeometry.Intersect(MonitorGeometry.Inflate(dockWindowPx, m, m), monitorBoundsPx);
    }

    /// <summary>The top strip of a monitor for the auto-hiding menu bar (full width).</summary>
    public static NativeRect MenuBarRevealZone(NativeRect monitorBoundsPx) => new(monitorBoundsPx.Left, monitorBoundsPx.Top, monitorBoundsPx.Right, monitorBoundsPx.Top + StripPx);

    /// <summary>Menu bar keep zone: the bar plus a small margin below it.</summary>
    public static NativeRect MenuBarKeepZone(NativeRect barPx, NativeRect monitorBoundsPx, double scale)
    {
        var m = DpiMath.DipToPxCeil(8, scale);
        return MonitorGeometry.Intersect(new NativeRect(barPx.Left, barPx.Top, barPx.Right, barPx.Bottom + m), monitorBoundsPx);
    }

    public static bool Contains(NativeRect zone, NativePoint p) => !MonitorGeometry.IsEmpty(zone) && zone.Contains(p.X, p.Y);

    private static int MidX(NativeRect r, NativeRect fallback) => r.Width > 0 ? r.Left + r.Width / 2 : fallback.Left + fallback.Width / 2;

    private static int MidY(NativeRect r, NativeRect fallback) => r.Height > 0 ? r.Top + r.Height / 2 : fallback.Top + fallback.Height / 2;
}
