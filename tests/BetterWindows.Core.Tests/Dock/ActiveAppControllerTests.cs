using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.Dock;

public class ActiveAppControllerTests
{
    private static RawWindow W(long handle, string title = "Window", string exe = @"C:\Apps\alpha\alpha.exe", string? aumid = null, int pid = 100) => FakeWindowSystem.Win(handle, title, exe, aumid, pid);
    private static ActiveAppState? Last(FakeSurface s) => s.LastEvent<ActiveAppState>(Msg.ActiveAppChanged);

    [Fact]
    public void A_menubar_gets_an_empty_state_when_nothing_is_active()
    {
        using var d = new DockHarness();
        d.Start();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        menubar.Events(Msg.ActiveAppChanged).Should().ContainSingle();
        Last(menubar)!.App.Should().BeNull();
    }

    [Fact]
    public void The_foreground_app_is_pushed_right_after_surface_ready()
    {
        using var d = new DockHarness();
        d.Catalog.Add("Alpha", aumid: "V.Alpha!App");
        d.Windows.Add(W(42, "Inbox - Alpha", aumid: "V.Alpha!App"));
        d.Windows.Foreground = 42;
        d.Start();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        var app = Last(menubar)!.App!;
        (app.AppId, app.Name, app.Handle, app.Title).Should().Be(("aumid:V.Alpha!App", "Alpha", "42", "Inbox - Alpha"));
        app.IconUrl.Should().Be(DynUrls.AppIcon("aumid:V.Alpha!App", 64));
    }

    [Fact]
    public void The_overlay_gets_it_too_and_the_dock_does_not()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1)); d.Windows.Foreground = 1;
        d.Start();
        d.H.AddSurface(SurfaceKind.Overlay).Events(Msg.ActiveAppChanged).Should().ContainSingle();
        d.H.AddSurface(SurfaceKind.Dock).Events(Msg.ActiveAppChanged).Should().BeEmpty();
    }

    [Fact]
    public void Switching_apps_publishes_the_new_foreground_app()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1)); d.Windows.Add(W(2, "Beta window", @"C:\Apps\beta\beta.exe", pid: 2));
        d.Windows.Foreground = 1;
        d.Start();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        d.Windows.Foreground = 2;
        d.Windows.Raise(WindowEventKind.Foreground, 2);
        d.Settle();
        var app = Last(menubar)!.App!;
        (app.AppId, app.Name, app.Title).Should().Be((@"exe:c:\apps\beta\beta.exe", "Beta", "Beta window"));
        app.IconUrl.Should().Be(DynUrls.WindowIcon(2, 64), "an app unknown to the catalog uses its window icon");
    }

    [Fact]
    public void A_title_change_of_the_foreground_window_republishes()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1, "Old")); d.Windows.Foreground = 1;
        d.Start();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        d.Windows.Update(1, w => w with { Title = "New" });
        d.Windows.Raise(WindowEventKind.TitleChanged, 1);
        d.Settle();
        Last(menubar)!.App!.Title.Should().Be("New");
    }

    [Fact]
    public void Our_own_windows_never_become_the_active_app()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1)); d.Windows.Add(W(9, "BetterWindows overlay", @"C:\bw\bw.exe", pid: 9) with { IsOwnProcess = true });
        d.Windows.Foreground = 1;
        d.Start();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        menubar.Clear();
        d.Windows.Foreground = 9;
        d.Windows.Raise(WindowEventKind.Foreground, 9);
        d.Settle();
        menubar.Events(Msg.ActiveAppChanged).Should().BeEmpty("nothing changed from the menu bar's point of view");
        Last(d.H.AddSurface(SurfaceKind.Menubar, "m2"))!.App!.AppId.Should().Be(@"exe:c:\apps\alpha\alpha.exe");
    }

    [Fact]
    public void When_the_foreground_window_closes_the_app_becomes_null()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1)); d.Windows.Foreground = 1;
        d.Start();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        d.Windows.Remove(1);
        d.Windows.Raise(WindowEventKind.Destroyed, 1);
        d.Settle();
        menubar.Events(Msg.ActiveAppChanged).Should().HaveCount(2);
        Last(menubar)!.App.Should().BeNull();
    }

    [Fact]
    public void A_catalog_change_updates_the_name_and_icon()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1)); d.Windows.Foreground = 1;
        d.Start();
        var menubar = d.H.AddSurface(SurfaceKind.Menubar, "m1");
        Last(menubar)!.App!.IconUrl.Should().Be(DynUrls.WindowIcon(1, 64));
        var entry = d.Catalog.Add("Alpha Pro", path: @"C:\Apps\alpha\alpha.exe");
        d.Catalog.RaiseChanged();
        d.Settle();
        var app = Last(menubar)!.App!;
        (app.AppId, app.Name, app.IconUrl).Should().Be((entry.Id, "Alpha Pro", DynUrls.AppIcon(entry.Id, 64)));
    }

    [Fact]
    public void A_title_with_control_characters_is_sanitised_and_capped()
    {
        using var d = new DockHarness();
        d.Windows.Add(W(1, "a\u0007b" + new string('x', 400))); d.Windows.Foreground = 1;
        d.Start();
        var title = Last(d.H.AddSurface(SurfaceKind.Menubar, "m1"))!.App!.Title!;
        title.Length.Should().Be(256);
        title.Should().NotContain("\u0007");
    }
}
