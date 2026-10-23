using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Rectangle helpers shared by the shell layer; run everywhere.</summary>
public class RectMathTests
{
    [Fact]
    public void Covers_respects_tolerance_on_every_side()
    {
        var inner = new NativeRect(0, 0, 100, 100);
        RectMath.Covers(new NativeRect(0, 0, 100, 100), inner, 0).Should().BeTrue();
        RectMath.Covers(new NativeRect(1, 0, 100, 100), inner, 0).Should().BeFalse();
        RectMath.Covers(new NativeRect(1, 0, 100, 100), inner, 1).Should().BeTrue();
        RectMath.Covers(new NativeRect(0, 0, 100, 98), inner, 1).Should().BeFalse();
        RectMath.Covers(new NativeRect(-50, -50, 500, 500), inner, 0).Should().BeTrue();
    }

    [Fact]
    public void Empty_inner_rectangle_is_never_covered() =>
        RectMath.Covers(new NativeRect(0, 0, 100, 100), new NativeRect(10, 10, 10, 50), 5).Should().BeFalse();

    [Fact]
    public void Protocol_conversion_round_trips_and_rounds_to_whole_pixels()
    {
        var native = new NativeRect(-1920, 10, 0, 1090);
        var p = RectMath.ToProtocol(native);
        (p.X, p.Y, p.Width, p.Height).Should().Be((-1920, 10, 1920, 1080));
        RectMath.FromProtocol(p).Should().Be(native);
        RectMath.FromProtocol(new Rect { X = 0.4, Y = 0.6, Width = 99.7, Height = 10.2 }).Should().Be(new NativeRect(0, 1, 100, 11));
    }

    [Fact]
    public void Intersection_area()
    {
        RectMath.IntersectionArea(new NativeRect(0, 0, 10, 10), new NativeRect(5, 5, 20, 20)).Should().Be(25);
        RectMath.IntersectionArea(new NativeRect(0, 0, 10, 10), new NativeRect(10, 0, 20, 10)).Should().Be(0);
        RectMath.IntersectionArea(new NativeRect(0, 0, 10, 10), new NativeRect(50, 50, 60, 60)).Should().Be(0);
    }
}
