using System.Text.Json.Nodes;
using BetterWindows.Core.Desktop;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Desktop;
using BetterWindows.Core.Widgets;

namespace BetterWindows.Core.Tests.Widgets;

public class WidgetPlacementTests
{
    private static readonly DipRect Area = new(0, 28, 1920, 1012);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(3, 0)]
    [InlineData(4, 8)]
    [InlineData(11, 8)]
    [InlineData(12, 16)]
    [InlineData(-4, -8)]
    public void Snap_rounds_to_the_nearest_multiple_of_8(double v, double expected)
    {
        WidgetPlacement.Snap(v).Should().Be(expected);
    }

    [Fact]
    public void Snap_of_a_non_finite_value_is_zero()
    {
        WidgetPlacement.Snap(double.NaN).Should().Be(0);
        WidgetPlacement.Snap(double.PositiveInfinity).Should().Be(0);
    }

    [Fact]
    public void A_widget_inside_the_area_is_only_snapped()
    {
        WidgetPlacement.Clamp(Area, 101, 203, 160, 160, snap: true).Should().Be((104.0, 200.0));
    }

    [Fact]
    public void Without_snapping_the_position_is_kept()
    {
        WidgetPlacement.Clamp(Area, 101.5, 203.25, 160, 160, snap: false).Should().Be((101.5, 203.25));
    }

    [Fact]
    public void A_widget_dragged_past_the_edges_is_pulled_back_inside_and_stays_on_the_grid()
    {
        var (x, y) = WidgetPlacement.Clamp(Area, 5000, 5000, 340, 160, snap: true);
        x.Should().BeLessThanOrEqualTo(Area.Right - 340);
        y.Should().BeLessThanOrEqualTo(Area.Bottom - 160);
        (x % 8).Should().Be(0);
        (y % 8).Should().Be(0);
        x.Should().Be(1576); // floor((1920-340)/8)*8
        y.Should().Be(880);  // floor((1040-160)/8)*8
    }

    [Fact]
    public void The_top_edge_respects_the_work_area_not_the_screen()
    {
        // the menu bar owns the first 28 DIPs; the first snapped position at or below it is 32
        WidgetPlacement.Clamp(Area, -50, -50, 160, 160, snap: true).Should().Be((0.0, 32.0));
    }

    [Fact]
    public void A_widget_larger_than_the_area_is_pinned_to_the_top_left()
    {
        WidgetPlacement.Clamp(new DipRect(0, 0, 100, 100), 50, 50, 340, 340, snap: false).Should().Be((0.0, 0.0));
        WidgetPlacement.Clamp(new DipRect(0, 0, 100, 100), 50, 50, 340, 340, snap: true).Should().Be((0.0, 0.0));
    }

    [Fact]
    public void Non_finite_input_goes_to_the_area_origin()
    {
        WidgetPlacement.Clamp(Area, double.NaN, double.NegativeInfinity, 160, 160, snap: false).Should().Be((0.0, 28.0));
    }

    [Fact]
    public void The_first_auto_placed_widget_goes_to_the_documented_cascade_origin()
    {
        WidgetPlacement.AutoPlace(Area, 160, 160, Array.Empty<DipRect>(), snap: true).Should().Be((48.0, 80.0));
    }

    [Fact]
    public void Auto_placement_avoids_existing_widgets()
    {
        var first = new DipRect(48, 80, 340, 160);
        var (x, y) = WidgetPlacement.AutoPlace(Area, 340, 160, new[] { first }, snap: true);
        new DipRect(x, y, 340, 160).Overlaps(first).Should().BeFalse();
    }

    [Fact]
    public void Auto_placement_cascades_when_the_cascade_point_is_free()
    {
        // one small widget far away: n = 1 so the cascade point is (72,104) and it is free
        var far = new DipRect(1500, 800, 160, 160);
        WidgetPlacement.AutoPlace(Area, 160, 160, new[] { far }, snap: true).Should().Be((72.0, 104.0));
    }

    [Fact]
    public void Auto_placement_finds_free_space_among_many_widgets()
    {
        var placed = new List<DipRect>();
        for (var i = 0; i < 12; i++)
        {
            var (x, y) = WidgetPlacement.AutoPlace(Area, 340, 160, placed, snap: true);
            var r = new DipRect(x, y, 340, 160);
            placed.Any(p => p.Overlaps(r)).Should().BeFalse($"widget {i} must not overlap an earlier one");
            r.X.Should().BeGreaterThanOrEqualTo(Area.X);
            r.Right.Should().BeLessThanOrEqualTo(Area.Right);
            r.Y.Should().BeGreaterThanOrEqualTo(Area.Y);
            r.Bottom.Should().BeLessThanOrEqualTo(Area.Bottom);
            placed.Add(r);
        }
    }

    [Fact]
    public void When_nothing_is_free_the_cascade_point_is_used_anyway()
    {
        var wall = new DipRect(0, 28, 1920, 1012);
        var pos = WidgetPlacement.AutoPlace(Area, 160, 160, new[] { wall }, snap: true);
        Area.X.Should().BeLessThanOrEqualTo(pos.X);
        pos.X.Should().BeLessThanOrEqualTo(Area.Right - 160);
    }
}

public class WidgetMetricsTests
{
    [Theory]
    [InlineData(WidgetSize.Small, 160, 160)]
    [InlineData(WidgetSize.Medium, 340, 160)]
    [InlineData(WidgetSize.Large, 340, 340)]
    public void Size_classes_have_fixed_dip_sizes(WidgetSize size, double w, double h)
    {
        WidgetMetrics.SizeDip(size).Should().Be((w, h));
    }

    [Theory]
    [InlineData(WidgetType.Clock, WidgetSize.Small)]
    [InlineData(WidgetType.Battery, WidgetSize.Small)]
    [InlineData(WidgetType.Weather, WidgetSize.Medium)]
    [InlineData(WidgetType.Notes, WidgetSize.Medium)]
    public void Each_type_has_a_default_size(WidgetType type, WidgetSize size)
    {
        WidgetMetrics.DefaultSize(type).Should().Be(size);
    }

    [Fact]
    public void Default_configs_follow_the_documented_schemas()
    {
        WidgetMetrics.DefaultConfig(WidgetType.Notes).ToJsonString().Should().Be("{\"text\":\"\"}");
        WidgetMetrics.DefaultConfig(WidgetType.Clock)["style"]!.GetValue<string>().Should().Be("digital");
        WidgetMetrics.DefaultConfig(WidgetType.Calendar)["weekNumbers"]!.GetValue<bool>().Should().BeFalse();
        WidgetMetrics.DefaultConfig(WidgetType.Weather).Count.Should().Be(0);
    }

    [Fact]
    public void Every_widget_type_has_an_object_config()
    {
        foreach (var t in Enum.GetValues<WidgetType>()) WidgetMetrics.DefaultConfig(t).Should().BeOfType<JsonObject>();
    }

    [Fact]
    public void A_config_must_be_an_object()
    {
        foreach (var bad in new JsonNode?[] { JsonNode.Parse("[1,2]"), JsonValue.Create("text"), JsonValue.Create(5), null })
        {
            var act = () => WidgetMetrics.ValidateConfig(bad);
            act.Should().Throw<ProtocolException>().Which.Code.Should().Be("badRequest");
        }
    }

    [Fact]
    public void A_config_up_to_16_kb_is_accepted_and_copied()
    {
        var original = new JsonObject { ["text"] = new string('x', 16 * 1024 - 20) };
        var copy = WidgetMetrics.ValidateConfig(original);
        copy.Should().NotBeSameAs(original);
        copy.Parent.Should().BeNull();
    }

    [Fact]
    public void A_config_over_16_kb_is_refused()
    {
        var act = () => WidgetMetrics.ValidateConfig(new JsonObject { ["text"] = new string('x', 16 * 1024 + 1) });
        act.Should().Throw<ProtocolException>().Which.Code.Should().Be("badRequest");
    }

    [Fact]
    public void The_size_limit_counts_utf8_bytes_not_characters()
    {
        var act = () => WidgetMetrics.ValidateConfig(new JsonObject { ["text"] = new string('я', 9000) }); // 18000 bytes
        act.Should().Throw<ProtocolException>();
    }
}

public class WidgetOpsTests
{
    private static readonly MonitorInfo M1 = Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true, workTop: 28, workBottom: 12);
    private static readonly MonitorInfo M2 = Kit.Monitor("m2", 1920, 0, 1280, 720);
    private static readonly IReadOnlyList<MonitorInfo> Both = new[] { M1, M2 };
    private static Settings Empty() => new() { Desktop = new DesktopSettings { Enabled = true } };

    [Fact]
    public void A_new_widget_gets_the_type_default_size_config_and_a_unique_id()
    {
        var (s, w) = WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Notes, null, null, null, WidgetOps.NewId());
        w!.Size.Should().Be(WidgetSize.Medium);
        w.Config!.ToJsonString().Should().Be("{\"text\":\"\"}");
        w.Id.Should().StartWith("w-");
        s.Desktop.Widgets.Should().ContainSingle();
        WidgetOps.NewId().Should().NotBe(WidgetOps.NewId());
    }

    [Fact]
    public void An_explicit_size_wins_over_the_default()
    {
        WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Clock, WidgetSize.Large, null, null, "w-1").Widget!.Size.Should().Be(WidgetSize.Large);
    }

    [Fact]
    public void An_explicit_position_is_clamped_and_snapped()
    {
        var w = WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Clock, null, 1001, 5000, "w-1").Widget!;
        w.X.Should().Be(1000);
        w.Y.Should().BeLessThanOrEqualTo(1080 - 12 - 160);
        (w.Y % 8).Should().Be(0);
    }

    [Fact]
    public void Position_snapping_follows_the_grid_snap_setting()
    {
        var s = Empty() with { Desktop = Empty().Desktop with { GridSnap = false } };
        WidgetOps.Add(s, Both, M1, null, WidgetType.Clock, null, 101.5, 203.25, "w-1").Widget!.Should().Match<WidgetInstance>(w => w.X == 101.5 && w.Y == 203.25);
    }

    [Fact]
    public void Widgets_without_a_position_avoid_each_other_on_the_same_monitor()
    {
        var s = Empty();
        var rects = new List<DipRect>();
        for (var i = 0; i < 8; i++)
        {
            var r = WidgetOps.Add(s, Both, M1, null, WidgetType.Weather, null, null, null, "w-" + i);
            s = r.Settings;
            var rect = WidgetOps.RectOf(r.Widget!);
            rects.Any(o => o.Overlaps(rect)).Should().BeFalse();
            rects.Add(rect);
        }
    }

    [Fact]
    public void Widgets_on_another_monitor_do_not_count_as_obstacles()
    {
        var s = WidgetOps.Add(Empty(), Both, M2, "m2", WidgetType.Weather, null, null, null, "w-0").Settings;
        var onM1 = WidgetOps.Add(s, Both, M1, null, WidgetType.Weather, null, null, null, "w-1").Widget!;
        (onM1.X, onM1.Y).Should().Be((48.0, 80.0));
    }

    [Fact]
    public void The_widget_limit_is_enforced()
    {
        var many = Enumerable.Range(0, WidgetOps.MaxWidgets).Select(i => new WidgetInstance { Id = "w-" + i, Type = WidgetType.Clock }).ToArray();
        var s = Empty() with { Desktop = Empty().Desktop with { Widgets = many } };
        WidgetOps.Add(s, Both, M1, null, WidgetType.Clock, null, null, null, "w-x").Widget.Should().BeNull();
    }

    [Fact]
    public void Moving_clamps_inside_the_work_area_and_snaps()
    {
        var s = WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Weather, null, 100, 100, "w-1").Settings;
        var (next, w) = WidgetOps.Move(s, Both, "w-1", 99999, -99999);
        w!.X.Should().Be(1576);
        w.Y.Should().Be(32);
        next.Desktop.Widgets.Single().X.Should().Be(1576);
    }

    [Fact]
    public void Moving_an_unknown_widget_changes_nothing()
    {
        var s = Empty();
        var (next, w) = WidgetOps.Move(s, Both, "nope", 1, 1);
        w.Should().BeNull();
        next.Should().BeSameAs(s);
    }

    [Fact]
    public void Update_replaces_the_whole_config()
    {
        var s = WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Clock, null, 100, 100, "w-1").Settings;
        var (next, w) = WidgetOps.Update(s, Both, "w-1", null, new JsonObject { ["style"] = "analog" });
        w!.Config!.ToJsonString().Should().Be("{\"style\":\"analog\"}"); // "seconds" from the default is gone
        next.Desktop.Widgets.Single().Config!.ToJsonString().Should().Be("{\"style\":\"analog\"}");
    }

    [Fact]
    public void Update_without_a_config_keeps_it_and_changes_the_size()
    {
        var s = WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Notes, null, 100, 100, "w-1").Settings;
        var w = WidgetOps.Update(s, Both, "w-1", WidgetSize.Large, null).Widget!;
        w.Size.Should().Be(WidgetSize.Large);
        w.Config!.ToJsonString().Should().Be("{\"text\":\"\"}");
    }

    [Fact]
    public void Growing_a_widget_pulls_it_back_inside_the_work_area()
    {
        var s = WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Clock, WidgetSize.Small, 1760, 880, "w-1").Settings;
        var w = WidgetOps.Update(s, Both, "w-1", WidgetSize.Large, null).Widget!;
        WidgetOps.RectOf(w).Right.Should().BeLessThanOrEqualTo(1920);
        WidgetOps.RectOf(w).Bottom.Should().BeLessThanOrEqualTo(1080 - 12);
    }

    [Fact]
    public void Updating_an_unknown_widget_reports_null()
    {
        WidgetOps.Update(Empty(), Both, "nope", WidgetSize.Small, null).Widget.Should().BeNull();
    }

    [Fact]
    public void Removing_drops_the_widget()
    {
        var s = WidgetOps.Add(Empty(), Both, M1, null, WidgetType.Clock, null, null, null, "w-1").Settings;
        var (next, found) = WidgetOps.Remove(s, "w-1");
        found.Should().BeTrue();
        next.Desktop.Widgets.Should().BeEmpty();
        WidgetOps.Remove(next, "w-1").Found.Should().BeFalse();
    }

    [Fact]
    public void A_widget_of_an_unplugged_monitor_is_shown_on_the_primary_one_inside_its_work_area()
    {
        var s = Empty() with { Desktop = Empty().Desktop with { Widgets = new[] { new WidgetInstance { Id = "w-1", Type = WidgetType.Clock, Size = WidgetSize.Small, X = 3000, Y = 2000, MonitorId = "gone" } } } };
        var shown = WidgetOps.On(s, Both, M1).Select(w => WidgetOps.ForDisplay(w, M1)).Single();
        shown.X.Should().BeLessThanOrEqualTo(1920 - 160);
        shown.Y.Should().BeLessThanOrEqualTo(1080 - 12 - 160);
        WidgetOps.On(s, Both, M2).Should().BeEmpty();
    }

    [Fact]
    public void For_display_leaves_a_widget_that_already_fits_untouched()
    {
        var w = new WidgetInstance { Id = "w", Type = WidgetType.Clock, Size = WidgetSize.Small, X = 100, Y = 100 };
        WidgetOps.ForDisplay(w, M1).Should().BeSameAs(w);
    }
}
