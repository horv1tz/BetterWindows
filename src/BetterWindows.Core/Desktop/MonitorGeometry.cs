using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Desktop;

/// <summary>A rectangle in DIPs (CSS px of one monitor's surface). Right/Bottom are exclusive edges.</summary>
public readonly record struct DipRect(double X, double Y, double Width, double Height)
{
    public double Right => X + Width;
    public double Bottom => Y + Height;

    /// <summary>True when the two rectangles share an area larger than zero (touching edges do not overlap).</summary>
    public bool Overlaps(DipRect o) => X < o.Right && o.X < Right && Y < o.Bottom && o.Y < Bottom;
}

/// <summary>
/// Pure monitor maths shared by the desktop layer and Mission Control: physical-pixel virtual-screen rectangles (MonitorInfo) to DIP areas relative to one
/// monitor's top-left, and "which monitor does this thing belong to" with the fallbacks that keep items reachable after a monitor was unplugged.
/// </summary>
public static class MonitorGeometry
{
    /// <summary>The effective DPI scale of a monitor; garbage (0, NaN, negative, absurd) falls back to 1 so no division can blow up.</summary>
    public static double ScaleOf(MonitorInfo m) => double.IsFinite(m.Scale) && m.Scale >= 0.25 && m.Scale <= 16 ? m.Scale : 1.0;

    /// <summary>The whole monitor in DIPs, origin (0,0): the area a full-monitor surface (desktop layer) covers.</summary>
    public static DipRect BoundsDip(MonitorInfo m)
    {
        var s = ScaleOf(m);
        return new DipRect(0, 0, Math.Max(0, m.BoundsPx.Width / s), Math.Max(0, m.BoundsPx.Height / s));
    }

    /// <summary>
    /// The work area (screen minus the menu bar / dock AppBars) in DIPs RELATIVE to the monitor's top-left, i.e. in the coordinate space of a surface that covers the whole
    /// monitor. A work area that is empty or lies outside the bounds degrades to the whole monitor.
    /// </summary>
    public static DipRect WorkAreaDip(MonitorInfo m)
    {
        var s = ScaleOf(m);
        var bounds = BoundsDip(m);
        var wa = new DipRect((m.WorkAreaPx.X - m.BoundsPx.X) / s, (m.WorkAreaPx.Y - m.BoundsPx.Y) / s, m.WorkAreaPx.Width / s, m.WorkAreaPx.Height / s);
        if (!(wa.Width > 0 && wa.Height > 0) || !double.IsFinite(wa.X) || !double.IsFinite(wa.Y)) return bounds;
        var left = Math.Clamp(wa.X, 0, bounds.Width);
        var top = Math.Clamp(wa.Y, 0, bounds.Height);
        var right = Math.Clamp(wa.Right, left, bounds.Width);
        var bottom = Math.Clamp(wa.Bottom, top, bounds.Height);
        if (right - left <= 0 || bottom - top <= 0) return bounds;
        return new DipRect(left, top, right - left, bottom - top);
    }

    /// <summary>The primary monitor, else the first one, else null.</summary>
    public static MonitorInfo? Primary(IReadOnlyList<MonitorInfo> monitors)
    {
        foreach (var m in monitors) if (m.Primary) return m;
        return monitors.Count > 0 ? monitors[0] : null;
    }

    /// <summary>Exact id match, or null for an unknown id. <c>null</c> id = the primary monitor.</summary>
    public static MonitorInfo? Find(IReadOnlyList<MonitorInfo> monitors, string? id)
    {
        if (id is null) return Primary(monitors);
        foreach (var m in monitors) if (m.Id == id) return m;
        return null;
    }

    /// <summary>
    /// The monitor an item that stored <paramref name="id"/> is shown on: that monitor if it still exists, otherwise the primary one (an unplugged monitor must not make its
    /// shortcuts and widgets unreachable).
    /// </summary>
    public static MonitorInfo? Effective(IReadOnlyList<MonitorInfo> monitors, string? id) => Find(monitors, id) ?? Primary(monitors);

    /// <summary>The monitor whose bounds contain the physical-pixel point, else the nearest one (by distance to its rectangle), else null.</summary>
    public static MonitorInfo? MonitorOfPoint(IReadOnlyList<MonitorInfo> monitors, double xPx, double yPx)
    {
        MonitorInfo? best = null;
        var bestDist = double.MaxValue;
        foreach (var m in monitors)
        {
            var b = m.BoundsPx;
            var dx = xPx < b.X ? b.X - xPx : xPx >= b.X + b.Width ? xPx - (b.X + b.Width - 1) : 0;
            var dy = yPx < b.Y ? b.Y - yPx : yPx >= b.Y + b.Height ? yPx - (b.Y + b.Height - 1) : 0;
            var d = dx * dx + dy * dy;
            if (d == 0) return m;
            if (d < bestDist) { bestDist = d; best = m; }
        }
        return best;
    }
}
