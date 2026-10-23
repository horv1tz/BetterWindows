using BetterWindows.Core.Controllers;
using BetterWindows.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Config;
using BetterWindows.Core.Tests.Resources;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Xunit;

namespace BetterWindows.Core.Tests.Apps;

public class AppsControllerTests : IDisposable
{
    private readonly TestWindowTracker _tracker = new();
    private readonly CoreHarness _h;

    public AppsControllerTests()
    {
        _h = ControllerRig.Start<AppsController>(sc => sc.AddSingleton<IWindowTracker>(_tracker));
    }

    public void Dispose() => _h.Dispose();

    private FakeAppCatalogSource Source => _h.Get<FakeAppCatalogSource>();
    private FakeAppLauncher Launcher => _h.Get<FakeAppLauncher>();

    /// <summary>Scans the fake start menu so the real catalog has its entries.</summary>
    private async Task ScanAsync() => await _h.Get<IAppCatalog>().RefreshAsync();

    private async Task<string> LaunchError(FakeSurface from, string appId) =>
        ControllerRig.ErrorCode(await _h.CallRawAsync(from, Msg.AppsLaunch, new AppLaunchParams { AppId = appId }));

    [Fact]
    public async Task Apps_list_returns_the_scanned_catalog_with_icon_urls()
    {
        Source.Apps.Add(FakeAppCatalogSource.App("Notes", aumid: "Contoso.Notes!App"));
        Source.Apps.Add(FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        await ScanAsync();

        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var list = await _h.CallAsync<AppList>(overlay, Msg.AppsList);
        Assert.Equal(new[] { "Notes", "Paint" }, list!.Apps.Select(a => a.Name));
        Assert.All(list.Apps, a => Assert.Equal(DynUrls.AppIcon(a.Id, 128), a.IconUrl));
    }

    [Fact]
    public async Task A_catalog_app_launches_by_its_aumid()
    {
        Source.Apps.Add(FakeAppCatalogSource.App("Notes", aumid: "Contoso.Notes!App"));
        await ScanAsync();
        var dock = _h.AddSurface(SurfaceKind.Dock);

        var r = await _h.CallRawAsync(dock, Msg.AppsLaunch, new AppLaunchParams { AppId = "aumid:Contoso.Notes!App" });
        Assert.True(r["ok"]!.GetValue<bool>());
        var launched = Launcher.Launched.Single();
        Assert.Equal("Contoso.Notes!App", launched.Target.Aumid);
        Assert.Null(launched.Target.Path);
        Assert.Null(launched.Args);
    }

    [Fact]
    public async Task Unknown_aumids_are_not_found_and_malformed_ids_are_bad_requests()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.Equal("notFound", await LaunchError(overlay, "aumid:Nope.App!X"));
        Assert.Equal("badRequest", await LaunchError(overlay, ""));
        Assert.Equal("badRequest", await LaunchError(overlay, "notAnId"));
        Assert.Equal("badRequest", await LaunchError(overlay, "exe:relative\\x.exe"));
        Assert.Equal("badRequest", await LaunchError(overlay, "exe:\\\\server\\share\\x.exe"));
        Assert.Empty(Launcher.Launched);
    }

    [Fact]
    public async Task Folders_are_not_apps()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.Equal("notFound", await LaunchError(overlay, @"dir:C:\Users\me\Documents"));
        Assert.Empty(Launcher.Launched);
    }

    [Fact]
    public async Task An_exe_is_launched_only_when_the_catalog_a_running_window_or_a_grant_knows_it()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.Equal("notFound", await LaunchError(overlay, @"exe:c:\tools\unknown.exe"));

        Source.Apps.Add(FakeAppCatalogSource.App("Tool", path: @"C:\Tools\Tool.exe"));
        await ScanAsync();
        Assert.True((await _h.CallRawAsync(overlay, Msg.AppsLaunch, new AppLaunchParams { AppId = @"exe:c:\tools\tool.exe" }))["ok"]!.GetValue<bool>());
        Assert.Equal(@"C:\Tools\Tool.exe", Launcher.Launched.Single().Target.Path, ignoreCase: true);

        _tracker.Add(7, appId: @"exe:c:\games\g.exe", name: "Game", exe: @"C:\games\g.exe");
        Assert.True((await _h.CallRawAsync(overlay, Msg.AppsLaunch, new AppLaunchParams { AppId = @"exe:c:\games\g.exe" }))["ok"]!.GetValue<bool>());

        _h.Get<IPathGrants>().Grant(@"C:\picked\picked.exe");
        Assert.True((await _h.CallRawAsync(overlay, Msg.AppsLaunch, new AppLaunchParams { AppId = @"exe:c:\picked\picked.exe" }))["ok"]!.GetValue<bool>());
        Assert.Equal(3, Launcher.Launched.Count);
    }

    [Fact]
    public async Task A_running_app_without_a_catalog_entry_launches_by_aumid()
    {
        _tracker.Add(8, appId: "aumid:Other.App!Main", name: "Other", exe: null, aumid: "Other.App!Main");
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.True((await _h.CallRawAsync(overlay, Msg.AppsLaunch, new AppLaunchParams { AppId = "aumid:Other.App!Main" }))["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task Launcher_outcomes_map_to_protocol_errors()
    {
        Source.Apps.Add(FakeAppCatalogSource.App("Notes", aumid: "Contoso.Notes!App"));
        await ScanAsync();
        var overlay = _h.AddSurface(SurfaceKind.Overlay);

        Launcher.Result = LaunchResult.NotFound;
        Assert.Equal("notFound", await LaunchError(overlay, "aumid:Contoso.Notes!App"));
        Launcher.Result = LaunchResult.Failed;
        Assert.Equal("failed", await LaunchError(overlay, "aumid:Contoso.Notes!App"));
    }

    [Fact]
    public async Task Launch_is_refused_from_the_menu_bar()
    {
        var bar = _h.AddSurface(SurfaceKind.Menubar, "m1");
        Assert.Equal("forbidden", await LaunchError(bar, "aumid:Contoso.Notes!App"));
    }

    [Fact]
    public async Task Reveal_shows_the_exe_of_a_catalog_app_and_refuses_packaged_apps_without_a_file()
    {
        Source.Apps.Add(FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        Source.Apps.Add(FakeAppCatalogSource.App("Notes", aumid: "Contoso.Notes!App"));
        await ScanAsync();
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);

        Assert.True((await _h.CallRawAsync(settingsWindow, Msg.AppsReveal, new AppIdParams { AppId = "exe:c:\\windows\\mspaint.exe" }))["ok"]!.GetValue<bool>());
        Assert.Equal(@"C:\Windows\mspaint.exe", Launcher.Revealed.Single(), ignoreCase: true);

        var packaged = await _h.CallRawAsync(settingsWindow, Msg.AppsReveal, new AppIdParams { AppId = "aumid:Contoso.Notes!App" });
        Assert.Equal("unsupported", ControllerRig.ErrorCode(packaged));
        Assert.Single(Launcher.Revealed);
    }

    [Fact]
    public async Task Reveal_of_an_unknown_app_is_not_found()
    {
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);
        var r = await _h.CallRawAsync(settingsWindow, Msg.AppsReveal, new AppIdParams { AppId = @"exe:c:\nowhere\x.exe" });
        Assert.Equal("notFound", ControllerRig.ErrorCode(r));
        Assert.Empty(Launcher.Revealed);
    }

    [Fact]
    public async Task Reveal_is_refused_to_the_menu_bar_but_allowed_from_the_overlay()
    {
        var bar = _h.AddSurface(SurfaceKind.Menubar, "m1");
        Assert.Equal("forbidden", ControllerRig.ErrorCode(await _h.CallRawAsync(bar, Msg.AppsReveal, new AppIdParams { AppId = "aumid:X!Y" })));

        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        Assert.Equal("notFound", ControllerRig.ErrorCode(await _h.CallRawAsync(overlay, Msg.AppsReveal, new AppIdParams { AppId = "aumid:X!Y" })));
    }

    [Fact]
    public async Task A_catalog_change_is_pushed_to_the_overlay_only()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var settingsWindow = _h.AddSurface(SurfaceKind.Settings);

        Source.Apps.Add(FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        Source.RaiseChanged();
        await ScanAsync();
        _h.Time.Advance(TimeSpan.FromMilliseconds(200));

        var last = overlay.LastEvent<AppList>(Msg.AppsChanged);
        Assert.NotNull(last);
        Assert.Single(last!.Apps);
        Assert.Empty(settingsWindow.Events(Msg.AppsChanged));
        Assert.Empty(overlay.Violations);
    }

    [Fact]
    public async Task Apps_changed_is_not_sent_to_the_dock()
    {
        var dock = _h.AddSurface(SurfaceKind.Dock);
        Source.Apps.Add(FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        Source.RaiseChanged();
        await ScanAsync();
        _h.Time.Advance(TimeSpan.FromMilliseconds(200));
        Assert.Empty(dock.Events(Msg.AppsChanged));
    }
}
