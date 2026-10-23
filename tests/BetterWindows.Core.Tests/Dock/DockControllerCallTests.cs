using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.Dock;

public class DockControllerCallTests
{
    private const string AlphaAumid = "V.Alpha!App";
    private const string AlphaId = "aumid:V.Alpha!App";

    private static RawWindow W(long handle, string title = "Window", string exe = @"C:\Apps\alpha\alpha.exe", string? aumid = null, int pid = 100) => FakeWindowSystem.Win(handle, title, exe, aumid, pid);
    private static DockItemParams Item(string id) => new() { ItemId = id };
    private static WindowHandleParams Handle(long h) => new() { Handle = h.ToString() };

    /// <summary>Alpha is in the catalog and pinned; the tracker and controllers are started and a dock is connected.</summary>
    private static DockHarness Setup(Action<DockHarness>? arrange = null, bool pinAlpha = true)
    {
        var d = new DockHarness();
        d.Catalog.Add("Alpha", aumid: AlphaAumid);
        d.Settings.Set(s => s with
        {
            General = s.General with { FirstRunDone = true },
            Dock = s.Dock with { Pins = pinAlpha ? new[] { new PinnedItem { Id = AlphaId, Target = AlphaAumid, Name = "Alpha" } } : Array.Empty<PinnedItem>() },
        });
        arrange?.Invoke(d);
        d.Start();
        d.AddDock();
        return d;
    }

    // ---- dock.activate ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Activating_a_pinned_app_that_is_not_running_launches_it_bounces_and_shows_launching()
    {
        using var d = Setup();
        (await d.CallAsync(Msg.DockActivate, Item(AlphaId))).Should().BeEmpty();

        d.Launcher.Launched.Should().ContainSingle().Which.Target.Should().Be(LaunchTarget.FromAumid(AlphaAumid));
        d.Launcher.Launched[0].Args.Should().BeNull("the UI can never supply arguments");
        var bounce = ProtocolJson.Deserialize<DockBounce>(d.Dock.Events(Msg.DockBounce).Single()["payload"])!;
        bounce.ItemId.Should().Be(AlphaId);
        bounce.Kind.Should().Be(DockBounceKind.Launch);
        d.Settle();
        d.LastDockState()!.Items.Single().Launching.Should().BeTrue();
    }

    [Fact]
    public async Task Launching_ends_with_the_first_window_of_the_app()
    {
        using var d = Setup();
        await d.CallAsync(Msg.DockActivate, Item(AlphaId));
        d.Settle();
        d.Windows.Add(W(1, aumid: AlphaAumid));
        d.Windows.Raise(WindowEventKind.Created, 1);
        d.Settle();
        var item = d.LastDockState()!.Items.Single();
        item.Running.Should().BeTrue();
        item.Launching.Should().BeFalse();
        d.Time.Advance(TimeSpan.FromSeconds(30));
        d.Windows.Remove(1);
        d.Windows.Raise(WindowEventKind.Destroyed, 1);
        d.Settle();
        d.LastDockState()!.Items.Single().Launching.Should().BeFalse("the launch is over; closing the app must not bring the spinner back");
    }

    [Fact]
    public async Task Launching_expires_after_20_seconds_without_a_window()
    {
        using var d = Setup();
        await d.CallAsync(Msg.DockActivate, Item(AlphaId));
        d.Settle();
        d.LastDockState()!.Items.Single().Launching.Should().BeTrue();
        d.Time.Advance(TimeSpan.FromSeconds(19));
        d.LastDockState()!.Items.Single().Launching.Should().BeTrue();
        d.Time.Advance(TimeSpan.FromSeconds(2));
        d.LastDockState()!.Items.Single().Launching.Should().BeFalse();
    }

    [Fact]
    public async Task An_unpinned_catalog_app_being_launched_gets_a_transient_item_that_expires()
    {
        using var d = Setup(pinAlpha: false);
        (await d.CallAsync(Msg.DockActivate, Item(AlphaId))).Should().BeEmpty();
        d.Settle();
        var item = d.LastDockState()!.Items.Should().ContainSingle().Subject;
        (item.Launching, item.Pinned, item.Running).Should().Be((true, false, false));
        d.Time.Advance(TimeSpan.FromSeconds(21));
        d.LastDockState()!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task An_exe_pin_is_launched_by_path()
    {
        using var d = Setup(pinAlpha: false);
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = @"exe:c:\tools\t.exe", Target = @"C:\Tools\t.exe" } } } });
        await d.CallAsync(Msg.DockActivate, Item(@"exe:c:\tools\t.exe"));
        d.Launcher.Launched.Should().ContainSingle().Which.Target.Should().Be(LaunchTarget.FromPath(@"C:\Tools\t.exe"));
    }

    [Fact]
    public async Task Launching_prefers_the_catalog_entry_over_the_stored_pin_target()
    {
        using var d = Setup(pinAlpha: false);
        d.Catalog.Add("Beta", aumid: "V.Beta!App", path: @"C:\Beta\beta.exe");
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = "aumid:V.Beta!App", Target = "stale", Name = "Beta" } } } });
        await d.CallAsync(Msg.DockActivate, Item("aumid:V.Beta!App"));
        d.Launcher.Launched.Single().Target.Should().Be(LaunchTarget.FromAumid("V.Beta!App"));
    }

    [Fact]
    public async Task The_bounce_follows_the_bounceOnLaunch_setting()
    {
        using var d = Setup();
        d.Settings.Set(s => s with { Dock = s.Dock with { BounceOnLaunch = false } });
        await d.CallAsync(Msg.DockActivate, Item(AlphaId));
        d.Launcher.Launched.Should().HaveCount(1);
        d.Dock.Events(Msg.DockBounce).Should().BeEmpty();
    }

    [Fact]
    public async Task A_failed_launch_is_reported_and_leaves_no_trace()
    {
        using var d = Setup();
        d.Launcher.Result = LaunchResult.Failed;
        (await d.CallAsync(Msg.DockActivate, Item(AlphaId))).Should().Be("failed");
        d.Launcher.Result = LaunchResult.NotFound;
        (await d.CallAsync(Msg.DockActivate, Item(AlphaId))).Should().Be("notFound");
        d.Settle();
        d.Dock.Events(Msg.DockBounce).Should().BeEmpty();
        d.LastDockState()!.Items.Single().Launching.Should().BeFalse();
    }

    [Fact]
    public async Task Activating_a_running_app_focuses_its_most_recent_window()
    {
        using var d = Setup(x => { x.Windows.Add(W(1, aumid: AlphaAumid)); x.Windows.Add(W(2, aumid: AlphaAumid)); });
        (await d.CallAsync(Msg.DockActivate, Item(AlphaId))).Should().BeEmpty();
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("activate", 1) });
        d.Launcher.Launched.Should().BeEmpty();
    }

    [Fact]
    public async Task Activating_an_app_with_a_minimised_window_restores_it_first()
    {
        using var d = Setup(x => x.Windows.Add(W(1, aumid: AlphaAumid) with { IsMinimized = true }));
        await d.CallAsync(Msg.DockActivate, Item(AlphaId));
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("restore", 1), ("activate", 1) });
    }

    [Fact]
    public async Task Activating_the_foreground_app_with_several_windows_cycles_through_them()
    {
        using var d = Setup(x => { x.Windows.Add(W(10, aumid: AlphaAumid)); x.Windows.Add(W(20, aumid: AlphaAumid)); x.Windows.Add(W(30, aumid: AlphaAumid)); x.Windows.Foreground = 10; });
        var order = new List<long>();
        for (var i = 0; i < 4; i++)
        {
            await d.CallAsync(Msg.DockActivate, Item(AlphaId));
            var h = d.Windows.Calls[^1].Handle;
            order.Add(h);
            d.Windows.Raise(WindowEventKind.Foreground, h); // the OS reports the new foreground window
            d.Settle();
        }
        order.Should().Equal(20, 30, 10, 20);
    }

    [Fact]
    public async Task Activating_the_foreground_app_with_a_single_window_just_keeps_it_active()
    {
        using var d = Setup(x => { x.Windows.Add(W(1, aumid: AlphaAumid)); x.Windows.Foreground = 1; });
        (await d.CallAsync(Msg.DockActivate, Item(AlphaId))).Should().BeEmpty();
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("activate", 1) });
    }

    [Fact]
    public async Task A_running_app_that_is_not_pinned_can_be_activated_by_its_app_id()
    {
        using var d = Setup(pinAlpha: false, arrange: x => x.Windows.Add(W(5, exe: @"C:\Tools\tool.exe", pid: 5)));
        (await d.CallAsync(Msg.DockActivate, Item(@"exe:c:\tools\tool.exe"))).Should().BeEmpty();
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("activate", 5) });
    }

    [Fact]
    public async Task Activating_a_pinned_folder_opens_it()
    {
        using var d = Setup();
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = s.Dock.Pins.Concat(new[] { new PinnedItem { Id = @"dir:c:\users\me\docs", Kind = PinnedKind.Folder, Target = @"C:\Users\me\Docs" } }).ToList() } });
        (await d.CallAsync(Msg.DockActivate, Item(@"dir:c:\users\me\docs"))).Should().BeEmpty();
        d.Launcher.Opened.Should().Equal(@"C:\Users\me\Docs");
        d.Launcher.Launched.Should().BeEmpty();
    }

    [Theory]
    [InlineData("aumid:Nothing.Like.This!App", "notFound")]
    [InlineData(@"dir:c:\not\pinned", "notFound")]
    [InlineData(@"exe:c:\not\known.exe", "notFound")]
    [InlineData("whatever", "notFound")]
    [InlineData("separator:1", "badRequest")]
    [InlineData("", "badRequest")]
    public async Task Activating_garbage_is_rejected(string id, string code)
    {
        using var d = Setup();
        (await d.CallAsync(Msg.DockActivate, Item(id))).Should().Be(code);
        d.Launcher.Launched.Should().BeEmpty();
        d.Launcher.Opened.Should().BeEmpty();
        d.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Overlong_ids_and_ids_with_control_characters_are_rejected()
    {
        using var d = Setup();
        (await d.CallAsync(Msg.DockActivate, Item("aumid:" + new string('x', 5000)))).Should().Be("badRequest");
        (await d.CallAsync(Msg.DockActivate, Item("aumid:a\u0001b"))).Should().Be("badRequest");
    }

    [Fact]
    public async Task A_payload_without_itemId_is_a_bad_request()
    {
        using var d = Setup();
        (await d.CallAsync(Msg.DockActivate, new { })).Should().Be("badRequest");
    }

    // ---- window calls -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task ActivateWindow_activates_a_tracked_window()
    {
        using var d = Setup(x => x.Windows.Add(W(7)));
        (await d.CallAsync(Msg.DockActivateWindow, Handle(7))).Should().BeEmpty();
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("activate", 7) });
    }

    [Fact]
    public async Task ActivateWindow_restores_a_minimised_window_first()
    {
        using var d = Setup(x => x.Windows.Add(W(7) with { IsMinimized = true }));
        await d.CallAsync(Msg.DockActivateWindow, Handle(7));
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("restore", 7), ("activate", 7) });
    }

    [Theory]
    [InlineData("999")]
    [InlineData("0")]
    public async Task Window_calls_reject_unknown_handles(string handle)
    {
        using var d = Setup(x => x.Windows.Add(W(7)));
        (await d.CallAsync(Msg.DockActivateWindow, new WindowHandleParams { Handle = handle })).Should().Be(handle == "0" ? "badRequest" : "notFound");
        (await d.CallAsync(Msg.DockCloseWindow, new WindowHandleParams { Handle = handle })).Should().Be(handle == "0" ? "badRequest" : "notFound");
        d.Windows.Calls.Should().BeEmpty();
    }

    [Theory]
    [InlineData("abc")]
    [InlineData("")]
    [InlineData("12 34")]
    [InlineData("1e3")]
    public async Task Window_calls_reject_malformed_handles(string handle)
    {
        using var d = Setup(x => x.Windows.Add(W(7)));
        (await d.CallAsync(Msg.DockActivateWindow, new WindowHandleParams { Handle = handle })).Should().Be("badRequest");
        d.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Our_own_windows_can_never_be_acted_upon()
    {
        using var d = Setup(x => { x.Windows.Add(W(7)); x.Windows.Add(W(8, "Overlay") with { IsOwnProcess = true }); });
        (await d.CallAsync(Msg.DockActivateWindow, Handle(8))).Should().Be("notFound");
        (await d.CallAsync(Msg.DockCloseWindow, Handle(8))).Should().Be("notFound");
        d.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_recycled_handle_of_another_process_is_not_touched()
    {
        using var d = Setup(x => x.Windows.Add(W(7, pid: 100)));
        // the window died and the OS handed the same handle to a window of ANOTHER process before the tracker noticed
        d.Windows.Update(7, w => w with { ProcessId = 999 });
        (await d.CallAsync(Msg.DockActivateWindow, Handle(7))).Should().Be("notFound");
        (await d.CallAsync(Msg.DockCloseWindow, Handle(7))).Should().Be("notFound");
        d.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task A_window_that_vanished_since_the_last_snapshot_is_reported_as_gone()
    {
        using var d = Setup(x => x.Windows.Add(W(7)));
        d.Windows.Remove(7);
        (await d.CallAsync(Msg.DockCloseWindow, Handle(7))).Should().Be("notFound");
        d.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task CloseWindow_posts_a_close_to_a_tracked_window()
    {
        using var d = Setup(x => x.Windows.Add(W(7)));
        (await d.CallAsync(Msg.DockCloseWindow, Handle(7))).Should().BeEmpty();
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("close", 7) });
    }

    // ---- newWindow / quit / hide / reveal -------------------------------------------------------------------------------------------

    [Fact]
    public async Task NewWindow_launches_again_even_when_the_app_is_running_without_a_bounce_or_launching_flag()
    {
        using var d = Setup(x => x.Windows.Add(W(1, aumid: AlphaAumid)));
        (await d.CallAsync(Msg.DockNewWindow, Item(AlphaId))).Should().BeEmpty();
        d.Launcher.Launched.Should().ContainSingle().Which.Target.Should().Be(LaunchTarget.FromAumid(AlphaAumid));
        d.Dock.Events(Msg.DockBounce).Should().BeEmpty();
        d.Settle();
        d.LastDockState()!.Items.Single().Launching.Should().BeFalse();
    }

    [Fact]
    public async Task NewWindow_of_a_folder_opens_it()
    {
        using var d = Setup();
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = @"dir:c:\d", Kind = PinnedKind.Folder, Target = @"C:\D" } } } });
        await d.CallAsync(Msg.DockNewWindow, Item(@"dir:c:\d"));
        d.Launcher.Opened.Should().Equal(@"C:\D");
    }

    [Fact]
    public async Task Quit_closes_every_window_of_the_app_and_nothing_else()
    {
        using var d = Setup(x =>
        {
            x.Windows.Add(W(1, aumid: AlphaAumid)); x.Windows.Add(W(2, aumid: AlphaAumid));
            x.Windows.Add(W(3, exe: @"C:\Apps\beta\beta.exe", pid: 3));
        });
        (await d.CallAsync(Msg.DockQuit, Item(AlphaId))).Should().BeEmpty();
        d.Windows.Calls.Should().BeEquivalentTo(new (string, long)[] { ("close", 1), ("close", 2) });
    }

    [Fact]
    public async Task Quit_works_for_any_running_app_id_not_only_visible_dock_items()
    {
        using var d = Setup(pinAlpha: false, arrange: x => x.Windows.Add(W(3, exe: @"C:\Apps\beta\beta.exe", pid: 3)));
        (await d.CallAsync(Msg.DockQuit, Item(@"exe:c:\apps\beta\beta.exe"))).Should().BeEmpty();
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("close", 3) });
    }

    [Fact]
    public async Task Quit_of_a_pinned_app_that_is_not_running_is_a_no_op_and_of_an_unknown_id_not_found()
    {
        using var d = Setup();
        (await d.CallAsync(Msg.DockQuit, Item(AlphaId))).Should().BeEmpty();
        (await d.CallAsync(Msg.DockQuit, Item("aumid:Unknown!App"))).Should().Be("notFound");
        d.Windows.Calls.Should().BeEmpty();
    }

    [Fact]
    public async Task Quit_skips_windows_that_changed_owner_process_since_the_snapshot()
    {
        using var d = Setup(x => { x.Windows.Add(W(1, aumid: AlphaAumid, pid: 100)); x.Windows.Add(W(2, aumid: AlphaAumid, pid: 100)); });
        d.Windows.Update(2, w => w with { ProcessId = 777 });
        await d.CallAsync(Msg.DockQuit, Item(AlphaId));
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("close", 1) });
    }

    [Fact]
    public async Task Hide_minimises_the_windows_that_are_not_minimised_yet()
    {
        using var d = Setup(x =>
        {
            x.Windows.Add(W(1, aumid: AlphaAumid)); x.Windows.Add(W(2, aumid: AlphaAumid) with { IsMinimized = true });
            x.Windows.Add(W(3, exe: @"C:\Apps\beta\beta.exe", pid: 3));
        });
        (await d.CallAsync(Msg.DockHide, Item(AlphaId))).Should().BeEmpty();
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("minimize", 1) });
    }

    [Fact]
    public async Task RevealInExplorer_uses_the_catalog_path_the_pin_path_or_a_window_path()
    {
        using var d = Setup(pinAlpha: false);
        d.Catalog.Add("Beta", path: @"C:\Beta\beta.exe");
        d.Windows.Add(W(9, exe: @"C:\Gamma\gamma.exe", pid: 9));
        d.Windows.Raise(WindowEventKind.Created, 9);
        d.Settle();
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = @"exe:c:\delta\delta.exe", Target = @"C:\Delta\delta.exe" } } } });

        (await d.CallAsync(Msg.DockRevealInExplorer, Item(@"exe:c:\beta\beta.exe"))).Should().BeEmpty();
        (await d.CallAsync(Msg.DockRevealInExplorer, Item(@"exe:c:\delta\delta.exe"))).Should().BeEmpty();
        (await d.CallAsync(Msg.DockRevealInExplorer, Item(@"exe:c:\gamma\gamma.exe"))).Should().BeEmpty();
        d.Launcher.Revealed.Should().Equal(@"C:\Beta\beta.exe", @"C:\Delta\delta.exe", @"C:\Gamma\gamma.exe");
    }

    [Fact]
    public async Task RevealInExplorer_of_a_folder_and_of_an_app_without_a_file_location()
    {
        using var d = Setup();
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = s.Dock.Pins.Concat(new[] { new PinnedItem { Id = @"dir:c:\d", Kind = PinnedKind.Folder, Target = @"C:\D" } }).ToList() } });
        (await d.CallAsync(Msg.DockRevealInExplorer, Item(@"dir:c:\d"))).Should().BeEmpty();
        d.Launcher.Revealed.Should().Equal(@"C:\D");
        (await d.CallAsync(Msg.DockRevealInExplorer, Item(AlphaId))).Should().Be("notFound", "a packaged app known only by AUMID has no path");
    }

    // ---- infrastructure ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_dock_call_of_the_catalog_has_a_handler()
    {
        using var d = Setup();
        var expected = MessageCatalog.All.Where(m => m.Kind == MessageKind.Call && m.Type.StartsWith("dock.")).Select(m => m.Type).ToArray();
        expected.Should().NotBeEmpty();
        d.H.Router.RegisteredTypes.Should().Contain(expected);
    }

    [Fact]
    public async Task A_menubar_cannot_call_dock_actions()
    {
        using var d = Setup();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        (await d.CallAsync(Msg.DockQuit, Item(AlphaId), menubar)).Should().Be("forbidden");
        (await d.CallAsync(Msg.DockPin, new DockPinParams { AppId = AlphaId }, menubar)).Should().Be("forbidden");
    }

    [Fact]
    public async Task The_overlay_and_settings_surfaces_may_call_dock_actions()
    {
        using var d = Setup(x => x.Windows.Add(W(1, aumid: AlphaAumid)));
        var overlay = d.H.AddSurface(SurfaceKind.Overlay);
        var settings = d.H.AddSurface(SurfaceKind.Settings);
        (await d.CallAsync(Msg.DockHide, Item(AlphaId), overlay)).Should().BeEmpty();
        (await d.CallAsync(Msg.DockHide, Item(AlphaId), settings)).Should().BeEmpty();
    }

    [Fact]
    public async Task After_Stop_calls_are_refused_instead_of_acting()
    {
        using var d = Setup(x => x.Windows.Add(W(1, aumid: AlphaAumid)));
        d.H.Get<BetterWindows.Core.Hosting.ShellRuntime>().Stop();
        (await d.CallAsync(Msg.DockQuit, Item(AlphaId))).Should().Be("failed");
        d.Windows.Calls.Should().BeEmpty();
    }
}
