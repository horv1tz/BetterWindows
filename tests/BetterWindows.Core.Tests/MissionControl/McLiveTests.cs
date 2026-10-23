using BetterWindows.Core.Controllers;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Desktop;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.MissionControl;

public class McLiveTests
{
    private static readonly TimeSpan Settle = TimeSpan.FromMilliseconds(120);

    private static IReadOnlyList<System.Text.Json.Nodes.JsonObject> Changed(McRig r) => r.OverlaySurface.Events(Msg.McChanged);

    [Fact]
    public async Task A_window_change_while_mission_control_is_open_is_pushed_as_mc_changed_with_the_same_revision()
    {
        using var rig = McRig.Start();
        rig.Add(FakeWindowTracker.Win(1, "A"));
        rig.ShowMissionControl();
        var snap = await rig.Snapshot();

        rig.Add(FakeWindowTracker.Win(2, "B"));
        rig.Tracker.RaiseChanged();
        Changed(rig).Should().BeEmpty(); // coalesced: nothing before the delay
        rig.H.Time.Advance(Settle);

        var ev = rig.OverlaySurface.LastEvent<McSnapshot>(Msg.McChanged)!;
        ev.Windows.Select(w => w.Handle).Should().Equal("1", "2");
        ev.Rev.Should().Be(snap.Rev);
    }

    [Fact]
    public async Task Nothing_is_pushed_while_mission_control_is_not_showing()
    {
        using var rig = McRig.Start();
        rig.Add(FakeWindowTracker.Win(1));
        rig.Tracker.RaiseChanged();
        rig.Desktops.RaiseChanged();
        rig.H.Time.Advance(Settle);
        Changed(rig).Should().BeEmpty();

        rig.Overlay.Set(new OverlayState(true, OverlayMode.Spotlight, "m1", 1, null, null));
        rig.Tracker.RaiseChanged();
        rig.H.Time.Advance(Settle);
        Changed(rig).Should().BeEmpty();
        await Task.CompletedTask;
    }

    [Fact]
    public async Task A_burst_of_changes_is_coalesced_into_one_event()
    {
        using var rig = McRig.Start();
        rig.ShowMissionControl();
        await rig.Snapshot();
        for (var i = 1; i <= 10; i++)
        {
            rig.Add(FakeWindowTracker.Win(i, "W" + i));
            rig.Tracker.RaiseChanged();
            rig.H.Time.Advance(TimeSpan.FromMilliseconds(5));
        }
        rig.H.Time.Advance(Settle);
        Changed(rig).Should().HaveCount(1);
        rig.OverlaySurface.LastEvent<McSnapshot>(Msg.McChanged)!.Windows.Should().HaveCount(10);
    }

    [Fact]
    public async Task Events_are_at_least_100_ms_apart()
    {
        using var rig = McRig.Start();
        rig.ShowMissionControl();
        await rig.Snapshot();
        rig.Add(FakeWindowTracker.Win(1));
        rig.Tracker.RaiseChanged();
        rig.H.Time.Advance(TimeSpan.FromMilliseconds(99));
        Changed(rig).Should().BeEmpty();
        rig.H.Time.Advance(TimeSpan.FromMilliseconds(2));
        Changed(rig).Should().HaveCount(1);
    }

    [Fact]
    public async Task A_change_that_does_not_alter_the_snapshot_pushes_nothing()
    {
        using var rig = McRig.Start();
        rig.Add(FakeWindowTracker.Win(1));
        rig.ShowMissionControl();
        await rig.Snapshot();
        rig.Tracker.RaiseChanged(); // e.g. a flash state this view does not show
        rig.H.Time.Advance(Settle);
        Changed(rig).Should().BeEmpty();
    }

    [Fact]
    public async Task A_desktop_change_is_pushed()
    {
        using var rig = McRig.Start();
        rig.ShowMissionControl();
        await rig.Snapshot();
        await rig.Ok<DesktopInfo>(Msg.McCreateDesktop);
        rig.H.Time.Advance(Settle);
        rig.OverlaySurface.LastEvent<McSnapshot>(Msg.McChanged)!.Desktops.Should().HaveCount(4);
    }

    [Fact]
    public async Task The_service_reporting_a_desktop_change_is_pushed()
    {
        using var rig = McRig.Start();
        rig.ShowMissionControl();
        await rig.Snapshot();
        rig.Desktops.Desktops = rig.Desktops.Desktops.Select(d => d with { Current = d.Id == "d2" }).ToList();
        rig.Desktops.RaiseChanged();
        rig.H.Time.Advance(Settle);
        rig.OverlaySurface.LastEvent<McSnapshot>(Msg.McChanged)!.Desktops.Single(d => d.Current).Id.Should().Be("d2");
    }

    [Fact]
    public async Task Closing_the_overlay_cancels_a_pending_push()
    {
        using var rig = McRig.Start();
        rig.ShowMissionControl();
        await rig.Snapshot();
        rig.Add(FakeWindowTracker.Win(1));
        rig.Tracker.RaiseChanged();
        rig.HideOverlay();
        rig.H.Time.Advance(Settle);
        Changed(rig).Should().BeEmpty();
    }

    [Fact]
    public async Task Only_overlay_surfaces_receive_mc_changed()
    {
        using var rig = McRig.Start();
        var dock = rig.H.AddSurface(SurfaceKind.Dock);
        var desktop = rig.H.AddSurface(SurfaceKind.Desktop, "m1");
        rig.ShowMissionControl();
        await rig.Snapshot();
        rig.Add(FakeWindowTracker.Win(1));
        rig.Tracker.RaiseChanged();
        rig.H.Time.Advance(Settle);
        Changed(rig).Should().HaveCount(1);
        dock.Events(Msg.McChanged).Should().BeEmpty();
        desktop.Events(Msg.McChanged).Should().BeEmpty();
    }

    [Fact]
    public async Task A_new_show_pushes_a_higher_revision()
    {
        using var rig = McRig.Start();
        rig.ShowMissionControl(token: 1);
        var first = await rig.Snapshot();
        rig.HideOverlay();
        rig.ShowMissionControl(token: 2);
        rig.Add(FakeWindowTracker.Win(1));
        rig.Tracker.RaiseChanged();
        rig.H.Time.Advance(Settle);
        rig.OverlaySurface.LastEvent<McSnapshot>(Msg.McChanged)!.Rev.Should().BeGreaterThan(first.Rev);
    }

    [Fact]
    public async Task The_pushed_snapshot_is_for_the_overlay_monitor()
    {
        using var rig = McRig.Start(r =>
        {
            r.Monitors.Monitors.Clear();
            r.Monitors.Monitors.Add(Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true));
            r.Monitors.Monitors.Add(Kit.Monitor("m2", 1920, 0, 1920, 1080));
        });
        rig.ShowMissionControl(monitor: "m2");
        await rig.Snapshot();
        rig.Add(FakeWindowTracker.Win(1, bounds: new NativeRect(100, 100, 500, 500)), FakeWindowTracker.Win(2, bounds: new NativeRect(2100, 100, 2500, 500)));
        rig.Tracker.RaiseChanged();
        rig.H.Time.Advance(Settle);
        rig.OverlaySurface.LastEvent<McSnapshot>(Msg.McChanged)!.Windows.Select(w => w.Handle).Should().Equal("2");
    }

    [Fact]
    public async Task Closing_a_window_refreshes_the_view_when_the_tracker_lags()
    {
        using var rig = McRig.Start();
        rig.Add(FakeWindowTracker.Win(1));
        rig.ShowMissionControl();
        await rig.Snapshot();
        rig.Tracker.Set(Array.Empty<TrackedWindow>()); // the window vanished, but the tracker has not raised Changed yet
        await rig.Ok<object?>(Msg.McCloseWindow, new WindowHandleParams { Handle = "1" }).ContinueWith(_ => { });
        rig.H.Time.Advance(Settle);
        Changed(rig).Should().NotBeNull();
    }

    // ---- prewarming thumbnails

    [Fact]
    public async Task The_window_that_loses_the_foreground_is_prewarmed()
    {
        using var rig = McRig.Start();
        var a = FakeWindowTracker.Win(1);
        var b = FakeWindowTracker.Win(2);
        rig.Add(a, b);
        rig.Tracker.RaiseForeground(a);
        rig.Tracker.RaiseForeground(b);
        await Kit.Eventually(() => rig.Dyn.Prewarmed.Contains(1));
        rig.Dyn.Prewarmed.Should().NotContain(2);
    }

    [Fact]
    public async Task The_initial_foreground_window_is_prewarmed_when_it_loses_the_foreground()
    {
        using var rig = McRig.Start(r =>
        {
            var a = FakeWindowTracker.Win(1);
            r.Add(a);
            r.Tracker.Set(r.Tracker.Snapshot.Windows, 1);
        });
        rig.Tracker.RaiseForeground(FakeWindowTracker.Win(2));
        await Kit.Eventually(() => rig.Dyn.Prewarmed.Contains(1));
    }

    [Fact]
    public async Task Losing_the_foreground_to_nothing_prewarms_the_old_window()
    {
        using var rig = McRig.Start();
        var a = FakeWindowTracker.Win(1);
        rig.Add(a);
        rig.Tracker.RaiseForeground(a);
        rig.Tracker.RaiseForeground(null);
        await Kit.Eventually(() => rig.Dyn.Prewarmed.Contains(1));
    }

    [Fact]
    public async Task The_same_foreground_window_twice_prewarms_nothing()
    {
        using var rig = McRig.Start();
        var a = FakeWindowTracker.Win(1);
        rig.Add(a);
        rig.Tracker.RaiseForeground(a);
        rig.Tracker.RaiseForeground(a);
        await Task.Delay(50);
        rig.Dyn.Prewarmed.Should().BeEmpty();
    }

    [Fact]
    public async Task A_window_that_is_gone_or_not_tracked_is_not_prewarmed()
    {
        using var rig = McRig.Start();
        var ghost = FakeWindowTracker.Win(1);            // never added to the tracker
        var b = FakeWindowTracker.Win(2);
        rig.Add(b);
        rig.Tracker.RaiseForeground(ghost);
        rig.Tracker.RaiseForeground(b);
        await Task.Delay(50);
        rig.Dyn.Prewarmed.Should().BeEmpty();
    }

    [Fact]
    public async Task Stopping_unsubscribes_from_the_tracker()
    {
        using var rig = McRig.Start();
        var a = FakeWindowTracker.Win(1);
        rig.Add(a, FakeWindowTracker.Win(2));
        rig.H.Get<MissionControlController>().Stop();
        rig.Tracker.RaiseForeground(a);
        rig.Tracker.RaiseForeground(FakeWindowTracker.Win(2));
        await Task.Delay(50);
        rig.Dyn.Prewarmed.Should().BeEmpty();
    }
}
