using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Interop;

/// <summary>Pure rectangle helpers (physical pixels, Win32 RECT semantics: Right/Bottom exclusive). Cross-platform and unit-tested.</summary>
internal static class RectMath
{
    /// <summary>True when <paramref name="outer"/> contains <paramref name="inner"/> on every side, allowing <paramref name="tolerancePx"/> of slack per side.</summary>
    public static bool Covers(NativeRect outer, NativeRect inner, int tolerancePx)
    {
        if (inner.Width <= 0 || inner.Height <= 0)
        {
            return false;
        }

        return outer.Left <= inner.Left + tolerancePx
            && outer.Top <= inner.Top + tolerancePx
            && outer.Right >= inner.Right - tolerancePx
            && outer.Bottom >= inner.Bottom - tolerancePx;
    }

    /// <summary>Protocol (double) rectangle to physical-pixel rectangle, rounding to the nearest pixel.</summary>
    public static NativeRect FromProtocol(Rect r) => new(
        (int)Math.Round(r.X),
        (int)Math.Round(r.Y),
        (int)Math.Round(r.X + r.Width),
        (int)Math.Round(r.Y + r.Height));

    /// <summary>Physical-pixel rectangle to the protocol rectangle (x, y, width, height).</summary>
    public static Rect ToProtocol(NativeRect r) => new() { X = r.Left, Y = r.Top, Width = r.Width, Height = r.Height };

    /// <summary>Area of the intersection of two rectangles (0 when disjoint).</summary>
    public static long IntersectionArea(NativeRect a, NativeRect b)
    {
        long w = Math.Min(a.Right, b.Right) - (long)Math.Max(a.Left, b.Left);
        long h = Math.Min(a.Bottom, b.Bottom) - (long)Math.Max(a.Top, b.Top);
        return w > 0 && h > 0 ? w * h : 0;
    }
}
