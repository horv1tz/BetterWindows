using BetterWindows.Core.Desktop;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Desktop;

public class MonitorGeometryTests
{
    [Theory]
    [InlineData(0.0)]
    [InlineData(-2.0)]
    [InlineData(double.NaN)]
    [InlineData(999.0)]
    public void A_nonsensical_scale_falls_back_to_one(double scale)
    {
        MonitorGeometry.ScaleOf(new MonitorInfo { Scale = scale }).Should().Be(1.0);
    }

    [Fact]
    public void The_work_area_is_converted_to_dips_relative_to_the_monitor_origin()
    {
        // second monitor at x=1920, 150% scale, 40 px reserved at the top and 60 px at the bottom
        var m = Kit.Monitor("m2", 1920, 0, 2880, 1620, scale: 1.5, workTop: 42, workBottom: 60);
        var wa = MonitorGeometry.WorkAreaDip(m);
        wa.X.Should().Be(0);
        wa.Y.Should().Be(28);
        wa.Width.Should().Be(1920);
        wa.Height.Should().BeApproximately((1620 - 42 - 60) / 1.5, 0.001);
    }

    [Fact]
    public void An_empty_or_foreign_work_area_degrades_to_the_whole_monitor()
    {
        var m = new MonitorInfo { Id = "m", Scale = 1, BoundsPx = new Rect { Width = 1000, Height = 500 }, WorkAreaPx = new Rect() };
        MonitorGeometry.WorkAreaDip(m).Should().Be(new DipRect(0, 0, 1000, 500));
    }

    [Fact]
    public void Bounds_in_dips_use_the_scale()
    {
        MonitorGeometry.BoundsDip(Kit.Monitor("m", 100, 200, 3000, 1500, scale: 2)).Should().Be(new DipRect(0, 0, 1500, 750));
    }

    [Fact]
    public void Primary_is_the_flagged_monitor_else_the_first()
    {
        var a = Kit.Monitor("a", 0, 0, 100, 100);
        var b = Kit.Monitor("b", 100, 0, 100, 100, primary: true);
        MonitorGeometry.Primary(new[] { a, b }).Should().BeSameAs(b);
        MonitorGeometry.Primary(new[] { a }).Should().BeSameAs(a);
        MonitorGeometry.Primary(Array.Empty<MonitorInfo>()).Should().BeNull();
    }

    [Fact]
    public void Find_null_means_primary_and_an_unknown_id_is_null()
    {
        var a = Kit.Monitor("a", 0, 0, 100, 100, primary: true);
        var b = Kit.Monitor("b", 100, 0, 100, 100);
        MonitorGeometry.Find(new[] { a, b }, null).Should().BeSameAs(a);
        MonitorGeometry.Find(new[] { a, b }, "b").Should().BeSameAs(b);
        MonitorGeometry.Find(new[] { a, b }, "zzz").Should().BeNull();
    }

    [Fact]
    public void An_item_of_an_unplugged_monitor_lands_on_the_primary_one()
    {
        var a = Kit.Monitor("a", 0, 0, 100, 100, primary: true);
        MonitorGeometry.Effective(new[] { a }, "gone").Should().BeSameAs(a);
    }

    [Fact]
    public void A_point_belongs_to_the_monitor_that_contains_it()
    {
        var a = Kit.Monitor("a", 0, 0, 1920, 1080, primary: true);
        var b = Kit.Monitor("b", 1920, 0, 1280, 720);
        MonitorGeometry.MonitorOfPoint(new[] { a, b }, 1919, 500)!.Id.Should().Be("a");
        MonitorGeometry.MonitorOfPoint(new[] { a, b }, 1920, 500)!.Id.Should().Be("b");
    }

    [Fact]
    public void A_point_outside_every_monitor_goes_to_the_nearest_one()
    {
        var a = Kit.Monitor("a", 0, 0, 1920, 1080, primary: true);
        var b = Kit.Monitor("b", 1920, 0, 1280, 720);
        MonitorGeometry.MonitorOfPoint(new[] { a, b }, -500, 300)!.Id.Should().Be("a");
        MonitorGeometry.MonitorOfPoint(new[] { a, b }, 5000, 300)!.Id.Should().Be("b");
        MonitorGeometry.MonitorOfPoint(new[] { a, b }, 3000, 900)!.Id.Should().Be("b");
        MonitorGeometry.MonitorOfPoint(Array.Empty<MonitorInfo>(), 0, 0).Should().BeNull();
    }

    [Fact]
    public void Rectangles_that_only_touch_do_not_overlap()
    {
        new DipRect(0, 0, 10, 10).Overlaps(new DipRect(10, 0, 10, 10)).Should().BeFalse();
        new DipRect(0, 0, 10, 10).Overlaps(new DipRect(9.9, 0, 10, 10)).Should().BeTrue();
    }
}
