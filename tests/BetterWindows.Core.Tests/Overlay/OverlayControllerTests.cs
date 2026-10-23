using BetterWindows.Core.Controllers;
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

public class OverlayControllerTests
{
    private static CoreHarness NewHarness(Action<IServiceCollection>? more = null) => Kit.Harness<OverlayController>(more);

    private static OverlayRequest Pop(PopoverKind kind, string? item = null, string? monitor = null) => new()
    {
        Mode = OverlayMode.Popover, MonitorId = monitor,
        Popover = new PopoverRequest { Kind = kind, ItemId = item, Anchor = new Rect { X = 100, Y = 4, Width = 30, Height = 20 } },
    };

    private static readonly SurfaceId Dock = new(SurfaceKind.Dock);

    // ---- overlay.open ----

    [Fact]
    public async Task Open_forwards_the_request_with_the_calling_surface_as_source()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        var request = Pop(PopoverKind.ControlCenter);
        Kit.IsOk(await h.CallRawAsync(menubar, Msg.OverlayOpen, request)).Should().BeTrue();

        var shown = h.Get<FakeSurfaceManager>().Shown;
        shown.Should().ContainSingle();
        shown[0].Source.Should().Be(new SurfaceId(SurfaceKind.Menubar, "m1"));
        shown[0].Request.Mode.Should().Be(OverlayMode.Popover);
        shown[0].Request.Popover!.Kind.Should().Be(PopoverKind.ControlCenter);
        shown[0].Request.Popover!.Anchor.Should().BeEquivalentTo(request.Popover!.Anchor);
        shown[0].Request.MonitorId.Should().BeNull("the Host resolves the monitor of the source surface");
    }

    [Theory]
    [InlineData(SurfaceKind.Dock)]
    [InlineData(SurfaceKind.Menubar)]
    [InlineData(SurfaceKind.Desktop)]
    [InlineData(SurfaceKind.Overlay)]
    public async Task Open_is_allowed_from_dock_menubar_desktop_and_overlay(SurfaceKind kind)
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var s = h.AddSurface(kind);
        Kit.IsOk(await h.CallRawAsync(s, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Launchpad })).Should().BeTrue();
        h.Get<FakeSurfaceManager>().Shown.Single().Source.Should().Be(s.Id);
    }

    [Fact]
    public async Task Open_is_forbidden_from_the_settings_window()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var s = h.AddSurface(SurfaceKind.Settings);
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight })).Should().Be("forbidden");
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_with_an_explicit_monitor_passes_it_on()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var s = h.AddSurface(SurfaceKind.Dock);
        await h.CallRawAsync(s, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.MissionControl, MonitorId = "m1" });
        h.Get<FakeSurfaceManager>().Shown.Single().Request.MonitorId.Should().Be("m1");
        h.Get<OverlayStateService>().Current.MonitorId.Should().Be("m1");
    }

    [Fact]
    public async Task Open_validates_everything_and_never_reaches_the_host_on_bad_input()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var bad = new object[]
        {
            new { mode = "popover" },                                                                                       // popover required
            new { mode = "spotlight", popover = new { kind = "apple", anchor = new { x = 0, y = 0, width = 1, height = 1 } } }, // popover not allowed
            new { mode = "popover", popover = new { kind = "apple" } },                                                    // anchor missing
            new { mode = "popover", popover = new { kind = "teapot", anchor = new { x = 0, y = 0, width = 1, height = 1 } } },
            new { mode = "popover", popover = new { kind = "apple", anchor = new { x = 1e12, y = 0, width = 1, height = 1 } } },
            new { mode = "popover", popover = new { kind = "apple", anchor = new { x = 0, y = 0, width = -5, height = 1 } } },
            new { mode = "popover", popover = new { kind = "apple", anchor = new { x = 0, y = 0, width = "wide", height = 1 } } },
            new { mode = "popover", monitorId = "m9", popover = new { kind = "apple", anchor = new { x = 0, y = 0, width = 1, height = 1 } } },
            new { mode = "banana" },
            new { },
        };
        foreach (var payload in bad) Kit.ErrorCode(await h.CallRawAsync(s, Msg.OverlayOpen, payload)).Should().Be("badRequest", System.Text.Json.JsonSerializer.Serialize(payload));
        h.Get<FakeSurfaceManager>().Shown.Should().BeEmpty();
        h.Get<OverlayStateService>().Current.Should().Be(OverlayState.Hidden);
    }

    [Fact]
    public async Task A_host_that_throws_on_show_becomes_failed()
    {
        using var h = NewHarness(sc => sc.AddSingleton<ISurfaceManager, ThrowingSurfaces>());
        h.StartAll<OverlayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        var r = await h.CallRawAsync(s, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        Kit.ErrorCode(r).Should().Be("failed");
        ((string?)Kit.ErrorOf(r)!["message"]).Should().NotContain("SECRET");
    }

    private sealed class ThrowingSurfaces : ISurfaceManager
    {
        public bool IsOverlayVisible => false;
        public OverlayMode? CurrentOverlayMode => null;
        public int OverlayToken => 0;
        public bool SupportsGlassOverflow => false;
        public event Action? OverlayHidden { add { } remove { } }
        public void ResizeSurface(SurfaceId surface, double widthDip, double heightDip, double? barThicknessDip) { }
        public void ShowOverlay(OverlayRequest request, SurfaceId? source) => throw new InvalidOperationException("SECRET");
        public void HideOverlay() => throw new InvalidOperationException("SECRET");
        public void SetDockRevealed(SurfaceId dock, bool revealed) { }
        public void SetSurfaceShape(SurfaceId surface, SurfaceShapeParams shape) { }
        public void ShowSettingsWindow(string? page) { }
        public void SetSurfacesVisible(SurfaceKind? kind, string? monitorId, bool visible, string reason) { }
    }

    // ---- overlay state ----

    [Fact]
    public async Task Every_show_updates_the_overlay_state()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var seen = new List<OverlayState>();
        h.Get<IOverlayState>().Changed += seen.Add;
        var s = h.AddSurface(SurfaceKind.Dock);

        await h.CallRawAsync(s, Msg.OverlayOpen, Pop(PopoverKind.DockMenu, item: "exe:c:\\a.exe", monitor: "m1"));

        var state = h.Get<IOverlayState>().Current;
        state.Should().Be(new OverlayState(true, OverlayMode.Popover, "m1", 1, PopoverKind.DockMenu, "exe:c:\\a.exe"));
        seen.Should().Contain(state);
    }

    [Fact]
    public async Task The_state_carries_the_token_of_the_show()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var s = h.AddSurface(SurfaceKind.Menubar, "m1");
        await h.CallRawAsync(s, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        h.Get<IOverlayState>().Current.Token.Should().Be(1);
        await h.CallRawAsync(s, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Launchpad });
        h.Get<IOverlayState>().Current.Should().Match<OverlayState>(x => x.Token == 2 && x.Mode == OverlayMode.Launchpad && x.Popover == null);
    }

    [Fact]
    public async Task The_monitor_of_the_state_falls_back_to_the_source_surface_then_the_cursor()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        h.Get<IOverlayState>().Current.MonitorId.Should().Be("m1");

        var dock = h.AddSurface(SurfaceKind.Dock);
        h.Get<FakeCursorProvider>().Position = new NativePoint(10, 10);
        await h.CallRawAsync(dock, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Launchpad });
        h.Get<IOverlayState>().Current.MonitorId.Should().Be("m1", "the dock has no monitor id: the cursor's monitor is used");
    }

    // ---- overlay.close ----

    [Fact]
    public async Task Close_with_the_current_token_hides_the_overlay_and_the_state()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        await h.CallRawAsync(menubar, Msg.OverlayOpen, Pop(PopoverKind.Apple));
        var token = surfaces.OverlayToken;

        Kit.IsOk(await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = token })).Should().BeTrue();

        surfaces.HideCount.Should().Be(1);
        surfaces.IsOverlayVisible.Should().BeFalse();
        h.Get<IOverlayState>().Current.Should().Be(OverlayState.Hidden);
    }

    [Fact]
    public async Task Close_with_a_stale_token_is_ignored()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        await h.CallRawAsync(menubar, Msg.OverlayOpen, Pop(PopoverKind.Apple));                   // token 1
        await h.CallRawAsync(menubar, Msg.OverlayOpen, Pop(PopoverKind.Battery));                 // token 2: the user re-opened

        Kit.IsOk(await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = 1 })).Should().BeTrue("stale closes are acknowledged but ignored");
        surfaces.HideCount.Should().Be(0);
        surfaces.IsOverlayVisible.Should().BeTrue();
        h.Get<IOverlayState>().Current.Should().Match<OverlayState>(x => x.Visible && x.Popover == PopoverKind.Battery);

        await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = 99 });
        surfaces.HideCount.Should().Be(0);
        await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = 2 });
        surfaces.HideCount.Should().Be(1);
    }

    [Theory]
    [InlineData(SurfaceKind.Dock)]
    [InlineData(SurfaceKind.Menubar)]
    [InlineData(SurfaceKind.Desktop)]
    [InlineData(SurfaceKind.Settings)]
    public async Task Close_is_forbidden_from_every_surface_but_the_overlay(SurfaceKind kind)
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var s = h.AddSurface(kind);
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m9"), Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        Kit.ErrorCode(await h.CallRawAsync(s, Msg.OverlayClose, new OverlayCloseParams { Token = h.Get<FakeSurfaceManager>().OverlayToken })).Should().Be("forbidden");
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0);
    }

    [Fact]
    public async Task Close_without_a_token_is_a_badRequest()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        Kit.ErrorCode(await h.CallRawAsync(overlay, Msg.OverlayClose, new { })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Close_after_the_host_already_hid_the_overlay_only_syncs_the_state()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        var token = surfaces.OverlayToken;
        surfaces.SimulateHostHide();
        Kit.IsOk(await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = token })).Should().BeTrue();
        surfaces.HideCount.Should().Be(0, "nothing is visible: no HideOverlay call");
        h.Get<IOverlayState>().Current.Should().Be(OverlayState.Hidden);
    }

    // ---- host-initiated hides ----

    [Fact]
    public async Task OverlayHidden_from_the_host_clears_the_state()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        h.Get<IOverlayState>().Current.Visible.Should().BeTrue();
        h.Get<FakeSurfaceManager>().SimulateHostHide();
        h.Get<IOverlayState>().Current.Should().Be(OverlayState.Hidden);
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0, "the host hid it: Core does nothing else");
    }

    [Fact]
    public async Task The_controller_never_publishes_overlay_show_or_hide_itself()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = h.Get<FakeSurfaceManager>().OverlayToken });
        h.Get<FakeSurfaceManager>().SimulateHostHide();
        foreach (var s in new[] { menubar, overlay })
        {
            s.Events(Msg.OverlayShow).Should().BeEmpty();
            s.Events(Msg.OverlayHide).Should().BeEmpty();
        }
    }

    // ---- dock reveal ----

    [Theory]
    [InlineData(PopoverKind.DockMenu)]
    [InlineData(PopoverKind.WindowList)]
    public async Task A_dock_popover_holds_the_dock_revealed_and_releases_it_on_close(PopoverKind kind)
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        var dock = h.AddSurface(SurfaceKind.Dock);
        var overlay = h.AddSurface(SurfaceKind.Overlay);

        await h.CallRawAsync(dock, Msg.OverlayOpen, Pop(kind, item: "exe:c:\\a.exe"));
        surfaces.DockReveals.Should().Equal((Dock, true));

        await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = surfaces.OverlayToken });
        surfaces.DockReveals.Should().Equal((Dock, true), (Dock, false));
    }

    [Fact]
    public async Task The_dock_is_released_when_the_host_hides_the_overlay()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Dock), Msg.OverlayOpen, Pop(PopoverKind.DockMenu, item: "x"));
        surfaces.SimulateHostHide();
        surfaces.DockReveals.Should().Equal((Dock, true), (Dock, false));
    }

    [Fact]
    public async Task The_dock_is_released_when_another_mode_replaces_the_dock_menu()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        var dock = h.AddSurface(SurfaceKind.Dock);
        await h.CallRawAsync(dock, Msg.OverlayOpen, Pop(PopoverKind.DockMenu, item: "x"));
        await h.CallRawAsync(dock, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.MissionControl });
        surfaces.DockReveals.Should().Equal((Dock, true), (Dock, false));
    }

    [Fact]
    public async Task Switching_between_two_dock_popovers_keeps_the_hold_without_flapping()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        var dock = h.AddSurface(SurfaceKind.Dock);
        await h.CallRawAsync(dock, Msg.OverlayOpen, Pop(PopoverKind.DockMenu, item: "a"));
        await h.CallRawAsync(dock, Msg.OverlayOpen, Pop(PopoverKind.WindowList, item: "a"));
        surfaces.DockReveals.Should().Equal((Dock, true));
    }

    [Fact]
    public async Task Menubar_popovers_do_not_touch_the_dock()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m1"), Msg.OverlayOpen, Pop(PopoverKind.ControlCenter));
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m2"), Msg.OverlayOpen, Pop(PopoverKind.AppMenu));
        h.Get<FakeSurfaceManager>().DockReveals.Should().BeEmpty();
    }

    [Fact]
    public async Task Stop_releases_a_held_dock()
    {
        using var h = NewHarness();
        var c = h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Dock), Msg.OverlayOpen, Pop(PopoverKind.DockMenu, item: "x"));
        c.Stop();
        surfaces.DockReveals.Should().Equal((Dock, true), (Dock, false));
    }

    // ---- foreground changes ----

    [Fact]
    public async Task Another_app_taking_the_foreground_hides_the_overlay_after_the_debounce()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m1"), Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });

        tracker.SetForeground(20);
        h.Time.Advance(TimeSpan.FromMilliseconds(149));
        surfaces.HideCount.Should().Be(0);
        h.Time.Advance(TimeSpan.FromMilliseconds(1));
        surfaces.HideCount.Should().Be(1);
        h.Get<IOverlayState>().Current.Should().Be(OverlayState.Hidden);
    }

    [Fact]
    public async Task A_foreground_that_flips_back_within_the_debounce_does_not_close_the_overlay()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        h.StartAll<OverlayController>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m1"), Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });

        tracker.SetForeground(20);
        h.Time.Advance(TimeSpan.FromMilliseconds(60));
        tracker.SetForeground(10);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0);
        h.Get<IOverlayState>().Current.Visible.Should().BeTrue();
    }

    [Fact]
    public async Task A_null_foreground_is_ignored()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        h.StartAll<OverlayController>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m1"), Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        tracker.ClearForeground();
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0);
    }

    [Fact]
    public async Task The_same_window_reported_again_is_not_a_different_app()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        h.StartAll<OverlayController>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m1"), Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        tracker.SetForeground(10);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0);
    }

    [Fact]
    public void Foreground_changes_while_the_overlay_is_hidden_do_nothing()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        h.StartAll<OverlayController>();
        tracker.SetForeground(20);
        tracker.SetForeground(30);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0);
    }

    [Fact]
    public async Task The_baseline_is_the_foreground_at_the_time_of_the_show()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        h.StartAll<OverlayController>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        tracker.SetForeground(20);                                                                         // before the overlay opens
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        tracker.SetForeground(20);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0, "window 20 was already the foreground when the overlay opened");
        tracker.SetForeground(10);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(1);
    }

    [Fact]
    public async Task A_new_show_resets_the_pending_foreground_hide()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        h.StartAll<OverlayController>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        tracker.SetForeground(20);
        h.Time.Advance(TimeSpan.FromMilliseconds(100));
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Launchpad });   // re-shown: baseline is now window 20
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<FakeSurfaceManager>().HideCount.Should().Be(0);
    }

    [Fact]
    public async Task Closing_the_overlay_cancels_a_pending_foreground_hide()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        var overlay = h.AddSurface(SurfaceKind.Overlay);
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        tracker.SetForeground(20);
        await h.CallRawAsync(overlay, Msg.OverlayClose, new OverlayCloseParams { Token = surfaces.OverlayToken });
        h.Time.Advance(TimeSpan.FromSeconds(1));
        surfaces.HideCount.Should().Be(1, "only the close itself");
    }

    // ---- token resync with a host that shows asynchronously ----

    [Fact]
    public async Task The_token_is_resynchronised_when_the_host_shows_the_overlay_asynchronously()
    {
        using var h = NewHarness(sc => sc.AddSingleton<ISurfaceManager, LaggingSurfaceManager>());
        h.StartAll<OverlayController>();
        var host = (LaggingSurfaceManager)h.Get<ISurfaceManager>();
        var menubar = h.AddSurface(SurfaceKind.Menubar, "m1");
        await h.CallRawAsync(menubar, Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        h.Get<IOverlayState>().Current.Should().Match<OverlayState>(x => x.Visible && x.Token == 0, "the host has not shown it yet");

        host.Commit();                                                                                     // the UI thread got to it
        h.Time.Advance(OverlayCoordinator.TokenResyncDelay);
        h.Get<IOverlayState>().Current.Token.Should().Be(1);
    }

    [Fact]
    public async Task The_resync_never_resurrects_or_hides_the_overlay_on_a_lagging_getter()
    {
        using var h = NewHarness(sc => sc.AddSingleton<ISurfaceManager, LaggingSurfaceManager>());
        h.StartAll<OverlayController>();
        var host = (LaggingSurfaceManager)h.Get<ISurfaceManager>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m1"), Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        h.Time.Advance(TimeSpan.FromSeconds(1));                                                           // host is still not visible; state stays as requested
        h.Get<IOverlayState>().Current.Visible.Should().BeTrue();

        host.RaiseHostHidden();
        h.Get<IOverlayState>().Current.Should().Be(OverlayState.Hidden);
        host.Commit();                                                                                     // a late commit of a request that was superseded changes nothing in the state
        h.Time.Advance(TimeSpan.FromSeconds(1));
        h.Get<IOverlayState>().Current.Should().Be(OverlayState.Hidden);
    }

    // ---- lifecycle ----

    [Fact]
    public void Handlers_are_registered_for_open_and_close()
    {
        using var h = NewHarness();
        h.StartAll<OverlayController>();
        h.Router.RegisteredTypes.Should().Contain(new[] { Msg.OverlayOpen, Msg.OverlayClose });
    }

    [Fact]
    public async Task Stop_detaches_from_the_host_and_the_tracker()
    {
        using var h = NewHarness();
        var tracker = h.Get<FakeWindowTracker>();
        tracker.SetForeground(10);
        var c = h.StartAll<OverlayController>();
        var surfaces = h.Get<FakeSurfaceManager>();
        await h.CallRawAsync(h.AddSurface(SurfaceKind.Menubar, "m1"), Msg.OverlayOpen, new OverlayRequest { Mode = OverlayMode.Spotlight });
        c.Stop();
        tracker.SetForeground(20);
        h.Time.Advance(TimeSpan.FromSeconds(1));
        surfaces.HideCount.Should().Be(0);
        surfaces.SimulateHostHide();
        h.Get<IOverlayState>().Current.Visible.Should().BeTrue("a stopped controller no longer follows the host");
    }

    [Fact]
    public void A_controller_started_while_the_host_already_shows_the_overlay_adopts_that_state()
    {
        using var h = NewHarness();
        var surfaces = h.Get<FakeSurfaceManager>();
        surfaces.ShowOverlay(new OverlayRequest { Mode = OverlayMode.Launchpad }, null);
        h.StartAll<OverlayController>();
        h.Get<IOverlayState>().Current.Should().Match<OverlayState>(x => x.Visible && x.Mode == OverlayMode.Launchpad && x.Token == 1);
    }
}
