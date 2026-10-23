using BetterWindows.Core.Input;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Input;

public class HotCornerDetectorTests
{
    private static HotCornerMonitor Mon(string id, int left, int top, int right, int bottom) => new(id, new NativeRect(left, top, right, bottom));

    private static readonly HotCornerActions AllMission = new(HotCornerAction.MissionControl, HotCornerAction.MissionControl, HotCornerAction.MissionControl, HotCornerAction.MissionControl);

    /// <summary>Drives the detector with a 40 ms poll like the controller does.</summary>
    private sealed class Rig
    {
        public readonly HotCornerDetector Detector = new();
        public IReadOnlyList<HotCornerMonitor> Monitors;
        public HotCornerActions Actions = AllMission;
        public TimeSpan Dwell = TimeSpan.FromMilliseconds(150);
        public TimeSpan Now = TimeSpan.FromSeconds(10);
        public Func<string, bool>? Fullscreen;

        public Rig(params HotCornerMonitor[] monitors) { Monitors = monitors; }

        public HotCornerTrigger? Step(int x, int y, int ms = 40, bool down = false)
        {
            Now += TimeSpan.FromMilliseconds(ms);
            return Detector.Update(Now, new NativePoint(x, y), down, Monitors, Actions, Dwell, Fullscreen);
        }

        /// <summary>Holds the pointer at (x,y) for <paramref name="totalMs"/> and returns the first trigger.</summary>
        public HotCornerTrigger? Hold(int x, int y, int totalMs, bool down = false)
        {
            for (var t = 0; t < totalMs; t += 40)
                if (Step(x, y, 40, down) is { } hit) return hit;
            return null;
        }
    }

    private static Rig Single() => new(Mon("A", 0, 0, 1920, 1080));

    // ---- usable corners per layout ----

    private static string Describe(IReadOnlyList<HotCornerMonitor> mons) =>
        string.Join(",", HotCornerDetector.UsableCorners(mons).Select(c => $"{c.MonitorId}.{c.Corner}").OrderBy(s => s, StringComparer.Ordinal));

    [Fact]
    public void Single_monitor_has_four_usable_corners_at_the_very_corner_pixels()
    {
        var c = HotCornerDetector.UsableCorners(new[] { Mon("A", 0, 0, 1920, 1080) });
        c.Should().BeEquivalentTo(new[]
        {
            ("A", ScreenCorner.TopLeft, 0, 0), ("A", ScreenCorner.TopRight, 1919, 0), ("A", ScreenCorner.BottomLeft, 0, 1079), ("A", ScreenCorner.BottomRight, 1919, 1079),
        });
    }

    [Fact]
    public void Side_by_side_monitors_only_expose_the_outer_corners()
    {
        Describe(new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080) })
            .Should().Be("A.BottomLeft,A.TopLeft,B.BottomRight,B.TopRight");
    }

    [Fact]
    public void Stacked_monitors_only_expose_the_outer_corners()
    {
        Describe(new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 0, 1080, 1920, 2160) })
            .Should().Be("A.TopLeft,A.TopRight,B.BottomLeft,B.BottomRight");
    }

    [Fact]
    public void Negative_coordinates_work()
    {
        Describe(new[] { Mon("L", -1920, 0, 0, 1080), Mon("P", 0, 0, 1920, 1080) })
            .Should().Be("L.BottomLeft,L.TopLeft,P.BottomRight,P.TopRight");
        Describe(new[] { Mon("U", 0, -1080, 1920, 0), Mon("P", 0, 0, 1920, 1080) })
            .Should().Be("P.BottomLeft,P.BottomRight,U.TopLeft,U.TopRight");
    }

    [Fact]
    public void Mixed_sizes_expose_corners_that_overhang_the_neighbour()
    {
        // 1080p on the left, 1440p on the right (top aligned): B's bottom-left corner pokes out below A and IS reachable.
        Describe(new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 4480, 1440) })
            .Should().Be("A.BottomLeft,A.TopLeft,B.BottomLeft,B.BottomRight,B.TopRight");
    }

    [Fact]
    public void Mixed_sizes_with_a_taller_left_monitor()
    {
        // A 2560x1440 left of B 1920x1080 (top aligned): A's bottom-right overhangs and is usable, B's top-left is shared with A's edge.
        Describe(new[] { Mon("A", 0, 0, 2560, 1440), Mon("B", 2560, 0, 4480, 1080) })
            .Should().Be("A.BottomLeft,A.BottomRight,A.TopLeft,B.BottomRight,B.TopRight");
    }

    [Fact]
    public void Monitors_offset_vertically_keep_only_unshared_corners()
    {
        // B is shifted down by 400: A.BR is still adjacent to B (B spans y 400..1480), B.TL (y=400) is adjacent to A, but A.TR (y=0) is NOT next to B.
        Describe(new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 400, 3840, 1480) })
            .Should().Be("A.BottomLeft,A.TopLeft,A.TopRight,B.BottomLeft,B.BottomRight,B.TopRight");
    }

    [Fact]
    public void Three_monitors_in_a_row_only_expose_the_outermost_corners()
    {
        Describe(new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080), Mon("C", 3840, 0, 5760, 1080) })
            .Should().Be("A.BottomLeft,A.TopLeft,C.BottomRight,C.TopRight");
    }

    [Fact]
    public void L_shaped_layout_exposes_the_inner_notch_corners_that_face_empty_space()
    {
        // A top-left, B right of A, C below A (an L). B.BL (1920,1079) faces empty space below B but touches C diagonally -> blocked; B.BR free; C.TR (1919,1080) touches B diagonally -> blocked.
        Describe(new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080), Mon("C", 0, 1080, 1920, 2160) })
            .Should().Be("A.TopLeft,B.BottomRight,B.TopRight,C.BottomLeft,C.BottomRight");
    }

    [Fact]
    public void Mirrored_displays_with_identical_bounds_share_corner_pixels_so_each_corner_exists_once()
    {
        var c = HotCornerDetector.UsableCorners(new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 0, 0, 1920, 1080) });
        c.Should().HaveCount(4);
        c.Select(x => x.MonitorId).Distinct().Should().Equal("A");
    }

    [Fact]
    public void Empty_or_degenerate_monitors_are_ignored()
    {
        HotCornerDetector.UsableCorners(Array.Empty<HotCornerMonitor>()).Should().BeEmpty();
        HotCornerDetector.UsableCorners(new[] { Mon("Z", 10, 10, 10, 500) }).Should().BeEmpty();
    }

    // ---- dwell ----

    [Fact]
    public void Fires_after_the_dwell_time_and_not_before()
    {
        var rig = Single();
        rig.Step(0, 0).Should().BeNull();            // entered, t=0
        rig.Step(0, 0).Should().BeNull();            // 40
        rig.Step(0, 0).Should().BeNull();            // 80
        rig.Step(0, 0).Should().BeNull();            // 120
        var hit = rig.Step(0, 0);                    // 160 >= 150
        hit.Should().Be(new HotCornerTrigger("A", ScreenCorner.TopLeft, HotCornerAction.MissionControl));
    }

    [Fact]
    public void Dwell_time_is_configurable()
    {
        var rig = Single();
        rig.Dwell = TimeSpan.FromMilliseconds(500);
        rig.Hold(0, 0, 440).Should().BeNull();
        rig.Hold(0, 0, 200).Should().NotBeNull();
    }

    [Fact]
    public void A_corner_fires_exactly_when_the_elapsed_time_reaches_the_dwell()
    {
        var rig = Single();
        rig.Step(0, 0, 0).Should().BeNull();
        rig.Step(0, 0, 149).Should().BeNull();
        rig.Step(0, 0, 1).Should().NotBeNull();
    }

    [Fact]
    public void Leaving_the_zone_before_the_dwell_resets_it()
    {
        var rig = Single();
        rig.Hold(0, 0, 120).Should().BeNull();
        rig.Step(30, 30).Should().BeNull();          // left
        rig.Hold(0, 0, 120).Should().BeNull();       // 120 < 150 since re-entry
        rig.Hold(0, 0, 80).Should().NotBeNull();
    }

    [Theory]
    [InlineData(2, 2)]
    [InlineData(0, 2)]
    [InlineData(2, 0)]
    [InlineData(1, 1)]
    public void The_pointer_counts_as_in_the_corner_within_two_pixels(int x, int y)
    {
        var rig = Single();
        rig.Hold(x, y, 400).Should().NotBeNull();
    }

    [Theory]
    [InlineData(3, 0)]
    [InlineData(0, 3)]
    [InlineData(3, 3)]
    [InlineData(10, 10)]
    [InlineData(100, 100)]
    public void More_than_two_pixels_away_is_not_the_corner(int x, int y)
    {
        var rig = Single();
        rig.Hold(x, y, 1000).Should().BeNull();
    }

    [Theory]
    [InlineData(1919, 0, ScreenCorner.TopRight)]
    [InlineData(0, 1079, ScreenCorner.BottomLeft)]
    [InlineData(1919, 1079, ScreenCorner.BottomRight)]
    [InlineData(1917, 1081 - 2, ScreenCorner.BottomRight)]
    public void All_four_corners_report_their_corner(int x, int y, ScreenCorner corner)
    {
        var rig = Single();
        rig.Hold(x, y, 400)!.Value.Corner.Should().Be(corner);
    }

    [Fact]
    public void Each_corner_returns_its_own_action()
    {
        var rig = Single();
        rig.Actions = new HotCornerActions(HotCornerAction.Spotlight, HotCornerAction.Launchpad, HotCornerAction.ShowDesktop, HotCornerAction.Sleep);
        rig.Hold(0, 0, 400)!.Value.Action.Should().Be(HotCornerAction.Spotlight);
        rig.Step(500, 500, 600);
        rig.Hold(1919, 0, 400)!.Value.Action.Should().Be(HotCornerAction.Launchpad);
        rig.Step(500, 500, 600);
        rig.Hold(0, 1079, 400)!.Value.Action.Should().Be(HotCornerAction.ShowDesktop);
        rig.Step(500, 500, 600);
        rig.Hold(1919, 1079, 400)!.Value.Action.Should().Be(HotCornerAction.Sleep);
    }

    [Fact]
    public void Corners_without_an_action_are_ignored()
    {
        var rig = Single();
        rig.Actions = new HotCornerActions(HotCornerAction.None, HotCornerAction.MissionControl, HotCornerAction.None, HotCornerAction.None);
        rig.Hold(0, 0, 1000).Should().BeNull();
        rig.Hold(1919, 1079, 1000).Should().BeNull();
        rig.Hold(1919, 0, 400).Should().NotBeNull();
    }

    // ---- once per entry, re-arm ----

    [Fact]
    public void Fires_once_per_entry_even_if_the_pointer_stays()
    {
        var rig = Single();
        rig.Hold(0, 0, 400).Should().NotBeNull();
        rig.Hold(0, 0, 5000).Should().BeNull();
    }

    [Fact]
    public void Wiggling_inside_the_rearm_radius_does_not_rearm()
    {
        var rig = Single();
        rig.Hold(0, 0, 400).Should().NotBeNull();
        for (var i = 0; i < 20; i++)
        {
            rig.Step(10, 10, 600).Should().BeNull();   // 14 px away: still disarmed
            rig.Step(0, 0, 600).Should().BeNull();
        }
    }

    [Fact]
    public void Rearms_after_leaving_a_24_pixel_radius()
    {
        var rig = Single();
        rig.Hold(0, 0, 400).Should().NotBeNull();
        rig.Step(17, 17, 600);                         // 24.04 px away
        rig.Hold(0, 0, 400).Should().NotBeNull();
    }

    [Fact]
    public void Exactly_24_pixels_away_is_not_yet_outside_the_radius()
    {
        var rig = Single();
        rig.Hold(0, 0, 400).Should().NotBeNull();
        rig.Step(24, 0, 600);                          // distance 24 is not > 24
        rig.Hold(0, 0, 400).Should().BeNull();
        rig.Step(25, 0, 600);                          // 25 > 24
        rig.Hold(0, 0, 400).Should().NotBeNull();
    }

    [Fact]
    public void Leaving_to_the_far_side_of_the_screen_rearms()
    {
        var rig = Single();
        rig.Hold(0, 0, 400).Should().NotBeNull();
        rig.Step(900, 500, 600);
        rig.Hold(0, 0, 400).Should().NotBeNull();
    }

    // ---- refractory ----

    [Fact]
    public void Refractory_period_delays_a_second_corner_until_500ms_after_the_first_trigger()
    {
        var rig = Single();
        rig.Actions = new HotCornerActions(HotCornerAction.Spotlight, HotCornerAction.Launchpad, HotCornerAction.None, HotCornerAction.None);
        rig.Hold(0, 0, 400)!.Value.Corner.Should().Be(ScreenCorner.TopLeft);
        var firedAt = rig.Now;
        // flick to the opposite corner and dwell there: the dwell passes quickly, but 500 ms must pass since the previous trigger
        HotCornerTrigger? second = null;
        while (second is null && rig.Now - firedAt < TimeSpan.FromSeconds(2)) second = rig.Step(1919, 0);
        second.Should().NotBeNull();
        (rig.Now - firedAt).Should().BeGreaterThanOrEqualTo(HotCornerDetector.RefractoryPeriod);
        (rig.Now - firedAt).Should().BeLessThan(HotCornerDetector.RefractoryPeriod + TimeSpan.FromMilliseconds(80));
    }

    [Fact]
    public void Refractory_period_constants_match_the_spec()
    {
        HotCornerDetector.RefractoryPeriod.Should().Be(TimeSpan.FromMilliseconds(500));
        HotCornerDetector.CornerTolerancePx.Should().Be(2);
        HotCornerDetector.RearmRadiusPx.Should().Be(24);
    }

    // ---- buttons ----

    [Fact]
    public void While_a_mouse_button_is_down_nothing_fires()
    {
        var rig = Single();
        rig.Hold(0, 0, 2000, down: true).Should().BeNull();
    }

    [Fact]
    public void A_drag_that_ends_in_the_corner_does_not_fire_after_the_release()
    {
        // dragging a window into the corner to snap it must not pop Mission Control when the button is released
        var rig = Single();
        rig.Hold(0, 0, 600, down: true).Should().BeNull();
        rig.Hold(0, 0, 2000, down: false).Should().BeNull();
        rig.Step(200, 200, 600);                        // leave
        rig.Hold(0, 0, 400).Should().NotBeNull();       // a fresh visit works again
    }

    [Fact]
    public void Pressing_a_button_during_the_dwell_disarms_the_corner_until_the_pointer_leaves()
    {
        var rig = Single();
        rig.Hold(0, 0, 80).Should().BeNull();
        rig.Step(0, 0, 40, down: true).Should().BeNull();
        rig.Hold(0, 0, 1000).Should().BeNull();
    }

    [Fact]
    public void A_button_held_far_from_any_corner_does_not_affect_later_visits()
    {
        var rig = Single();
        rig.Step(800, 500, 40, down: true);
        rig.Step(800, 500, 40, down: false);
        rig.Hold(0, 0, 400).Should().NotBeNull();
    }

    // ---- fullscreen ----

    [Fact]
    public void A_fullscreen_app_suppresses_the_trigger_and_the_check_is_lazy()
    {
        var rig = Single();
        var asked = new List<string>();
        rig.Fullscreen = id => { asked.Add(id); return true; };
        rig.Step(500, 500);
        asked.Should().BeEmpty("the fullscreen query is only made when a corner is about to fire");
        rig.Hold(0, 0, 1000).Should().BeNull();
        asked.Should().Equal("A");                      // asked once, then the corner is disarmed
    }

    [Fact]
    public void After_a_fullscreen_suppression_the_corner_works_again_once_the_pointer_left_and_the_app_is_gone()
    {
        var rig = Single();
        var fullscreen = true;
        rig.Fullscreen = _ => fullscreen;
        rig.Hold(0, 0, 400).Should().BeNull();
        fullscreen = false;
        rig.Hold(0, 0, 400).Should().BeNull("still disarmed: the pointer never left");
        rig.Step(100, 100, 600);
        rig.Hold(0, 0, 400).Should().NotBeNull();
    }

    [Fact]
    public void A_suppressed_fullscreen_trigger_does_not_start_the_refractory_period()
    {
        var rig = Single();
        rig.Actions = new HotCornerActions(HotCornerAction.Spotlight, HotCornerAction.Launchpad, HotCornerAction.None, HotCornerAction.None);
        rig.Fullscreen = _ => true;
        rig.Hold(0, 0, 400).Should().BeNull();
        rig.Fullscreen = _ => false;
        rig.Hold(1919, 0, 240).Should().NotBeNull();
    }

    [Fact]
    public void Fullscreen_is_asked_for_the_monitor_that_owns_the_corner()
    {
        var rig = new Rig(Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080));
        var asked = new List<string>();
        rig.Fullscreen = id => { asked.Add(id); return id == "B"; };
        rig.Hold(3839, 0, 400).Should().BeNull();           // B is fullscreen
        rig.Hold(0, 0, 400).Should().NotBeNull();           // A is not
        asked.Should().Equal("B", "A");
    }

    // ---- multi monitor behaviour ----

    [Fact]
    public void The_seam_between_side_by_side_monitors_is_never_a_hot_corner()
    {
        var rig = new Rig(Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080));
        rig.Hold(1919, 0, 2000).Should().BeNull();
        rig.Hold(1920, 0, 2000).Should().BeNull();
        rig.Hold(1919, 1079, 2000).Should().BeNull();
        rig.Hold(1920, 1079, 2000).Should().BeNull();
    }

    [Fact]
    public void The_seam_between_stacked_monitors_is_never_a_hot_corner()
    {
        var rig = new Rig(Mon("A", 0, 0, 1920, 1080), Mon("B", 0, 1080, 1920, 2160));
        rig.Hold(0, 1079, 2000).Should().BeNull();
        rig.Hold(0, 1080, 2000).Should().BeNull();
        rig.Hold(1919, 1079, 2000).Should().BeNull();
        rig.Hold(1919, 1080, 2000).Should().BeNull();
    }

    [Fact]
    public void Outer_corners_of_each_monitor_report_that_monitor()
    {
        var rig = new Rig(Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080));
        rig.Hold(0, 0, 400)!.Value.MonitorId.Should().Be("A");
        rig.Step(500, 500, 600);
        rig.Hold(3839, 0, 400)!.Value.MonitorId.Should().Be("B");
        rig.Step(2500, 500, 600);
        rig.Hold(3839, 1079, 400)!.Value.MonitorId.Should().Be("B");
        rig.Step(2500, 500, 600);
        rig.Hold(0, 1079, 400)!.Value.MonitorId.Should().Be("A");
    }

    [Fact]
    public void Negative_coordinate_monitor_corners_work()
    {
        var rig = new Rig(Mon("L", -1920, 0, 0, 1080), Mon("P", 0, 0, 1920, 1080));
        rig.Hold(-1920, 0, 400)!.Value.Should().Be(new HotCornerTrigger("L", ScreenCorner.TopLeft, HotCornerAction.MissionControl));
        rig.Step(-500, 500, 600);
        rig.Hold(-1, 0, 2000).Should().BeNull();           // L.TR faces the primary monitor
        rig.Hold(0, 0, 2000).Should().BeNull();            // P.TL faces L
        rig.Hold(1919, 1079, 400)!.Value.MonitorId.Should().Be("P");
    }

    [Fact]
    public void Mixed_size_overhang_corner_is_usable()
    {
        var rig = new Rig(Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 4480, 1440));
        rig.Hold(1920, 1439, 400)!.Value.Should().Be(new HotCornerTrigger("B", ScreenCorner.BottomLeft, HotCornerAction.MissionControl));
        rig.Step(3000, 700, 600);
        rig.Hold(1919, 1079, 2000).Should().BeNull();      // A.BR is shared with B
    }

    [Fact]
    public void Mirrored_displays_fire_only_once()
    {
        var rig = new Rig(Mon("A", 0, 0, 1920, 1080), Mon("B", 0, 0, 1920, 1080));
        rig.Hold(0, 0, 400).Should().NotBeNull();
        rig.Hold(0, 0, 400).Should().BeNull();
    }

    [Fact]
    public void A_pointer_outside_every_monitor_never_fires()
    {
        var rig = new Rig(Mon("A", 100, 100, 1000, 800));
        rig.Hold(99, 99, 1000).Should().BeNull();
        rig.Hold(98, 100, 1000).Should().BeNull();
        rig.Hold(-5000, -5000, 1000).Should().BeNull();
    }

    [Fact]
    public void Monitor_not_at_the_origin_works()
    {
        var rig = new Rig(Mon("A", 300, 200, 1300, 900));
        rig.Hold(300, 200, 400).Should().NotBeNull();
        rig.Step(700, 500, 600);
        rig.Hold(1299, 899, 400).Should().NotBeNull();
    }

    [Fact]
    public void Changing_the_monitor_layout_drops_state_of_vanished_corners()
    {
        var rig = new Rig(Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080));
        rig.Hold(3839, 0, 400).Should().NotBeNull();
        rig.Monitors = new[] { Mon("A", 0, 0, 1920, 1080) };    // B unplugged
        rig.Hold(3839, 0, 400).Should().BeNull();
        rig.Monitors = new[] { Mon("A", 0, 0, 1920, 1080), Mon("B", 1920, 0, 3840, 1080) };
        rig.Hold(3839, 0, 400).Should().NotBeNull("B is a new corner state again");
    }

    [Fact]
    public void Reset_forgets_dwell_and_arming_state()
    {
        var rig = Single();
        rig.Hold(0, 0, 400).Should().NotBeNull();
        rig.Hold(0, 0, 400).Should().BeNull();
        rig.Detector.Reset();
        rig.Hold(0, 0, 400).Should().NotBeNull();
    }

    [Fact]
    public void Reset_cancels_a_running_dwell()
    {
        var rig = Single();
        rig.Hold(0, 0, 120).Should().BeNull();
        rig.Detector.Reset();
        rig.Hold(0, 0, 120).Should().BeNull();
        rig.Hold(0, 0, 60).Should().NotBeNull();
    }

    [Fact]
    public void The_clock_going_backwards_never_fires_by_accident()
    {
        var rig = Single();
        rig.Step(0, 0);
        rig.Now -= TimeSpan.FromSeconds(5);
        rig.Step(0, 0, 0).Should().BeNull();
    }

    // ---- actions record ----

    [Fact]
    public void HotCornerActions_reads_the_settings_and_knows_when_nothing_is_assigned()
    {
        var s = new HotCornerSettings { TopLeft = HotCornerAction.None, TopRight = HotCornerAction.None, BottomLeft = HotCornerAction.None, BottomRight = HotCornerAction.None };
        HotCornerActions.From(s).AnyAssigned.Should().BeFalse();
        HotCornerActions.From(s with { BottomRight = HotCornerAction.Sleep }).AnyAssigned.Should().BeTrue();
        var a = HotCornerActions.From(new HotCornerSettings());
        a.For(ScreenCorner.TopLeft).Should().Be(HotCornerAction.MissionControl);
        a.For(ScreenCorner.BottomRight).Should().Be(HotCornerAction.None);
    }
}
