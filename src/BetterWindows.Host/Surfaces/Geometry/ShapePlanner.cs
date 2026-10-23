using System.Globalization;
using System.Text;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>One rounded rectangle of a window region in WINDOW-LOCAL physical pixels (Right/Bottom exclusive). Radius is the corner radius in px (0 = square corners).</summary>
internal readonly record struct PxRoundRect(int Left, int Top, int Right, int Bottom, int Radius)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}

/// <summary>
/// The pixel-exact description of a window region (union of rounded rectangles) plus a signature for no-op detection. Built by <see cref="ShapePlanner"/>, turned into an
/// HRGN by <c>RegionBuilder</c>. <see cref="Empty"/> means "no part of the window is visible or hit-testable".
/// </summary>
internal sealed class RegionPlan
{
    public static readonly RegionPlan Empty = new(Array.Empty<PxRoundRect>(), 0, 0);

    public RegionPlan(IReadOnlyList<PxRoundRect> rects, int windowWidthPx, int windowHeightPx)
    {
        Rects = rects;
        WindowWidthPx = windowWidthPx;
        WindowHeightPx = windowHeightPx;
        var sb = new StringBuilder(rects.Count * 20 + 16);
        sb.Append(windowWidthPx.ToString(CultureInfo.InvariantCulture)).Append('x').Append(windowHeightPx.ToString(CultureInfo.InvariantCulture)).Append('|');
        foreach (var r in rects)
        {
            sb.Append(r.Left.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Top.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.Right.ToString(CultureInfo.InvariantCulture)).Append(',').Append(r.Bottom.ToString(CultureInfo.InvariantCulture)).Append(',')
              .Append(r.Radius.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        Signature = sb.ToString();
    }

    public IReadOnlyList<PxRoundRect> Rects { get; }
    public int WindowWidthPx { get; }
    public int WindowHeightPx { get; }
    public bool IsEmpty => Rects.Count == 0;
    /// <summary>Equal signatures produce identical regions: applying the second one is a no-op.</summary>
    public string Signature { get; }

    /// <summary>Bounding box of all rectangles (window-local px); an empty rect when <see cref="IsEmpty"/>.</summary>
    public NativeRect Bounds
    {
        get
        {
            if (Rects.Count == 0) return new NativeRect(0, 0, 0, 0);
            int l = int.MaxValue, t = int.MaxValue, r = int.MinValue, b = int.MinValue;
            foreach (var x in Rects) { l = Math.Min(l, x.Left); t = Math.Min(t, x.Top); r = Math.Max(r, x.Right); b = Math.Max(b, x.Bottom); }
            return new NativeRect(l, t, r, b);
        }
    }
}

/// <summary>
/// surface.shape -&gt; region plan (docs/IPC.md sections 5 and 11). Rules, all enforced here because the UI is untrusted and every rectangle becomes a GDI region (the
/// process-wide GDI handle quota would otherwise break every window):
///  - at most <see cref="MaxRects"/> rects and <see cref="MaxRects"/> hitRects are considered (the rest is dropped);
///  - non-finite coordinates drop the rectangle; width/height &lt;= 0 after conversion drop it;
///  - coordinates are converted to physical px with OUTWARD rounding (floor of the near edge, ceil of the far edge) so anti-aliased edge pixels stay inside the region;
///  - everything is clamped to the window; the radius to min(w,h)/2; exact duplicates are removed;
///  - rects and hitRects are merged: the region is their union (hitRects are interactive but, with a whole-window backdrop, not un-blurred: SupportsGlassOverflow is false).
/// </summary>
internal static class ShapePlanner
{
    public const int MaxRects = 64;

    public static RegionPlan Plan(SurfaceShapeParams? shape, int windowWidthPx, int windowHeightPx, double scale)
    {
        if (shape is null || windowWidthPx <= 0 || windowHeightPx <= 0) return RegionPlan.Empty;
        var s = DpiMath.SanitizeScale(scale);
        var result = new List<PxRoundRect>(16);
        var seen = new HashSet<PxRoundRect>();
        Add(shape.Rects, windowWidthPx, windowHeightPx, s, result, seen);
        Add(shape.HitRects, windowWidthPx, windowHeightPx, s, result, seen);
        return result.Count == 0 ? RegionPlan.Empty : new RegionPlan(result, windowWidthPx, windowHeightPx);
    }

    private static void Add(IReadOnlyList<ShapeRect>? source, int ww, int wh, double scale, List<PxRoundRect> into, HashSet<PxRoundRect> seen)
    {
        if (source is null) return;
        var n = Math.Min(source.Count, MaxRects);
        for (var i = 0; i < n; i++)
        {
            var r = source[i];
            if (r is null || !double.IsFinite(r.X) || !double.IsFinite(r.Y) || !double.IsFinite(r.Width) || !double.IsFinite(r.Height)) continue;
            if (r.Width <= 0 || r.Height <= 0) continue;
            var l = ToInt(Math.Floor(r.X * scale));
            var t = ToInt(Math.Floor(r.Y * scale));
            var rt = ToInt(Math.Ceiling((r.X + r.Width) * scale));
            var b = ToInt(Math.Ceiling((r.Y + r.Height) * scale));
            l = Math.Clamp(l, 0, ww);
            rt = Math.Clamp(rt, 0, ww);
            t = Math.Clamp(t, 0, wh);
            b = Math.Clamp(b, 0, wh);
            if (rt <= l || b <= t) continue;
            var radius = double.IsFinite(r.Radius) && r.Radius > 0 ? ToInt(Math.Round(r.Radius * scale, MidpointRounding.AwayFromZero)) : 0;
            radius = Math.Clamp(radius, 0, Math.Min(rt - l, b - t) / 2);
            var px = new PxRoundRect(l, t, rt, b, radius);
            if (seen.Add(px)) into.Add(px);
        }
    }

    private static int ToInt(double v) => v >= int.MaxValue / 4 ? int.MaxValue / 4 : v <= int.MinValue / 4 ? int.MinValue / 4 : (int)v;
}
