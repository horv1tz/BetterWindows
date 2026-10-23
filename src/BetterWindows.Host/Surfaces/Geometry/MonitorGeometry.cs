using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Surfaces;

/// <summary>Conversions between the protocol's double-valued <see cref="Rect"/>/<see cref="MonitorInfo"/> (physical px, virtual-screen space) and <see cref="NativeRect"/>.</summary>
internal static class MonitorGeometry
{
    public static NativeRect ToNativeRect(Rect r)
    {
        var l = DpiMath.RoundPx(r.X);
        var t = DpiMath.RoundPx(r.Y);
        return new NativeRect(l, t, l + Math.Max(0, DpiMath.RoundPx(r.Width)), t + Math.Max(0, DpiMath.RoundPx(r.Height)));
    }

    public static NativeRect BoundsPx(MonitorInfo m) => ToNativeRect(m.BoundsPx);

    /// <summary>Work area, falling back to the bounds when the provider reported nothing sensible.</summary>
    public static NativeRect WorkAreaPx(MonitorInfo m)
    {
        var w = ToNativeRect(m.WorkAreaPx);
        return w.Width > 0 && w.Height > 0 ? w : BoundsPx(m);
    }

    public static double Scale(MonitorInfo m) => DpiMath.SanitizeScale(m.Scale);

    public static NativeRect Intersect(NativeRect a, NativeRect b)
    {
        var l = Math.Max(a.Left, b.Left);
        var t = Math.Max(a.Top, b.Top);
        var r = Math.Min(a.Right, b.Right);
        var bt = Math.Min(a.Bottom, b.Bottom);
        return r <= l || bt <= t ? new NativeRect(l, t, l, t) : new NativeRect(l, t, r, bt);
    }

    public static bool IsEmpty(NativeRect r) => r.Width <= 0 || r.Height <= 0;

    public static NativeRect Inflate(NativeRect r, int dx, int dy) => new(r.Left - dx, r.Top - dy, r.Right + dx, r.Bottom + dy);

    public static NativeRect Offset(NativeRect r, int dx, int dy) => new(r.Left + dx, r.Top + dy, r.Right + dx, r.Bottom + dy);

    /// <summary>The primary monitor; the first one when none is flagged; null for an empty list.</summary>
    public static MonitorInfo? Primary(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0) return null;
        foreach (var m in monitors) if (m.Primary) return m;
        return monitors[0];
    }

    public static MonitorInfo? ById(IReadOnlyList<MonitorInfo> monitors, string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        foreach (var m in monitors) if (string.Equals(m.Id, id, StringComparison.Ordinal)) return m;
        return null;
    }
}
