using BetterWindows.Core.Desktop;

namespace BetterWindows.Core.Widgets;

/// <summary>Pure placement rules for desktop widgets: snapping, keeping a widget inside a monitor's work area, and finding free space for a new one.</summary>
public static class WidgetPlacement
{
    /// <summary>Rounds to the nearest multiple of <see cref="WidgetMetrics.SnapGrid"/> (halves away from zero). Non-finite input gives 0.</summary>
    public static double Snap(double v) => double.IsFinite(v) ? Math.Round(v / WidgetMetrics.SnapGrid, MidpointRounding.AwayFromZero) * WidgetMetrics.SnapGrid : 0;

    /// <summary>
    /// Moves a <paramref name="width"/> x <paramref name="height"/> widget at (x,y) so it lies fully inside <paramref name="area"/>. With <paramref name="snap"/> the
    /// position is a multiple of 8 DIPs AND still inside (it snaps first, then clamps to the nearest snapped position that fits); an area that cannot hold the widget
    /// pins it to the area's top-left. Non-finite input gives the area's top-left.
    /// </summary>
    public static (double X, double Y) Clamp(DipRect area, double x, double y, double width, double height, bool snap)
    {
        return (Axis(x, area.X, area.Right - width), Axis(y, area.Y, area.Bottom - height));

        double Axis(double v, double lo, double hi)
        {
            if (!double.IsFinite(v)) v = lo;
            if (snap)
            {
                var sLo = Math.Ceiling(lo / WidgetMetrics.SnapGrid) * WidgetMetrics.SnapGrid;
                var sHi = Math.Floor(hi / WidgetMetrics.SnapGrid) * WidgetMetrics.SnapGrid;
                if (sHi >= sLo) return Math.Clamp(Snap(v), sLo, sHi);
            }
            return hi < lo ? lo : Math.Clamp(snap ? Snap(v) : Math.Round(v, 2), lo, hi);
        }
    }

    /// <summary>
    /// A position for a NEW widget: the documented cascade point (48+24n, 80+24n) for the n widgets already on the monitor when it is free, otherwise the first free
    /// spot found by scanning the work area row by row on a 16 DIP step. If nothing is free the cascade point is used anyway (overlap beats refusing to add).
    /// </summary>
    public static (double X, double Y) AutoPlace(DipRect area, double width, double height, IReadOnlyList<DipRect> existing, bool snap)
    {
        var n = existing.Count;
        var cascade = Clamp(area, WidgetMetrics.CascadeStartX + WidgetMetrics.CascadeStep * n, WidgetMetrics.CascadeStartY + WidgetMetrics.CascadeStep * n, width, height, snap);
        if (IsFree(cascade.X, cascade.Y)) return cascade;

        const double step = 16;
        var startX = Math.Max(area.X, Math.Min(WidgetMetrics.CascadeStartX, area.Right - width));
        var startY = Math.Max(area.Y, Math.Min(WidgetMetrics.CascadeStartY, area.Bottom - height));
        for (var y = startY; y + height <= area.Bottom + 0.001; y += step)
            for (var x = startX; x + width <= area.Right + 0.001; x += step)
            {
                var c = Clamp(area, x, y, width, height, snap);
                if (IsFree(c.X, c.Y)) return c;
            }
        // Second pass over the part above/left of the preferred start.
        for (var y = area.Y; y + height <= area.Bottom + 0.001; y += step)
            for (var x = area.X; x + width <= area.Right + 0.001; x += step)
            {
                var c = Clamp(area, x, y, width, height, snap);
                if (IsFree(c.X, c.Y)) return c;
            }
        return cascade;

        bool IsFree(double px, double py)
        {
            var r = new DipRect(px, py, width, height);
            foreach (var e in existing) if (r.Overlaps(e)) return false;
            return true;
        }
    }
}
