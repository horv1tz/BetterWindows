using BetterWindows.Core.Ipc;
using BetterWindows.Core.Overlay;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Status;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Overlay;

public class ShellActionsTests
{
    private static CoreHarness NewHarness(Action<IServiceCollection>? more = null) => Kit.Harness(more);

    private static ShellActions Actions(CoreHarness h) => ShellActions.Create(h.Services);

    private static MonitorInfo Monitor(string id, int x, int w = 1920) => new()
    {
        Id = id, Name = id, Scale = 1,
        BoundsPx = new Rect { X = x, Y = 0, Width = w, Height = 1080 }, WorkAreaPx = new Rect { X = x, Y = 0, Width = w, Height = 1040 },
    };

    // ---- toggle semantics ----

    [Theory]
    [InlineData(ShellAction.Spotlight, OverlayMode.Spotlight)]
    [InlineData(ShellAction.Launchpad, OverlayMode.Launchpad)]
    [InlineData(ShellAction.MissionControl, OverlayMode.MissionControl)]
    public void An_overlay_action_shows_its_mode_when_hidden_and_hides_it_when_visible_in_the_same_mode(ShellAction action, OverlayMode mode)
    {
        using var h = NewHarness();
        var actions = Actions(h);
        var surfaces = h.Get<FakeSurfaceManager>();

        actions.Execute(action).Should().BeTrue();
        surfaces.Shown.Should().ContainSingle().Which.Request.Mode.Should().Be(mode);
        surfaces.Shown[0].Source.Should().BeNull();
        h.Get<OverlayStateService>().Current.Should().Match<OverlayState>(s => s.Visible && s.Mode == mode);

        actions.Execute(action).Should().BeTrue();
        surfaces.HideCount.Should().Be(1);
        surfaces.Shown.Should().HaveCount(1);
        h.Get<OverlayStateService>().Current.Should().Be(OverlayState.Hidden);

        actions.Execute(action);
        surfaces.Shown.Should().HaveCount(2);
    }

    [Fact]
    public void A_different_mode_replaces_the_visible_one_instead_of_hiding()
    {
        using var h = NewHarness();
        var actions = Actions(h);
        var surfaces = h.Get<FakeSurfaceManager>();
        actions.Execute(ShellAction.Spotlight);
        actions.Execute(ShellAction.Launchpad);
        actions.Execute(ShellAction.MissionControl);
        surfaces.HideCount.Should().Be(0);
        surfaces.Shown.Select(s => s.Request.Mode).Should().Equal(OverlayMode.Spotlight, OverlayMode.Launchpad, OverlayMode.MissionControl);
        h.Get<OverlayStateService>().Current.Mode.Should().Be(OverlayMode.MissionControl);
    }

    [Fact]
    public void A_visible_popover_is_replaced_by_the_requested_mode()
    {
        using var h = NewHarness();
        var surfaces = h.Get<FakeSurfaceManager>();
        surfaces.ShowOverlay(new OverlayRequest { Mode = OverlayMode.Popover, Popover = new PopoverRequest { Kind = PopoverKind.Apple, Anchor = new Rect() } }, new SurfaceId(SurfaceKind.Menubar, "m1"));
        Actions(h).Execute(ShellAction.Spotlight);
        surfaces.HideCount.Should().Be(0);
        surfaces.Shown.Last().Request.Mode.Should().Be(OverlayMode.Spotlight);
    }

    [Fact]
    public void The_toggle_follows_the_host_when_the_overlay_was_hidden_behind_our_back()
    {
        using var h = NewHarness();
        var actions = Actions(h);
        var surfaces = h.Get<FakeSurfaceManager>();
        actions.Execute(ShellAction.Spotlight);
        surfaces.SimulateHostHide();                                         // e.g. deactivation
        actions.Execute(ShellAction.Spotlight);
        surfaces.HideCount.Should().Be(0);
        surfaces.Shown.Should().HaveCount(2);
    }

    // ---- target monitor ----

    [Fact]
    public void The_overlay_opens_on_the_monitor_under_the_cursor()
    {
        using var h = NewHarness();
        var mons = h.Get<FakeMonitorProvider>();
        mons.Monitors.Clear();
        mons.Monitors.AddRange(new[] { Monitor("m1", 0), Monitor("m2", 1920) });
        var actions = Actions(h);

        h.Get<FakeCursorProvider>().Position = new NativePoint(2500, 300);
        actions.Execute(ShellAction.Spotlight);
        h.Get<FakeSurfaceManager>().Shown.Last().Request.MonitorId.Should().Be("m2");
        h.Get<OverlayStateService>().Current.MonitorId.Should().Be("m2");

        actions.Execute(ShellAction.Spotlight);
        h.Get<FakeCursorProvider>().Position = new NativePoint(100, 100);
        actions.Execute(ShellAction.Launchpad);
        h.Get<FakeSurfaceManager>().Shown.Last().Request.MonitorId.Should().Be("m1");
    }

    [Fact]
    public void An_explicit_monitor_overrides_the_cursor()
    {
        using var h = NewHarness();
        var mons = h.Get<FakeMonitorProvider>();
        mons.Monitors.Clear();
        mons.Monitors.AddRange(new[] { Monitor("m1", 0), Monitor("m2", 1920) });
        h.Get<FakeCursorProvider>().Position = new NativePoint(10, 10);
        Actions(h).Execute(ShellAction.MissionControl, "m2");
        h.Get<FakeSurfaceManager>().Shown.Single().Request.MonitorId.Should().Be("m2");
    }

    [Fact]
    public void Without_a_cursor_the_host_picks_the_monitor()
    {
        using var h = NewHarness();
        h.Get<FakeCursorProvider>().Unavailable = true;
        Actions(h).Execute(ShellAction.Spotlight);
        h.Get<FakeSurfaceManager>().Shown.Single().Request.MonitorId.Should().BeNull();
    }

    [Fact]
    public void A_failing_monitor_lookup_does_not_prevent_the_overlay()
    {
        using var h = NewHarness(sc => sc.AddSingleton<IMonitorProvider, ThrowingMonitors>());
        Actions(h).Execute(ShellAction.Spotlight).Should().BeTrue();
        h.Get<FakeSurfaceManager>().Shown.Single().Request.MonitorId.Should().BeNull();
    }

    private sealed class ThrowingMonitors : IMonitorProvider
    {
        public IReadOnlyList<MonitorInfo> GetMonitors() => throw new InvalidOperationException("boom");
        public MonitorInfo? FromPoint(int xPx, int yPx) => throw new InvalidOperationException("boom");
        public event Action? MonitorsChanged { add { } remove { } }
    }

    // ---- other actions ----

    [Fact]
    public void ShowDesktop_goes_through_the_input_simulator()
    {
        using var h = NewHarness();
        Actions(h).Execute(ShellAction.ShowDesktop).Should().BeTrue();
        h.Get<FakeInputSimulator>().Calls.Should().Equal("showDesktop");
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
    }

    [Theory]
    [InlineData(ShellAction.LockScreen, PowerAction.Lock)]
    [InlineData(ShellAction.Sleep, PowerAction.Sleep)]
    public void Lock_and_sleep_go_through_the_power_port(ShellAction action, PowerAction expected)
    {
        using var h = NewHarness();
        Actions(h).Execute(action).Should().BeTrue();
        h.Get<FakeSystemPower>().Actions.Should().Equal(expected);
    }

    [Theory]
    [InlineData(ShellAction.ShowDesktop)]
    [InlineData(ShellAction.LockScreen)]
    [InlineData(ShellAction.Sleep)]
    public void System_actions_hide_an_open_overlay_first(ShellAction action)
    {
        using var h = NewHarness();
        var actions = Actions(h);
        actions.Execute(ShellAction.MissionControl);
        actions.Execute(action);
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(1);
        h.Get<OverlayStateService>().Current.Should().Be(OverlayState.Hidden);
    }

    [Fact]
    public void System_actions_do_not_call_HideOverlay_when_nothing_is_open()
    {
        using var h = NewHarness();
        Actions(h).Execute(ShellAction.ShowDesktop);
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0);
    }

    // ---- hot corner entry point ----

    [Theory]
    [InlineData(HotCornerAction.MissionControl, OverlayMode.MissionControl)]
    [InlineData(HotCornerAction.Launchpad, OverlayMode.Launchpad)]
    [InlineData(HotCornerAction.Spotlight, OverlayMode.Spotlight)]
    public void Hot_corner_overlay_actions_map_to_the_same_modes(HotCornerAction action, OverlayMode mode)
    {
        using var h = NewHarness();
        Actions(h).Execute(action).Should().BeTrue();
        h.Get<FakeSurfaceManager>().Shown.Single().Request.Mode.Should().Be(mode);
    }

    [Fact]
    public void Hot_corner_system_actions_map_to_the_ports()
    {
        using var h = NewHarness();
        var actions = Actions(h);
        actions.Execute(HotCornerAction.ShowDesktop).Should().BeTrue();
        actions.Execute(HotCornerAction.LockScreen).Should().BeTrue();
        actions.Execute(HotCornerAction.Sleep).Should().BeTrue();
        h.Get<FakeInputSimulator>().Calls.Should().Equal("showDesktop");
        h.Get<FakeSystemPower>().Actions.Should().Equal(PowerAction.Lock, PowerAction.Sleep);
    }

    [Fact]
    public void None_and_undefined_hot_corner_actions_do_nothing()
    {
        using var h = NewHarness();
        var actions = Actions(h);
        actions.Execute(HotCornerAction.None).Should().BeFalse();
        actions.Execute((HotCornerAction)123).Should().BeFalse();
        actions.Execute((ShellAction)123).Should().BeFalse();
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
        h.Get<FakeInputSimulator>().Calls.Should().BeEmpty();
        h.Get<FakeSystemPower>().Actions.Should().BeEmpty();
    }

    [Fact]
    public void Hot_corner_actions_pass_the_corner_monitor_on()
    {
        using var h = NewHarness();
        var mons = h.Get<FakeMonitorProvider>();
        mons.Monitors.Clear();
        mons.Monitors.AddRange(new[] { Monitor("m1", 0), Monitor("m2", 1920) });
        Actions(h).Execute(HotCornerAction.Spotlight, "m2");
        h.Get<FakeSurfaceManager>().Shown.Single().Request.MonitorId.Should().Be("m2");
    }

    // ---- robustness ----

    [Fact]
    public void A_port_that_throws_is_logged_not_propagated()
    {
        using var h = NewHarness(sc =>
        {
            sc.AddSingleton<IInputSimulator, ThrowingInput>();
            sc.AddSingleton<ISystemPower, ThrowingPower>();
        });
        var actions = Actions(h);
        actions.Execute(ShellAction.ShowDesktop).Should().BeFalse();
        actions.Execute(ShellAction.LockScreen).Should().BeFalse();
        actions.Execute(ShellAction.Sleep).Should().BeFalse();
    }

    [Fact]
    public void A_surface_manager_that_throws_makes_the_action_report_failure()
    {
        using var h = NewHarness(sc => sc.AddSingleton<ISurfaceManager, ThrowingShow>());
        Actions(h).Execute(ShellAction.Spotlight).Should().BeFalse();
    }

    private sealed class ThrowingInput : IInputSimulator
    {
        public void ShowDesktop() => throw new InvalidOperationException("boom");
        public void SwitchDesktopLeft() { }
        public void SwitchDesktopRight() { }
        public void SendTaskView() { }
    }

    private sealed class ThrowingPower : ISystemPower { public void Perform(PowerAction action) => throw new InvalidOperationException("boom"); }

    private sealed class ThrowingShow : ISurfaceManager
    {
        public bool IsOverlayVisible => false;
        public OverlayMode? CurrentOverlayMode => null;
        public int OverlayToken => 0;
        public bool SupportsGlassOverflow => false;
        public event Action? OverlayHidden { add { } remove { } }
        public void ResizeSurface(SurfaceId surface, double widthDip, double heightDip, double? barThicknessDip) { }
        public void ShowOverlay(OverlayRequest request, SurfaceId? source) => throw new InvalidOperationException("boom");
        public void HideOverlay() { }
        public void SetDockRevealed(SurfaceId dock, bool revealed) { }
        public void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape) { }
        public void ShowSettingsWindow(string? page) { }
        public void SetSurfacesVisible(SurfaceKind? kind, string? monitorId, bool visible, string reason) { }
    }

    [Fact]
    public void Create_prefers_an_instance_registered_in_DI()
    {
        using var h = NewHarness(sc =>
        {
            sc.AddSingleton(sp => ActivatorUtilities.CreateInstance<OverlayCoordinator>(sp));
            sc.AddSingleton(sp => new ShellActions(sp.GetRequiredService<OverlayCoordinator>(), sp.GetRequiredService<IInputSimulator>(), sp.GetRequiredService<ISystemPower>()));
        });
        ShellActions.Create(h.Services).Should().BeSameAs(h.Get<ShellActions>());
    }

    [Fact]
    public void Independent_instances_over_the_same_singletons_agree_on_the_toggle_state()
    {
        using var h = NewHarness();
        var a = ShellActions.Create(h.Services);
        var b = ShellActions.Create(h.Services);
        a.Execute(ShellAction.Spotlight);
        b.Execute(ShellAction.Spotlight);          // sees "visible, same mode" through the host and hides
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(1);
        h.Get<OverlayStateService>().Current.Should().Be(OverlayState.Hidden);
    }

    [Fact]
    public void OverlayCoordinator_state_updates_are_visible_to_subscribers_of_IOverlayState()
    {
        using var h = NewHarness();
        var seen = new List<OverlayState>();
        h.Get<IOverlayState>().Changed += seen.Add;
        var actions = Actions(h);
        actions.Execute(ShellAction.Launchpad);
        actions.Execute(ShellAction.Launchpad);
        seen.Select(s => s.Visible).Should().Equal(true, false);
    }
}
