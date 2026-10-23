using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Desktop;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.MissionControl;

public class McSnapshotTests
{
    private static FakeWindowTrackerWin Win(long h, string title = "Window", NativeRect? bounds = null, bool minimized = false, bool shell = false) => new(h, title, bounds, minimized, shell);

    // Tiny wrapper so tests read well: builds a TrackedWindow.
    private sealed record FakeWindowTrackerWin(long Handle, string Title, NativeRect? Bounds, bool Minimized, bool Shell)
    {
        public static implicit operator TrackedWindow(FakeWindowTrackerWin w) =>
            FakeWindowTracker.Win(w.Handle, w.Title, bounds: w.Bounds, minimized: w.Minimized, cloakedByShell: w.Shell);
    }

    private static void Add(McRig r, params FakeWindowTrackerWin[] ws) => r.Add(ws.Select(w => (TrackedWindow)w).ToArray());

    [Fact]
    public async Task The_snapshot_lists_every_tracked_window_with_identity_thumbnail_and_dip_bounds()
    {
        using var rig = McRig.Start();
        rig.Add(FakeWindowTracker.Win(11, "Inbox - Mail", bounds: new NativeRect(100, 200, 1000, 800), appId: "aumid:Mail", appName: "Mail"),
                FakeWindowTracker.Win(12, "Notes", appId: "exe:c:\\notes.exe", appName: "Notes", minimized: false));
        rig.Tracker.Set(rig.Tracker.Snapshot.Windows, foreground: 12);

        var snap = await rig.Snapshot();
        snap.Windows.Select(w => w.Handle).Should().Equal("11", "12");
        var mail = snap.Windows[0];
        mail.Should().Match<McWindow>(w => w.Title == "Inbox - Mail" && w.AppId == "aumid:Mail" && w.AppName == "Mail" && !w.Minimized && !w.Active && w.MonitorId == "m1");
        mail.IconUrl.Should().Be(DynUrls.AppIcon("aumid:Mail", 128));
        mail.ThumbUrl.Should().Be(DynUrls.WindowThumb(11, 480, snap.Rev));
        mail.Bounds.Should().Be(new Rect { X = 100, Y = 200, Width = 900, Height = 600 });
        snap.Windows[1].Active.Should().BeTrue();
    }

    [Fact]
    public async Task Windows_on_other_virtual_desktops_are_included_with_their_desktop_id()
    {
        using var rig = McRig.Start();
        Add(rig, Win(1), Win(2, shell: true));
        rig.Desktops.WindowDesktop[1] = "d1";
        rig.Desktops.WindowDesktop[2] = "d2";
        var snap = await rig.Snapshot();
        snap.Windows.Select(w => (w.Handle, w.DesktopId)).Should().Equal(("1", "d1"), ("2", "d2"));
        snap.DesktopsSupported.Should().BeTrue();
        snap.Desktops.Select(d => d.Id).Should().Equal("d1", "d2", "d3");
        snap.Desktops.Single(d => d.Current).Id.Should().Be("d1");
    }

    [Fact]
    public async Task A_window_on_no_particular_desktop_has_no_desktop_id()
    {
        using var rig = McRig.Start();
        Add(rig, Win(1));
        (await rig.Snapshot()).Windows.Single().DesktopId.Should().BeNull();
    }

    [Fact]
    public async Task Without_virtual_desktop_support_only_the_current_desktop_exists()
    {
        using var rig = McRig.Start(r => { r.Desktops.IsSupported = false; r.Desktops.Desktops = new(); });
        Add(rig, Win(1), Win(2, shell: true));
        var snap = await rig.Snapshot();
        snap.DesktopsSupported.Should().BeFalse();
        snap.Desktops.Should().BeEmpty();
        snap.Windows.Select(w => w.Handle).Should().Equal("1"); // the other-desktop window cannot be reached, so it is not offered
    }

    [Fact]
    public async Task Windows_hidden_by_their_own_app_and_our_own_windows_are_not_listed()
    {
        using var rig = McRig.Start();
        var suspended = new TrackedWindow(FakeWindowTracker.Win(5).Raw with { IsCloaked = true, IsCloakedByShell = false, CloakedFlags = 1 }, "exe:x", "X");
        var own = new TrackedWindow(FakeWindowTracker.Win(6).Raw with { IsOwnProcess = true }, "exe:y", "Y");
        rig.Add(suspended, own, FakeWindowTracker.Win(7));
        (await rig.Snapshot()).Windows.Select(w => w.Handle).Should().Equal("7");
    }

    [Fact]
    public async Task A_window_without_a_known_app_uses_its_own_icon()
    {
        using var rig = McRig.Start();
        rig.Add(FakeWindowTracker.Win(9, appId: "unknown", appName: "Something"));
        (await rig.Snapshot()).Windows.Single().IconUrl.Should().Be(DynUrls.WindowIcon(9, 128));
    }

    [Fact]
    public async Task Very_long_titles_are_cut()
    {
        using var rig = McRig.Start();
        rig.Add(FakeWindowTracker.Win(9, title: new string('t', 5000)));
        (await rig.Snapshot()).Windows.Single().Title.Should().HaveLength(512);
    }

    [Fact]
    public async Task A_failing_desktop_query_degrades_to_no_desktop_id_instead_of_failing_the_call()
    {
        using var rig = McRig.Start(r => r.Desktops.ThrowOnWindowQuery = true);
        Add(rig, Win(1));
        (await rig.Snapshot()).Windows.Single().DesktopId.Should().BeNull();
    }

    // ---- monitors

    private static void TwoMonitors(McRig r)
    {
        r.Monitors.Monitors.Clear();
        r.Monitors.Monitors.Add(Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true));
        r.Monitors.Monitors.Add(Kit.Monitor("m2", 1920, 0, 2880, 1620, scale: 1.5));
    }

    [Fact]
    public async Task Bounds_are_dips_relative_to_the_window_monitor_using_its_scale()
    {
        using var rig = McRig.Start(TwoMonitors);
        Add(rig, Win(1, bounds: new NativeRect(1920 + 300, 150, 1920 + 300 + 900, 150 + 600)));
        var w = (await rig.Snapshot("m2")).Windows.Single();
        w.MonitorId.Should().Be("m2");
        w.Bounds.Should().Be(new Rect { X = 200, Y = 100, Width = 600, Height = 400 });
    }

    [Fact]
    public async Task A_monitor_snapshot_contains_only_windows_whose_centre_is_on_that_monitor()
    {
        using var rig = McRig.Start(TwoMonitors);
        Add(rig, Win(1, bounds: new NativeRect(100, 100, 500, 500)),
                 Win(2, bounds: new NativeRect(2200, 100, 2900, 600)),
                 Win(3, bounds: new NativeRect(1500, 100, 2300, 600))); // 800 wide, centre at x=1900 -> still m1
        (await rig.Snapshot("m1")).Windows.Select(w => w.Handle).Should().Equal("1", "3");
        (await rig.Snapshot("m2")).Windows.Select(w => w.Handle).Should().Equal("2");
    }

    [Fact]
    public async Task A_window_straddling_two_monitors_belongs_to_the_one_with_its_centre()
    {
        using var rig = McRig.Start(TwoMonitors);
        Add(rig, Win(1, bounds: new NativeRect(1800, 100, 2500, 500))); // centre x = 2150 -> m2
        (await rig.Snapshot("m1")).Windows.Should().BeEmpty();
        (await rig.Snapshot("m2")).Windows.Should().ContainSingle();
    }

    [Fact]
    public async Task Without_a_monitor_id_the_overlay_monitor_is_used()
    {
        using var rig = McRig.Start(TwoMonitors);
        Add(rig, Win(1, bounds: new NativeRect(100, 100, 500, 500)), Win(2, bounds: new NativeRect(2200, 100, 2900, 600)));
        rig.ShowMissionControl(monitor: "m2");
        (await rig.Snapshot()).Windows.Select(w => w.Handle).Should().Equal("2");
    }

    [Fact]
    public async Task Without_a_monitor_id_or_overlay_the_cursor_monitor_is_used_then_the_primary()
    {
        using var rig = McRig.Start(TwoMonitors);
        Add(rig, Win(1, bounds: new NativeRect(100, 100, 500, 500)), Win(2, bounds: new NativeRect(2200, 100, 2900, 600)));
        rig.Cursor.Position = new NativePoint(3000, 400);
        (await rig.Snapshot()).Windows.Select(w => w.Handle).Should().Equal("2");
        rig.Cursor.Unavailable = true;
        (await rig.Snapshot()).Windows.Select(w => w.Handle).Should().Equal("1");
    }

    [Fact]
    public async Task An_unknown_monitor_id_is_a_bad_request()
    {
        using var rig = McRig.Start();
        Kit.Code(await rig.Call(Msg.McSnapshot, new McSnapshotParams { MonitorId = "m99" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task A_minimised_window_with_parked_bounds_gets_a_centred_placeholder_on_the_requested_monitor()
    {
        using var rig = McRig.Start(TwoMonitors);
        Add(rig, Win(1, bounds: new NativeRect(-32000, -32000, -31840, -31972), minimized: true));
        foreach (var monitor in new[] { "m1", "m2" })
        {
            var w = (await rig.Snapshot(monitor)).Windows.Single();
            w.Minimized.Should().BeTrue();
            w.MonitorId.Should().Be(monitor);
            w.Bounds.Width.Should().BeGreaterThan(100);
            w.Bounds.X.Should().BeGreaterThan(0);
        }
    }

    [Fact]
    public async Task A_window_outside_every_monitor_is_assigned_to_the_nearest_one()
    {
        using var rig = McRig.Start(TwoMonitors);
        Add(rig, Win(1, bounds: new NativeRect(6000, 100, 6400, 400)));
        (await rig.Snapshot("m2")).Windows.Should().ContainSingle();
        (await rig.Snapshot("m1")).Windows.Should().BeEmpty();
    }

    // ---- revisions

    [Fact]
    public async Task The_revision_is_stable_within_one_show_and_grows_with_every_new_show()
    {
        using var rig = McRig.Start();
        Add(rig, Win(1));
        rig.ShowMissionControl(token: 1);
        var a = await rig.Snapshot();
        var b = await rig.Snapshot();
        b.Rev.Should().Be(a.Rev);
        b.Windows[0].ThumbUrl.Should().Be(a.Windows[0].ThumbUrl);

        rig.HideOverlay();
        rig.ShowMissionControl(token: 2);
        var c = await rig.Snapshot();
        c.Rev.Should().BeGreaterThan(a.Rev);
        c.Windows[0].ThumbUrl.Should().NotBe(a.Windows[0].ThumbUrl); // a re-capture on every opening
    }

    [Fact]
    public async Task Outside_mission_control_mode_every_snapshot_is_a_new_revision()
    {
        using var rig = McRig.Start();
        var a = await rig.Snapshot();
        var b = await rig.Snapshot();
        b.Rev.Should().BeGreaterThan(a.Rev);
    }

    [Fact]
    public async Task Only_the_overlay_may_ask_for_a_snapshot()
    {
        using var rig = McRig.Start();
        var dock = rig.H.AddSurface(SurfaceKind.Dock);
        Kit.Code(await rig.Call(Msg.McSnapshot, new McSnapshotParams(), dock)).Should().Be("forbidden");
    }

    [Fact]
    public async Task The_controller_registers_every_mc_call()
    {
        using var rig = McRig.Start();
        rig.H.Router.RegisteredTypes.Where(t => t.StartsWith("mc.")).Should().BeEquivalentTo(
            Msg.McSnapshot, Msg.McActivateWindow, Msg.McCloseWindow, Msg.McSwitchDesktop, Msg.McMoveWindow, Msg.McCreateDesktop, Msg.McRemoveDesktop);
    }
}
