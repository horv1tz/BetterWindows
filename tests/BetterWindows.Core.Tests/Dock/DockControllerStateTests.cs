using BetterWindows.Core.Controllers;
using BetterWindows.Core.Hosting;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Dock;

public class DockControllerStateTests
{
    private static RawWindow W(long handle, string title = "Window", string exe = @"C:\Apps\alpha\alpha.exe", string? aumid = null, int pid = 100) => FakeWindowSystem.Win(handle, title, exe, aumid, pid);

    [Fact]
    public void A_dock_gets_the_current_state_right_after_surface_ready()
    {
        using var d = new DockHarness();
        var alpha = d.Catalog.Add("Alpha", aumid: "V.Alpha!App");
        d.Settings.Set(s => s with { General = s.General with { FirstRunDone = true }, Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = alpha.Id, Target = alpha.Aumid!, Name = "Alpha" } } } });
        d.Windows.Add(W(1, aumid: alpha.Aumid));
        d.Start();

        var dock = d.AddDock();
        var state = d.LastDockState()!;
        state.Items.Should().ContainSingle();
        state.Items[0].Id.Should().Be(alpha.Id);
        state.Items[0].Running.Should().BeTrue();
        state.Items[0].Windows.Should().ContainSingle().Which.Handle.Should().Be("1");
        dock.Events(Msg.DockState).Should().HaveCount(1);
    }

    [Fact]
    public async Task The_real_surface_ready_handshake_pushes_dock_state_after_the_reply()
    {
        using var d = new DockHarness(sc => sc.AddSingleton<IShellController>(sp => ActivatorUtilities.CreateInstance<FakeSurfaceReadyHandler>(sp)));
        d.Windows.Add(W(1));
        d.Start();
        var dock = d.H.AddSurface(SurfaceKind.Dock, ready: false);
        await d.H.ReadyAsync(dock);
        dock.Events(Msg.DockState).Should().HaveCount(1);
        var all = dock.All.Select(e => (string?)e["type"] ?? "result").ToList();
        all.IndexOf("result").Should().BeLessThan(all.IndexOf(Msg.DockState), "state events follow the surface.ready reply");
        d.Dock = dock;
    }

    [Fact]
    public void Overlay_surfaces_get_dock_state_but_menubars_do_not()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1));
        d.Start();
        var overlay = d.H.AddSurface(SurfaceKind.Overlay);
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        overlay.Events(Msg.DockState).Should().HaveCount(1);
        menubar.Events(Msg.DockState).Should().BeEmpty();
    }

    [Fact]
    public void Window_changes_republish_the_state_coalesced_to_50_ms()
    {
        using var d = new DockHarness();
        d.Start();
        var dock = d.AddDock();
        dock.Clear();

        d.Windows.Add(W(1)); d.Windows.Add(W(2, exe: @"C:\Apps\beta\beta.exe", pid: 2));
        d.Windows.Raise(WindowEventKind.Created, 1);
        d.Windows.Raise(WindowEventKind.Created, 2);
        d.Time.Advance(TimeSpan.FromMilliseconds(10));
        dock.Events(Msg.DockState).Should().BeEmpty("coalescing: not before the window elapsed");
        d.Settle();
        dock.Events(Msg.DockState).Should().HaveCount(1);
        d.LastDockState()!.Items.Should().HaveCount(2);
    }

    [Fact]
    public void An_unchanged_state_is_not_posted_again()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1));
        d.Start();
        var dock = d.AddDock();
        dock.Clear();
        d.Windows.Raise(WindowEventKind.Redraw, 1);
        d.Settle();
        dock.Events(Msg.DockState).Should().BeEmpty();
    }

    [Fact]
    public void A_title_change_reaches_the_window_list()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1, "Old"));
        d.Start();
        d.AddDock();
        d.Windows.Update(1, w => w with { Title = "Fresh title" });
        d.Windows.Raise(WindowEventKind.TitleChanged, 1);
        d.Settle();
        d.LastDockState()!.Items.Single().Windows.Single().Title.Should().Be("Fresh title");
    }

    [Fact]
    public void Closing_the_last_window_of_an_unpinned_app_removes_its_item()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1));
        d.Start();
        d.AddDock();
        d.LastDockState()!.Items.Should().ContainSingle();
        d.Windows.Remove(1);
        d.Windows.Raise(WindowEventKind.Destroyed, 1);
        d.Settle();
        d.LastDockState()!.Items.Should().BeEmpty();
    }

    [Fact]
    public void Minimised_windows_show_up_in_the_minimised_section()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1));
        d.Start();
        d.AddDock();
        d.Windows.Update(1, w => w with { IsMinimized = true });
        d.Windows.Raise(WindowEventKind.Minimized, 1);
        d.Settle();
        d.LastDockState()!.Minimized.Should().ContainSingle().Which.Handle.Should().Be("1");
    }

    [Fact]
    public void The_active_item_follows_the_foreground_window()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1)); d.Windows.Add(W(2, exe: @"C:\Apps\beta\beta.exe", pid: 2));
        d.Start();
        d.AddDock();
        d.Windows.Foreground = 2;
        d.Windows.Raise(WindowEventKind.Foreground, 2);
        d.Settle();
        d.LastDockState()!.ActiveItemId.Should().Be(@"exe:c:\apps\beta\beta.exe");
    }

    [Fact]
    public void Changing_the_pins_in_the_settings_updates_the_dock()
    {
        using var d = new DockHarness();
        d.Start();
        d.Settings.Set(s => s with { General = s.General with { FirstRunDone = true } });
        d.AddDock();
        d.LastDockState()!.Items.Should().BeEmpty();
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = "exe:c:\\x.exe", Target = @"C:\x.exe", Name = "X" } } } });
        d.Settle();
        d.LastDockState()!.Items.Should().ContainSingle().Which.Name.Should().Be("X");
    }

    [Fact]
    public void A_catalog_change_renames_items()
    {
        using var d = new DockHarness();
        d.Settings.Set(s => s with { General = s.General with { FirstRunDone = true }, Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = "aumid:V.Alpha!App", Target = "V.Alpha!App", Name = "Old" } } } });
        d.Start();
        d.AddDock();
        d.LastDockState()!.Items.Single().Name.Should().Be("Old");
        d.Catalog.Add("Alpha (new)", aumid: "V.Alpha!App");
        d.Catalog.RaiseChanged();
        d.Settle();
        d.LastDockState()!.Items.Single().Name.Should().Be("Alpha (new)");
    }

    [Fact]
    public void With_dock_monitor_primary_only_the_primary_monitors_dock_gets_state()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1));
        d.Start();
        d.H.Get<FakeMonitorProvider>().Monitors.Add(new MonitorInfo { Id = "m2", Index = 1, Primary = false, Scale = 1, BoundsPx = new Rect { X = 1920, Width = 1920, Height = 1080 }, WorkAreaPx = new Rect { X = 1920, Width = 1920, Height = 1040 } });
        var primary = d.H.AddSurface(SurfaceKind.Dock, "m1");
        var secondary = d.H.AddSurface(SurfaceKind.Dock, "m2");
        primary.Events(Msg.DockState).Should().HaveCount(1);
        secondary.Events(Msg.DockState).Should().BeEmpty();
    }

    [Fact]
    public void With_dock_monitor_all_every_dock_gets_state()
    {
        using var d = new DockHarness();
        d.Settings.Set(s => s with { Dock = s.Dock with { Monitor = DockMonitor.All } });
        d.Windows.Add(W(1));
        d.Start();
        var primary = d.H.AddSurface(SurfaceKind.Dock, "m1");
        var secondary = d.H.AddSurface(SurfaceKind.Dock, "m2");
        primary.Events(Msg.DockState).Should().HaveCount(1);
        secondary.Events(Msg.DockState).Should().HaveCount(1);
    }

    [Fact]
    public void A_window_that_starts_flashing_bounces_its_item_once_and_never_when_it_is_the_foreground()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1)); d.Windows.Add(W(2, exe: @"C:\Apps\beta\beta.exe", pid: 2));
        d.Windows.Foreground = 1;
        d.Start();
        var dock = d.AddDock();
        dock.Clear();

        d.Windows.Update(2, w => w with { IsFlashing = true });
        d.Windows.Raise(WindowEventKind.Flashed, 2);
        d.Settle();
        var bounces = dock.Events(Msg.DockBounce);
        bounces.Should().ContainSingle();
        var payload = ProtocolJson.Deserialize<DockBounce>(bounces[0]["payload"])!;
        payload.ItemId.Should().Be(@"exe:c:\apps\beta\beta.exe");
        payload.Kind.Should().Be(DockBounceKind.Attention);
        d.LastDockState()!.Items.Single(i => i.Id == payload.ItemId).Attention.Should().BeTrue();

        // another event while it keeps flashing: no second bounce
        d.Windows.Update(2, w => w with { Title = "still flashing" });
        d.Windows.Raise(WindowEventKind.TitleChanged, 2);
        d.Settle();
        dock.Events(Msg.DockBounce).Should().HaveCount(1);

        // the foreground window flashing is ignored
        d.Windows.Update(1, w => w with { IsFlashing = true });
        d.Windows.Raise(WindowEventKind.Flashed, 1);
        d.Settle();
        dock.Events(Msg.DockBounce).Should().HaveCount(1);
    }

    [Fact]
    public void First_run_seeds_the_default_pins_once_the_catalog_has_apps_and_marks_the_first_run_done()
    {
        using var d = new DockHarness();
        d.Start();
        d.Settings.Current.Dock.Pins.Should().BeEmpty("the catalog is not loaded yet");
        d.Settings.Current.General.FirstRunDone.Should().BeFalse();

        d.Catalog.Add("Notepad", aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
        d.Catalog.Add("Calculator", aumid: "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App");
        d.Catalog.Add("Spotify", aumid: "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify");
        d.Catalog.RaiseChanged();

        d.Settings.Current.Dock.Pins.Select(p => p.Name).Should().Equal("Notepad", "Calculator");
        d.Settings.Current.General.FirstRunDone.Should().BeTrue();
    }

    [Fact]
    public async Task A_user_who_removed_every_pin_is_not_seeded_again()
    {
        using var d = new DockHarness();
        d.Catalog.Add("Notepad", aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
        d.Start();
        d.Settings.Current.Dock.Pins.Should().ContainSingle();
        var dock = d.AddDock();

        (await d.CallAsync(Msg.DockUnpin, new DockItemParams { ItemId = d.Settings.Current.Dock.Pins[0].Id })).Should().BeEmpty();
        d.Catalog.RaiseChanged();
        d.Settings.Current.Dock.Pins.Should().BeEmpty();
    }

    [Fact]
    public void Existing_pins_or_a_finished_first_run_prevent_seeding()
    {
        using var d1 = new DockHarness();
        d1.Catalog.Add("Notepad", aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
        d1.Settings.Set(s => s with { Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = "exe:c:\\x.exe", Target = @"C:\x.exe" } } } });
        d1.Start();
        d1.Settings.Current.Dock.Pins.Should().ContainSingle().Which.Id.Should().Be("exe:c:\\x.exe");

        using var d2 = new DockHarness();
        d2.Catalog.Add("Notepad", aumid: "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App");
        d2.Settings.Set(s => s with { General = s.General with { FirstRunDone = true } });
        d2.Start();
        d2.Settings.Current.Dock.Pins.Should().BeEmpty();
    }

    [Fact]
    public void Stop_unsubscribes_from_everything()
    {
        using var d = new DockHarness();
        d.Start();
        var dock = d.AddDock();
        d.H.Get<ShellRuntime>().Stop();
        dock.Clear();
        d.Windows.Add(W(1));
        d.Windows.Raise(WindowEventKind.Created, 1);
        d.Catalog.RaiseChanged();
        d.Settle();
        dock.Events(Msg.DockState).Should().BeEmpty();
    }

    /// <summary>Stand-in for SurfaceController (core-settings): answers surface.ready so the real handshake can be exercised in this slice.</summary>
    private sealed class FakeSurfaceReadyHandler : IShellController
    {
        private readonly BetterWindows.Core.Ipc.IMessageRouter _router;
        public FakeSurfaceReadyHandler(BetterWindows.Core.Ipc.IMessageRouter router) => _router = router;
        public string Name => "FakeSurfaceReady";
        public void Start() => _router.Handle<SurfaceReadyParams, SurfaceInit>(Msg.SurfaceReady, (ctx, p) => Task.FromResult(new SurfaceInit()));
        public void Stop() { }
    }
}
