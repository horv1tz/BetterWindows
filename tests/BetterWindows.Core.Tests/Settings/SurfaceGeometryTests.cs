using BetterWindows.Core.Config;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using Xunit;

namespace BetterWindows.Core.Tests.Config;

public class SurfaceGeometryTests
{
    [Theory]
    [InlineData(double.NaN, 100)]
    [InlineData(double.PositiveInfinity, 100)]
    [InlineData(10, double.NaN)]
    [InlineData(10, 0.5)]
    public void Non_finite_requests_and_unusable_bounds_are_refused(double requested, double max) =>
        Assert.False(SurfaceGeometry.TryClampSize(requested, max, out _));

    [Fact]
    public void Sizes_are_clamped_to_one_and_the_monitor()
    {
        Assert.True(SurfaceGeometry.TryClampSize(-5, 1920, out var low));
        Assert.Equal(1, low);
        Assert.True(SurfaceGeometry.TryClampSize(5000, 1920, out var high));
        Assert.Equal(1920, high);
        Assert.True(SurfaceGeometry.TryClampSize(72.5, 1920, out var inside));
        Assert.Equal(72.5, inside);
    }

    [Fact]
    public void Bar_thickness_null_or_non_finite_keeps_the_previous_value()
    {
        Assert.Null(SurfaceGeometry.ClampBar(null, 80));
        Assert.Null(SurfaceGeometry.ClampBar(double.NaN, 80));
        Assert.Null(SurfaceGeometry.ClampBar(10, double.PositiveInfinity));
    }

    [Fact]
    public void Bar_thickness_is_clamped_into_zero_and_height()
    {
        Assert.Equal(0, SurfaceGeometry.ClampBar(-3, 80));
        Assert.Equal(80, SurfaceGeometry.ClampBar(500, 80));
        Assert.Equal(32, SurfaceGeometry.ClampBar(32, 80));
    }

    [Fact]
    public void Rects_are_trimmed_into_the_window_and_non_finite_values_fall_back()
    {
        var rects = new[]
        {
            new ShapeRect { X = -10, Y = 5, Width = 50, Height = 20, Radius = 4 },          // left edge pushed to 0
            new ShapeRect { X = 90, Y = 0, Width = 50, Height = 20, Radius = 0 },           // overflows the right edge: trimmed to 10 wide
            new ShapeRect { X = double.NaN, Y = 0, Width = double.PositiveInfinity, Height = 10, Radius = 0 }, // infinite width -> dropped (0 wide)
        };
        var result = SurfaceGeometry.ClampRects(rects, 100, 60);
        Assert.Equal(2, result.Count);
        Assert.Equal(new { X = 0.0, Y = 5.0, W = 50.0, H = 20.0 }, new { X = result[0].X, Y = result[0].Y, W = result[0].Width, H = result[0].Height });
        Assert.Equal(10, result[1].Width);
    }

    [Fact]
    public void Rects_with_no_area_after_clamping_are_dropped()
    {
        var rects = new[]
        {
            new ShapeRect { X = 100, Y = 0, Width = 10, Height = 10, Radius = 0 },   // starts on the right edge
            new ShapeRect { X = 0, Y = 0, Width = 0, Height = 10, Radius = 0 },       // zero width
            new ShapeRect { X = 0, Y = 0, Width = -4, Height = 10, Radius = 0 },      // negative width
        };
        Assert.Empty(SurfaceGeometry.ClampRects(rects, 100, 60));
    }

    [Fact]
    public void Radius_is_at_most_half_the_short_side_and_never_negative()
    {
        var rects = new[]
        {
            new ShapeRect { X = 0, Y = 0, Width = 40, Height = 10, Radius = 999 },
            new ShapeRect { X = 0, Y = 0, Width = 40, Height = 10, Radius = -3 },
            new ShapeRect { X = 0, Y = 0, Width = 40, Height = 10, Radius = double.NaN },
        };
        var result = SurfaceGeometry.ClampRects(rects, 100, 100);
        Assert.Equal(5, result[0].Radius);
        Assert.Equal(0, result[1].Radius);
        Assert.Equal(0, result[2].Radius);
    }

    [Fact]
    public void At_most_64_rects_survive_in_input_order()
    {
        var many = Enumerable.Range(0, 200).Select(i => new ShapeRect { X = i % 50, Y = 0, Width = 1, Height = 1, Radius = 0 }).ToArray();
        var result = SurfaceGeometry.ClampRects(many, 100, 100);
        Assert.Equal(Limits.MaxShapeRects, result.Count);
        Assert.Equal(0, result[0].X);
        Assert.Equal(1, result[1].X);
    }

    [Fact]
    public void Null_input_or_an_empty_window_yields_no_rects()
    {
        Assert.Empty(SurfaceGeometry.ClampRects(null, 100, 100));
        Assert.Empty(SurfaceGeometry.ClampRects(new[] { new ShapeRect { Width = 1, Height = 1 } }, 0, 100));
        Assert.Empty(SurfaceGeometry.ClampRects(new[] { new ShapeRect { Width = 1, Height = 1 } }, double.NaN, 100));
    }
}
