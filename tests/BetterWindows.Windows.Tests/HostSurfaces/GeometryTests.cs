using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Surfaces;

namespace BetterWindows.Native.Tests.HostSurfaces;

public class DpiMathTests
{
    [Fact]
    public void Sizes_round_up_so_fractional_pixels_never_clip_content()
    {
        Assert.Equal(11, DpiMath.DipToPxCeil(10.1, 1.0));
        Assert.Equal(70, DpiMath.DipToPxCeil(56, 1.25)); // 70.00000000000001 must stay 70, not 71
        Assert.Equal(0, DpiMath.DipToPxCeil(double.NaN, 1.0));
    }

    [Fact]
    public void Positions_round_half_away_from_zero()
    {
        Assert.Equal(3, DpiMath.DipToPxRound(2.5, 1.0));
        Assert.Equal(-3, DpiMath.DipToPxRound(-2.5, 1.0));
        Assert.Equal(-1, DpiMath.RoundPx(-0.5));
    }

    [Theory]
    [InlineData(double.NaN, 1.0)]
    [InlineData(0.1, 1.0)]
    [InlineData(20.0, 1.0)]
    [InlineData(double.PositiveInfinity, 1.0)]
    public void Absurd_scales_fall_back_to_one(double scale, double expected) => Assert.Equal(expected, DpiMath.SanitizeScale(scale));

    [Fact]
    public void Dpi_values_map_to_scales_and_zero_means_failure()
    {
        Assert.Equal(1.0, DpiMath.ScaleFromDpi(0));
        Assert.Equal(1.5, DpiMath.ScaleFromDpi(144));
        Assert.Equal(100.0, DpiMath.PxToDip(150, 1.5));
    }
}

public class MonitorGeometryTests
{
    [Fact]
    public void Primary_is_the_flagged_monitor_then_the_first_then_null()
    {
        var a = Mon.Make("1", 0, 0, 100, 100);
        var b = Mon.Make("2", 100, 0, 100, 100, primary: true);
        Assert.Equal("2", MonitorGeometry.Primary(new[] { a, b })!.Id);
        Assert.Equal("1", MonitorGeometry.Primary(new[] { a })!.Id);
        Assert.Null(MonitorGeometry.Primary(Array.Empty<MonitorInfo>()));
    }

    [Fact]
    public void ById_ignores_null_and_unknown_ids()
    {
        var list = new[] { Mon.Make("1", 0, 0, 100, 100) };
        Assert.Null(MonitorGeometry.ById(list, null));
        Assert.Null(MonitorGeometry.ById(list, "9"));
        Assert.NotNull(MonitorGeometry.ById(list, "1"));
    }

    [Fact]
    public void Intersect_of_disjoint_rectangles_is_empty()
    {
        var r = MonitorGeometry.Intersect(new NativeRect(0, 0, 10, 10), new NativeRect(20, 20, 30, 30));
        Assert.True(MonitorGeometry.IsEmpty(r));
        Assert.Equal(new NativeRect(5, 5, 10, 10), MonitorGeometry.Intersect(new NativeRect(0, 0, 10, 10), new NativeRect(5, 5, 30, 30)));
    }

    [Fact]
    public void Missing_work_area_falls_back_to_the_bounds_and_bad_rects_are_clamped()
    {
        var m = new MonitorInfo { BoundsPx = new Rect { Width = 800, Height = 600 }, WorkAreaPx = new Rect() };
        Assert.Equal(MonitorGeometry.BoundsPx(m), MonitorGeometry.WorkAreaPx(m));
        var neg = MonitorGeometry.ToNativeRect(new Rect { X = 10.4, Y = 5, Width = -5, Height = 7 });
        Assert.Equal(new NativeRect(10, 5, 10, 12), neg);
    }
}

public class AnchorTranslatorTests
{
    [Fact]
    public void Same_dpi_translates_by_the_origin_difference()
    {
        var r = AnchorTranslator.Translate(new Rect { X = 10, Y = 20, Width = 30, Height = 40 }, new NativePoint(100, 100), 1.0, new NativePoint(0, 0), 1.0);
        Assert.Equal(110, r.X);
        Assert.Equal(120, r.Y);
        Assert.Equal(30, r.Width);
        Assert.Equal(40, r.Height);
    }

    [Fact]
    public void Mixed_dpi_goes_through_physical_pixels()
    {
        // source at 100,100 px with scale 1; overlay at 0,0 px with scale 2: anchor (10,20) dip = (110,120) px = (55,60) overlay dip
        var r = AnchorTranslator.Translate(new Rect { X = 10, Y = 20, Width = 30, Height = 40 }, new NativePoint(100, 100), 1.0, new NativePoint(0, 0), 2.0);
        Assert.Equal(55, r.X);
        Assert.Equal(60, r.Y);
        Assert.Equal(15, r.Width);
        Assert.Equal(20, r.Height);
    }

    [Fact]
    public void Non_finite_and_negative_anchor_values_are_sanitised()
    {
        var r = AnchorTranslator.Translate(new Rect { X = double.NaN, Y = 0, Width = -5, Height = double.PositiveInfinity }, new NativePoint(100, 0), 1.0, new NativePoint(0, 0), 1.0);
        Assert.Equal(100, r.X);
        Assert.Equal(0, r.Width);
        Assert.Equal(0, r.Height);
    }
}

public class OverlayMonitorResolverTests
{
    private static readonly IReadOnlyList<MonitorInfo> Monitors =
        new[] { Mon.Make("1", 0, 0, 1920, 1080, primary: true), Mon.Make("2", 1920, 0, 1920, 1080) };

    [Fact]
    public void Request_then_source_then_cursor_then_primary()
    {
        Assert.Equal("2", OverlayMonitorResolver.Resolve(Monitors, "2", "1", "1")!.Id);
        Assert.Equal("2", OverlayMonitorResolver.Resolve(Monitors, null, "2", "1")!.Id);
        Assert.Equal("2", OverlayMonitorResolver.Resolve(Monitors, "9", null, "2")!.Id);
        Assert.Equal("1", OverlayMonitorResolver.Resolve(Monitors, null, null, null)!.Id);
        Assert.Null(OverlayMonitorResolver.Resolve(Array.Empty<MonitorInfo>(), null, null, null));
    }
}

public class DockPlacementTests
{
    private static readonly NativeRect Bounds = new(0, 0, 1920, 1080);
    private static readonly NativeRect Work = new(0, 0, 1920, 1040);

    [Fact]
    public void Bottom_dock_is_centred_on_the_bottom_edge()
    {
        var r = DockPlacement.Compute(new DockPlacementInput(DockPosition.Bottom, Bounds, Work, 1080, 640, 160));
        Assert.Equal(new NativeRect(640, 920, 1280, 1080), r);
    }

    [Fact]
    public void Oversized_dock_is_clamped_to_the_monitor()
    {
        var r = DockPlacement.Compute(new DockPlacementInput(DockPosition.Bottom, Bounds, Work, 1080, 3000, 160));
        Assert.Equal(new NativeRect(0, 920, 1920, 1080), r);
    }

    [Fact]
    public void Side_docks_sit_on_their_edge_and_centre_in_the_work_area()
    {
        Assert.Equal(new NativeRect(0, 220, 80, 820), DockPlacement.Compute(new DockPlacementInput(DockPosition.Left, Bounds, Work, 0, 80, 600)));
        Assert.Equal(new NativeRect(1840, 220, 1920, 820), DockPlacement.Compute(new DockPlacementInput(DockPosition.Right, Bounds, Work, 1920, 80, 600)));
    }

    [Fact]
    public void Reserved_thickness_is_ceil_of_dip_times_scale_and_never_more_than_half_the_monitor()
    {
        Assert.Equal(100, DockPlacement.ReservedThicknessPx(80, 1.25, DockPosition.Bottom, Bounds));
        Assert.Equal(540, DockPlacement.ReservedThicknessPx(2000, 1.0, DockPosition.Bottom, Bounds));
        Assert.Equal(1, DockPlacement.ReservedThicknessPx(0.0001, 1.0, DockPosition.Bottom, Bounds));
    }

    [Fact]
    public void Default_bar_thickness_is_the_clamped_icon_size_plus_padding()
    {
        Assert.Equal(80.0, DockPlacement.DefaultBarThicknessDip(56));
        Assert.Equal(56.0, DockPlacement.DefaultBarThicknessDip(10));
        Assert.Equal(152.0, DockPlacement.DefaultBarThicknessDip(500));
    }

    [Fact]
    public void Edge_without_appbar_follows_the_work_area_only_when_asked()
    {
        Assert.Equal(1040, DockPlacement.EdgeWithoutAppBar(DockPosition.Bottom, Bounds, Work, useWorkArea: true));
        Assert.Equal(1080, DockPlacement.EdgeWithoutAppBar(DockPosition.Bottom, Bounds, Work, useWorkArea: false));
        Assert.Equal(1920, DockPlacement.EdgeWithoutAppBar(DockPosition.Right, Bounds, Work, useWorkArea: false));
    }

    [Fact]
    public void Edge_from_the_appbar_rectangle_is_the_outer_edge()
    {
        Assert.Equal(80, DockPlacement.EdgeFromAppBarRect(DockPosition.Bottom, new NativeRect(0, 0, 1920, 80)));
        Assert.Equal(0, DockPlacement.EdgeFromAppBarRect(DockPosition.Left, new NativeRect(0, 0, 80, 1080)));
        Assert.Equal(AppBarEdge.Right, DockPlacement.ToAppBarEdge(DockPosition.Right));
    }
}

public class MenuBarPlacementTests
{
    [Fact]
    public void Height_is_28_dip_in_pixels_and_never_taller_than_the_monitor()
    {
        Assert.Equal(42, MenuBarPlacement.HeightPx(1.5));
        Assert.Equal(new NativeRect(0, 0, 1920, 28), MenuBarPlacement.Compute(new NativeRect(0, 0, 1920, 1080), 1.0));
        Assert.Equal(new NativeRect(0, 0, 100, 10), MenuBarPlacement.Compute(new NativeRect(0, 0, 100, 10), 1.0));
    }
}

public class ShapePlannerTests
{
    private static RegionPlan Plan(SurfaceShapeParams? shape, int w = 200, int h = 100, double scale = 1.0) => ShapePlanner.Plan(shape, w, h, scale);

    private static SurfaceShapeParams Shape(IEnumerable<ShapeRect> rects, IEnumerable<ShapeRect>? hits = null) => new()
    {
        Rects = rects.ToArray(),
        HitRects = (hits ?? Array.Empty<ShapeRect>()).ToArray(),
    };

    private static ShapeRect R(double x, double y, double w, double h, double radius = 0) => new() { X = x, Y = y, Width = w, Height = h, Radius = radius };

    [Fact]
    public void No_shape_or_empty_window_gives_an_empty_region()
    {
        Assert.True(Plan(null).IsEmpty);
        Assert.True(Plan(Shape(new[] { R(0, 0, 10, 10) }), 0, 100).IsEmpty);
    }

    [Fact]
    public void Edges_round_outwards_so_antialiased_pixels_stay_inside()
    {
        var plan = Plan(Shape(new[] { R(10.5, 10, 20.2, 20, radius: 4) }));
        Assert.Equal(new PxRoundRect(10, 10, 31, 30, 4), plan.Rects[0]);
    }

    [Fact]
    public void Rectangles_are_scaled_to_physical_pixels()
    {
        var plan = Plan(Shape(new[] { R(10, 10, 10, 10) }), scale: 2.0);
        Assert.Equal(new PxRoundRect(20, 20, 40, 40, 0), plan.Rects[0]);
    }

    [Fact]
    public void Outside_non_finite_and_empty_rectangles_are_dropped()
    {
        Assert.True(Plan(Shape(new[] { R(-50, 0, 20, 10) })).IsEmpty);
        Assert.True(Plan(Shape(new[] { R(double.NaN, 0, 10, 10) })).IsEmpty);
        Assert.True(Plan(Shape(new[] { R(0, 0, 0, 10) })).IsEmpty);
    }

    [Fact]
    public void Radius_is_clamped_to_half_the_smaller_side()
    {
        var plan = Plan(Shape(new[] { R(0, 0, 10, 10, radius: 100) }));
        Assert.Equal(5, plan.Rects[0].Radius);
    }

    [Fact]
    public void Duplicates_are_removed_and_hit_rects_join_the_union()
    {
        var plan = Plan(Shape(new[] { R(0, 0, 10, 10), R(0, 0, 10, 10) }, new[] { R(50, 50, 10, 10) }));
        Assert.Equal(2, plan.Rects.Count);
        Assert.Equal(new NativeRect(0, 0, 60, 60), plan.Bounds);
    }

    [Fact]
    public void At_most_64_rectangles_are_considered_from_each_list()
    {
        var many = Enumerable.Range(0, 80).Select(i => R(i * 2, 0, 1, 1)).ToArray();
        Assert.Equal(64, Plan(Shape(many), w: 300).Rects.Count);
    }

    [Fact]
    public void Identical_inputs_give_identical_signatures_and_different_ones_do_not()
    {
        var a = Plan(Shape(new[] { R(1, 1, 10, 10) }));
        var b = Plan(Shape(new[] { R(1, 1, 10, 10) }));
        var c = Plan(Shape(new[] { R(1, 1, 11, 10) }));
        Assert.Equal(a.Signature, b.Signature);
        Assert.NotEqual(a.Signature, c.Signature);
    }
}

public class SlideAnimationTests
{
    [Fact]
    public void Progress_is_ease_out_cubic_between_zero_and_one()
    {
        var d = TimeSpan.FromMilliseconds(100);
        Assert.Equal(0.0, SlideAnimation.Progress(TimeSpan.Zero, d));
        Assert.Equal(1.0, SlideAnimation.Progress(TimeSpan.FromMilliseconds(200), d));
        Assert.Equal(0.875, SlideAnimation.Progress(TimeSpan.FromMilliseconds(50), d), 6);
        Assert.Equal(1.0, SlideAnimation.Progress(TimeSpan.Zero, TimeSpan.Zero));
    }

    [Fact]
    public void Offsets_interpolate_from_the_current_position_so_reversing_does_not_jump()
    {
        Assert.Equal(0, SlideAnimation.Between(0, 28, 0));
        Assert.Equal(28, SlideAnimation.Between(0, 28, 1));
        Assert.Equal(14, SlideAnimation.Between(28, 0, 0.5));
        Assert.Equal(10, SlideAnimation.Between(10, 0, 0)); // a reversal starts where the bar is
        Assert.Equal(28, SlideAnimation.Between(0, 28, double.NaN)); // a non-finite progress finishes the slide
    }

    [Fact]
    public void Clip_keeps_only_the_part_on_the_monitor()
    {
        // window spans y -10..18 (Right/Bottom exclusive): the part at y 0..18 is on the monitor, which is y 10..28 in window-local px
        var clip = SlideAnimation.LocalClip(new NativeRect(0, -10, 100, 18), new NativeRect(0, 0, 1920, 1080));
        Assert.Equal(new NativeRect(0, 10, 100, 28), clip);
        Assert.Null(SlideAnimation.LocalClip(new NativeRect(0, -50, 100, -30), new NativeRect(0, 0, 1920, 1080)));
    }
}

public class AutoHideZonesTests
{
    private static readonly NativeRect Mon1080 = new(0, 0, 1920, 1080);

    [Fact]
    public void Bottom_reveal_strip_is_two_pixels_over_the_centre_third_or_the_dock_width()
    {
        var zone = AutoHideZones.DockRevealZone(DockPosition.Bottom, Mon1080, new NativeRect(640, 920, 1280, 1080));
        Assert.Equal(new NativeRect(640, 1078, 1280, 1080), zone);
    }

    [Fact]
    public void Side_reveal_strips_sit_on_their_edge()
    {
        Assert.Equal(new NativeRect(0, 220, 2, 820), AutoHideZones.DockRevealZone(DockPosition.Left, Mon1080, new NativeRect(0, 220, 80, 820)));
        Assert.Equal(new NativeRect(1918, 220, 1920, 820), AutoHideZones.DockRevealZone(DockPosition.Right, Mon1080, new NativeRect(1840, 220, 1920, 820)));
    }

    [Fact]
    public void Keep_zone_is_the_dock_plus_a_40_dip_margin_clamped_to_the_monitor()
    {
        Assert.Equal(new NativeRect(600, 880, 1320, 1080), AutoHideZones.KeepZone(new NativeRect(640, 920, 1280, 1080), Mon1080, 1.0));
    }

    [Fact]
    public void Menu_bar_zones_and_containment()
    {
        Assert.Equal(new NativeRect(0, 0, 1920, 2), AutoHideZones.MenuBarRevealZone(Mon1080));
        Assert.Equal(new NativeRect(0, 0, 1920, 36), AutoHideZones.MenuBarKeepZone(new NativeRect(0, 0, 1920, 28), Mon1080, 1.0));
        Assert.True(AutoHideZones.Contains(new NativeRect(0, 0, 10, 10), new NativePoint(5, 5)));
        Assert.False(AutoHideZones.Contains(new NativeRect(0, 0, 10, 10), new NativePoint(10, 10)));
        Assert.False(AutoHideZones.Contains(new NativeRect(0, 0, 0, 0), new NativePoint(0, 0)));
    }
}
