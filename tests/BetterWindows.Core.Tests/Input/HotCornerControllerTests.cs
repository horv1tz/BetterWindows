using BetterWindows.Core.Controllers;
using BetterWindows.Core.Input;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Status;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Input;

public class HotCornerControllerTests
{
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(40);

    private static CoreHarness NewHarness(Action<HotCornerSettings>? _ = null, Action<IServiceCollection>? more = null) => Kit.Harness<HotCornerController>(sc =>
    {
        sc.AddSingleton<CountingCursor>();
        sc.AddSingleton<ICursorProvider>(sp => sp.GetRequiredService<CountingCursor>());
        more?.Invoke(sc);
    });

    private static void Configure(CoreHarness h, Func<HotCornerSettings, HotCornerSettings> f) => h.Settings().Set(s => s with { HotCorners = f(s.HotCorners) });

    private static void Hold(CoreHarness h, int x, int y, int ms)
    {
        h.Get<CountingCursor>().Position = new NativePoint(x, y);
        for (var t = 0; t < ms; t += 40) h.Time.Advance(Tick);
    }

    private static MonitorInfo Monitor(string id, int x, int y, int w, int h, bool primary = false) => new()
    {
        Id = id, Index = 0, Primary = primary, Name = id, Scale = 1,
        BoundsPx = new Rect { X = x, Y = y, Width = w, Height = h }, WorkAreaPx = new Rect { X = x, Y = y, Width = w, Height = h },
    };

    // ---- polling only while needed ----

    [Fact]
    public void Default_settings_poll_because_top_left_has_mission_control()
    {
        using var h = NewHarness();
        var c = h.StartAll<HotCornerController>();
        c.IsPolling.Should().BeTrue();
        h.Time.Advance(Tick);
        h.Get<CountingCursor>().Polls.Should().Be(1);
    }

    [Fact]
    public void Polls_every_40_ms()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        h.Time.Advance(TimeSpan.FromMilliseconds(400));
        h.Get<CountingCursor>().Polls.Should().Be(10);
        HotCornerController.PollInterval.Should().Be(TimeSpan.FromMilliseconds(40));
    }

    [Fact]
    public void Disabled_hot_corners_create_no_timer_and_never_poll()
    {
        using var h = NewHarness();
        Configure(h, s => s with { Enabled = false });
        var c = h.StartAll<HotCornerController>();
        c.IsPolling.Should().BeFalse();
        h.Time.Advance(TimeSpan.FromSeconds(5));
        h.Get<CountingCursor>().Polls.Should().Be(0);
    }

    [Fact]
    public void Enabled_but_every_corner_none_does_not_poll()
    {
        using var h = NewHarness();
        Configure(h, s => s with { TopLeft = HotCornerAction.None });
        var c = h.StartAll<HotCornerController>();
        c.IsPolling.Should().BeFalse();
        h.Time.Advance(TimeSpan.FromSeconds(5));
        h.Get<CountingCursor>().Polls.Should().Be(0);
    }

    [Fact]
    public void Polling_starts_and_stops_with_the_settings()
    {
        using var h = NewHarness();
        Configure(h, s => s with { Enabled = false });
        var c = h.StartAll<HotCornerController>();
        c.IsPolling.Should().BeFalse();

        Configure(h, s => s with { Enabled = true });
        c.IsPolling.Should().BeTrue();
        h.Time.Advance(Tick);
        h.Get<CountingCursor>().Polls.Should().Be(1);

        Configure(h, s => s with { TopLeft = HotCornerAction.None });
        c.IsPolling.Should().BeFalse();
        h.Time.Advance(TimeSpan.FromSeconds(2));
        h.Get<CountingCursor>().Polls.Should().Be(1);

        Configure(h, s => s with { BottomRight = HotCornerAction.Launchpad });
        c.IsPolling.Should().BeTrue();
    }

    [Fact]
    public void Stop_disposes_the_timer()
    {
        using var h = NewHarness();
        var c = h.StartAll<HotCornerController>();
        c.Stop();
        c.IsPolling.Should().BeFalse();
        h.Time.Advance(TimeSpan.FromSeconds(2));
        h.Get<CountingCursor>().Polls.Should().Be(0);
        Configure(h, s => s with { BottomRight = HotCornerAction.Sleep });
        c.IsPolling.Should().BeFalse("a stopped controller ignores settings changes");
    }

    // ---- triggering ----

    [Fact]
    public void Dwelling_in_the_top_left_corner_opens_Mission_Control_on_that_monitor()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 120);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty("150 ms dwell not reached");
        Hold(h, 0, 0, 80);
        var shown = h.Get<FakeSurfaceManager>().Shown;
        shown.Should().HaveCount(1);
        shown[0].Request.Mode.Should().Be(OverlayMode.MissionControl);
        shown[0].Request.MonitorId.Should().Be("m1");
        shown[0].Source.Should().BeNull();
    }

    [Fact]
    public void The_dwell_setting_is_honoured()
    {
        using var h = NewHarness();
        Configure(h, s => s with { DwellMs = 600 });
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 560);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
        Hold(h, 0, 0, 80);
        h.Get<FakeSurfaceManager>().Shown.Should().HaveCount(1);
    }

    [Fact]
    public void A_dwell_change_applies_immediately()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 80);
        Configure(h, s => s with { DwellMs = 50 });
        Hold(h, 0, 0, 40);
        h.Get<FakeSurfaceManager>().Shown.Should().HaveCount(1);
    }

    [Fact]
    public void Fires_once_per_entry()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 3000);
        h.Get<FakeSurfaceManager>().Shown.Should().HaveCount(1);
    }

    [Fact]
    public void Leaving_and_returning_fires_again_and_toggles_the_overlay_off()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        Hold(h, 0, 0, 300);
        surfaces.Shown.Should().HaveCount(1);
        Hold(h, 800, 500, 800);                    // away, past the refractory period
        Hold(h, 0, 0, 300);
        surfaces.HideCount.Should().Be(1, "same mode while visible hides it");
        surfaces.Shown.Should().HaveCount(1);
    }

    [Theory]
    [InlineData(HotCornerAction.Launchpad, OverlayMode.Launchpad)]
    [InlineData(HotCornerAction.Spotlight, OverlayMode.Spotlight)]
    [InlineData(HotCornerAction.MissionControl, OverlayMode.MissionControl)]
    public void Overlay_actions_show_their_mode(HotCornerAction action, OverlayMode mode)
    {
        using var h = NewHarness();
        Configure(h, s => s with { TopLeft = action });
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 300);
        h.Get<FakeSurfaceManager>().Shown.Should().ContainSingle().Which.Request.Mode.Should().Be(mode);
    }

    [Fact]
    public void Show_desktop_goes_through_the_input_simulator()
    {
        using var h = NewHarness();
        Configure(h, s => s with { TopRight = HotCornerAction.ShowDesktop });
        h.StartAll<HotCornerController>();
        Hold(h, 1919, 0, 300);
        h.Get<FakeInputSimulator>().Calls.Should().Equal("showDesktop");
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
    }

    [Fact]
    public void Lock_and_sleep_go_through_the_power_port()
    {
        using var h = NewHarness();
        Configure(h, s => s with { BottomLeft = HotCornerAction.LockScreen, BottomRight = HotCornerAction.Sleep });
        h.StartAll<HotCornerController>();
        Hold(h, 0, 1079, 300);
        Hold(h, 900, 500, 700);
        Hold(h, 1919, 1079, 300);
        h.Get<FakeSystemPower>().Actions.Should().Equal(PowerAction.Lock, PowerAction.Sleep);
    }

    // ---- suppression ----

    [Fact]
    public void Nothing_fires_while_a_mouse_button_is_down()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        h.Get<CountingCursor>().ButtonDown = true;
        Hold(h, 0, 0, 2000);
        h.Get<CountingCursor>().ButtonDown = false;
        Hold(h, 0, 0, 2000);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
    }

    [Fact]
    public void A_fullscreen_app_suppresses_hot_corners()
    {
        using var h = NewHarness();
        h.Get<FakeWindowSystem>().FullscreenActive = true;
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 2000);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
    }

    [Fact]
    public void An_unavailable_pointer_skips_the_tick_and_resets_the_dwell()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 120);
        h.Get<CountingCursor>().Unavailable = true;       // UAC prompt / lock screen
        Hold(h, 0, 0, 2000);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
        h.Get<CountingCursor>().Unavailable = false;
        Hold(h, 0, 0, 120);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty("the dwell started over after the gap");
        Hold(h, 0, 0, 80);
        h.Get<FakeSurfaceManager>().Shown.Should().HaveCount(1);
    }

    [Fact]
    public void Only_corners_with_an_action_fire()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        Hold(h, 1919, 0, 1000);
        Hold(h, 1919, 1079, 1000);
        Hold(h, 0, 1079, 1000);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
    }

    // ---- monitors ----

    [Fact]
    public void Multi_monitor_outer_corners_fire_on_their_own_monitor_and_the_seam_does_not()
    {
        using var h = NewHarness(more: sc => { });
        var mons = h.Get<FakeMonitorProvider>();
        mons.Monitors.Clear();
        mons.Monitors.Add(Monitor("mA", 0, 0, 1920, 1080, primary: true));
        mons.Monitors.Add(Monitor("mB", 1920, 0, 2560, 1440));
        Configure(h, s => s with { TopLeft = HotCornerAction.Spotlight, TopRight = HotCornerAction.MissionControl, BottomRight = HotCornerAction.Launchpad });
        h.StartAll<HotCornerController>();
        var surfaces = h.Get<FakeSurfaceManager>();

        Hold(h, 1919, 0, 1000);                    // the seam: A's top right is next to B
        surfaces.Shown.Should().BeEmpty();

        Hold(h, 4479, 0, 300);                     // B's top right
        surfaces.Shown.Should().HaveCount(1);
        surfaces.Shown[0].Request.Should().Match<OverlayRequest>(r => r.MonitorId == "mB" && r.Mode == OverlayMode.MissionControl);

        Hold(h, 0, 0, 800);                        // away
        Hold(h, 0, 0, 300);                        // A's top left
        surfaces.Shown.Should().HaveCount(2);
        surfaces.Shown[1].Request.Should().Match<OverlayRequest>(r => r.MonitorId == "mA" && r.Mode == OverlayMode.Spotlight);
    }

    [Fact]
    public void Monitors_at_negative_coordinates_work()
    {
        using var h = NewHarness();
        var mons = h.Get<FakeMonitorProvider>();
        mons.Monitors.Clear();
        mons.Monitors.Add(Monitor("left", -1920, 0, 1920, 1080));
        mons.Monitors.Add(Monitor("main", 0, 0, 1920, 1080, primary: true));
        h.StartAll<HotCornerController>();
        Hold(h, -1920, 0, 300);                    // left monitor's top left, default action Mission Control
        h.Get<FakeSurfaceManager>().Shown.Should().ContainSingle().Which.Request.MonitorId.Should().Be("left");
    }

    [Fact]
    public void Fullscreen_is_asked_only_for_the_monitor_of_the_corner()
    {
        using var h = NewHarness(more: sc => sc.AddSingleton<IWindowSystem>(_ => new PerMonitorFullscreen("mB")));
        var mons = h.Get<FakeMonitorProvider>();
        mons.Monitors.Clear();
        mons.Monitors.Add(Monitor("mA", 0, 0, 1920, 1080, primary: true));
        mons.Monitors.Add(Monitor("mB", 1920, 0, 1920, 1080));
        Configure(h, s => s with { TopLeft = HotCornerAction.MissionControl, TopRight = HotCornerAction.Launchpad });
        h.StartAll<HotCornerController>();
        Hold(h, 3839, 0, 1000);                    // B's top right: a fullscreen app runs on B
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
        Hold(h, 0, 0, 300);                        // A is fine
        h.Get<FakeSurfaceManager>().Shown.Should().ContainSingle().Which.Request.MonitorId.Should().Be("mA");
    }

    [Fact]
    public void Monitor_changes_are_picked_up()
    {
        using var h = NewHarness();
        var mons = h.Get<FakeMonitorProvider>();
        var c = h.StartAll<HotCornerController>();
        Hold(h, 1919, 0, 300);                     // top right has no action: nothing
        mons.Monitors[0] = Monitor("m1", 0, 0, 1280, 720, primary: true);   // resolution changed
        mons.RaiseChanged();
        Hold(h, 0, 0, 300);
        h.Get<FakeSurfaceManager>().Shown.Should().HaveCount(1);
        c.IsPolling.Should().BeTrue();
    }

    [Fact]
    public void Disabling_while_a_dwell_is_running_cancels_it()
    {
        using var h = NewHarness();
        h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 120);
        Configure(h, s => s with { Enabled = false });
        Hold(h, 0, 0, 1000);
        Configure(h, s => s with { Enabled = true });
        Hold(h, 0, 0, 120);
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty("the dwell started over after re-enabling");
    }

    [Fact]
    public void A_failing_action_does_not_stop_the_polling()
    {
        using var h = NewHarness(more: sc => sc.AddSingleton<IInputSimulator, ThrowingInput>());
        Configure(h, s => s with { TopLeft = HotCornerAction.ShowDesktop });
        var c = h.StartAll<HotCornerController>();
        Hold(h, 0, 0, 300);
        c.IsPolling.Should().BeTrue();
        Hold(h, 900, 500, 800);
        var polls = h.Get<CountingCursor>().Polls;
        Hold(h, 900, 500, 200);
        h.Get<CountingCursor>().Polls.Should().BeGreaterThan(polls);
    }

    private sealed class ThrowingInput : IInputSimulator
    {
        public void ShowDesktop() => throw new InvalidOperationException("boom");
        public void SwitchDesktopLeft() { }
        public void SwitchDesktopRight() { }
        public void SendTaskView() { }
    }

    /// <summary>A window system that reports a fullscreen app only on one monitor id.</summary>
    private sealed class PerMonitorFullscreen : IWindowSystem
    {
        private readonly string _monitor;
        public PerMonitorFullscreen(string monitor) => _monitor = monitor;
        public IReadOnlyList<RawWindow> EnumerateTopLevel() => Array.Empty<RawWindow>();
        public RawWindow? TryDescribe(long handle) => null;
        public long GetForegroundWindow() => 0;
        public event Action<WindowEvent>? WindowChanged { add { } remove { } }
        public void Start() { }
        public void Stop() { }
        public bool Activate(long handle) => false;
        public bool Minimize(long handle) => false;
        public bool Restore(long handle) => false;
        public bool Maximize(long handle) => false;
        public bool Close(long handle) => false;
        public bool IsFullscreenWindowActive(MonitorInfo monitor) => monitor.Id == _monitor;
        public event Action? FullscreenChanged { add { } remove { } }
        public void Dispose() { }
    }
}
