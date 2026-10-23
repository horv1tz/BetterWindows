using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Config;

/// <summary>
/// Sanitises the geometry a surface reports (surface.resize, surface.shape; docs/IPC.md sections 5 and 11). Pure: non-finite numbers never pass, every rect is clamped
/// into its window, radii are at most half the rect's short side, and lists are capped (the region is a GDI object: unbounded input would exhaust the process).
/// </summary>
public static class SurfaceGeometry
{
    /// <summary>Clamps a requested DIP size into [1, max]. False when the request or the bound is not finite or the bound is below 1.</summary>
    public static bool TryClampSize(double requested, double max, out double clamped)
    {
        clamped = 0;
        if (!double.IsFinite(requested) || !double.IsFinite(max) || max < 1) return false;
        clamped = Math.Clamp(requested, 1, max);
        return true;
    }

    /// <summary>The always-visible bar extent: null stays null (keeps the previous value); otherwise a finite value in [0, height].</summary>
    public static double? ClampBar(double? bar, double height)
    {
        if (bar is not { } b || !double.IsFinite(b) || !double.IsFinite(height)) return null;
        return Math.Clamp(b, 0, Math.Max(0, height));
    }

    /// <summary>
    /// Clamps every rect into a window of <paramref name="width"/> x <paramref name="height"/> DIPs. Rects that are empty after clamping are dropped; at most
    /// <see cref="Limits.MaxShapeRects"/> survive (input order is kept).
    /// </summary>
    public static IReadOnlyList<ShapeRect> ClampRects(IEnumerable<ShapeRect>? rects, double width, double height)
    {
        var result = new List<ShapeRect>();
        if (rects is null || !double.IsFinite(width) || !double.IsFinite(height) || width <= 0 || height <= 0) return result;
        foreach (var r in rects)
        {
            if (result.Count >= Limits.MaxShapeRects) break;
            if (r is null) continue;
            var x = Finite(r.X, 0);
            var y = Finite(r.Y, 0);
            var w = Finite(r.Width, 0);
            var h = Finite(r.Height, 0);
            x = Math.Clamp(x, 0, width);
            y = Math.Clamp(y, 0, height);
            w = Math.Min(w, width - x);
            h = Math.Min(h, height - y);
            if (w <= 0 || h <= 0) continue;
            var radius = Math.Clamp(Finite(r.Radius, 0), 0, Math.Min(w, h) / 2);
            result.Add(new ShapeRect { X = x, Y = y, Width = w, Height = h, Radius = radius });
        }
        return result;
    }

    private static double Finite(double v, double fallback) => double.IsFinite(v) ? v : fallback;
}
