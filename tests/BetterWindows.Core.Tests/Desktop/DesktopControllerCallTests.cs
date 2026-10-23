using System.Text.Json.Nodes;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Desktop;

public class DesktopIconCallTests
{
    // ---- desktop.moveIcon

    [Fact]
    public async Task Move_icon_changes_the_shortcut_cell_in_the_settings_and_the_state()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0) } }));
        var surface = rig.AddDesktop("m1");
        await rig.Ok<object?>(surface, Msg.DesktopMoveIcon, new DesktopMoveParams { Id = "sc:a", X = 4, Y = 2 });
        rig.Settings.Current.Desktop.Shortcuts.Single().Should().Match<DesktopShortcut>(s => s.X == 4 && s.Y == 2);
        rig.State(surface)!.Icons.Single().Should().Match<DesktopIcon>(i => i.X == 4 && i.Y == 2);
    }

    [Fact]
    public async Task Move_icon_onto_another_icon_swaps_them()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0), DesktopRig.Shortcut("sc:b", 3, 1) } }));
        await rig.Ok<object?>(rig.AddDesktop("m1"), Msg.DesktopMoveIcon, new DesktopMoveParams { Id = "sc:a", X = 3, Y = 1 });
        var sc = rig.Settings.Current.Desktop.Shortcuts;
        sc.Single(s => s.Id == "sc:a").Should().Match<DesktopShortcut>(s => s.X == 3 && s.Y == 1);
        sc.Single(s => s.Id == "sc:b").Should().Match<DesktopShortcut>(s => s.X == 0 && s.Y == 0);
    }

    [Fact]
    public async Task Move_icon_accepts_fractional_coordinates_from_a_js_ui()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0) } }));
        var raw = await rig.Call(rig.AddDesktop("m1"), Msg.DesktopMoveIcon, new JsonObject { ["id"] = "sc:a", ["x"] = 2.4, ["y"] = 3.6 });
        Kit.Code(raw).Should().Be("ok");
        rig.Settings.Current.Desktop.Shortcuts.Single().Should().Match<DesktopShortcut>(s => s.X == 2 && s.Y == 4);
    }

    [Fact]
    public async Task Move_icon_moves_a_desktop_folder_item_through_placements()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Folder.Entries.Add(new DesktopEntry(@"fs:c:\a", "a", @"C:\a", false, false)); r.Configure(d => d); });
        await rig.Ok<object?>(rig.AddDesktop("m1"), Msg.DesktopMoveIcon, new DesktopMoveParams { Id = @"fs:c:\a", X = 5, Y = 5 });
        rig.Settings.Current.Desktop.Placements[@"fs:c:\a"].Should().Match<DesktopCell>(c => c.X == 5 && c.Y == 5);
    }

    [Fact]
    public async Task Move_icon_of_an_unknown_id_is_not_found()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopMoveIcon, new DesktopMoveParams { Id = "sc:nope", X = 1, Y = 1 })).Should().Be("notFound");
    }

    [Theory]
    [InlineData("")]
    [InlineData("sc:\u0001bad")]
    public async Task Move_icon_with_an_invalid_id_is_a_bad_request(string id)
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopMoveIcon, new DesktopMoveParams { Id = id, X = 1, Y = 1 })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Move_icon_with_a_missing_field_is_a_bad_request()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopMoveIcon, new JsonObject { ["id"] = "sc:a" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task The_menu_bar_may_not_call_desktop_calls()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var bar = rig.H.AddSurface(SurfaceKind.Menubar, "m1");
        Kit.Code(await rig.Call(bar, Msg.DesktopMoveIcon, new DesktopMoveParams { Id = "sc:a", X = 1, Y = 1 })).Should().Be("forbidden");
        Kit.Code(await rig.Call(bar, Msg.DesktopAddWidget, new AddWidgetParams { Type = WidgetType.Clock })).Should().Be("forbidden");
    }

    // ---- desktop.open

    [Theory]
    [InlineData(@"C:\Apps\tool.exe")]
    [InlineData(@"C:\Users\me\Desktop\Editor.lnk")]
    public async Task Open_launches_programs_through_the_launcher(string target)
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Probe.Files.Add(target);
            r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0, target: target) } });
        });
        await rig.Ok<object?>(rig.AddDesktop("m1"), Msg.DesktopOpen, new IdParams { Id = "sc:a" });
        rig.Launcher.Launched.Should().ContainSingle().Which.Target.Should().Be(LaunchTarget.FromPath(target));
        rig.Launcher.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_uses_the_shell_for_documents_and_folders()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Probe.Files.Add(@"C:\Docs\report.pdf");
            r.Probe.Directories.Add(@"C:\Projects");
            r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:doc", 0, 0, target: @"C:\Docs\report.pdf"), DesktopRig.Shortcut("sc:dir", 1, 0, target: @"C:\Projects") } });
        });
        var s = rig.AddDesktop("m1");
        await rig.Ok<object?>(s, Msg.DesktopOpen, new IdParams { Id = "sc:doc" });
        await rig.Ok<object?>(s, Msg.DesktopOpen, new IdParams { Id = "sc:dir" });
        rig.Launcher.Opened.Should().Equal(@"C:\Docs\report.pdf", @"C:\Projects");
        rig.Launcher.Launched.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_of_a_shortcut_whose_target_vanished_is_not_found()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0, target: @"C:\gone.exe") } }));
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopOpen, new IdParams { Id = "sc:a" })).Should().Be("notFound");
        rig.Launcher.Launched.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_refuses_a_hand_edited_unc_target()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0, target: @"\\evil\share\x.exe") } }));
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopOpen, new IdParams { Id = "sc:a" })).Should().Be("failed");
        rig.Launcher.Launched.Should().BeEmpty();
        rig.Launcher.Opened.Should().BeEmpty();
    }

    [Fact]
    public async Task Open_of_an_unknown_id_is_not_found()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        Kit.Code(await rig.Call(s, Msg.DesktopOpen, new IdParams { Id = "sc:nope" })).Should().Be("notFound");
        Kit.Code(await rig.Call(s, Msg.DesktopOpen, new IdParams { Id = "fs:c:\\nope" })).Should().Be("notFound");
        Kit.Code(await rig.Call(s, Msg.DesktopOpen, new IdParams { Id = "" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Open_a_special_icon_opens_only_the_allowed_shell_literal_whatever_the_source_says()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Folder.Entries.Add(new DesktopEntry("special:thisPc", "This PC", "shell:Evil", false, true));
            r.Folder.Entries.Add(new DesktopEntry("special:recycleBin", "Recycle Bin", "shell:RecycleBinFolder", false, true));
            r.Configure(d => d);
        });
        var s = rig.AddDesktop("m1");
        await rig.Ok<object?>(s, Msg.DesktopOpen, new IdParams { Id = "special:thisPc" });
        await rig.Ok<object?>(s, Msg.DesktopOpen, new IdParams { Id = "special:recycleBin" });
        rig.Launcher.Opened.Should().Equal("shell:MyComputerFolder", "shell:RecycleBinFolder");
    }

    [Fact]
    public async Task Open_a_desktop_folder_file_or_program()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Folder.Entries.Add(new DesktopEntry(@"fs:c:\users\me\desktop\a.txt", "a.txt", @"C:\Users\me\Desktop\a.txt", false, false));
            r.Folder.Entries.Add(new DesktopEntry(@"fs:c:\users\me\desktop\app.lnk", "app", @"C:\Users\me\Desktop\app.lnk", false, false));
            r.Folder.Entries.Add(new DesktopEntry(@"fs:c:\users\me\desktop\dir", "dir", @"C:\Users\me\Desktop\dir", true, false));
            r.Configure(d => d);
        });
        var s = rig.AddDesktop("m1");
        await rig.Ok<object?>(s, Msg.DesktopOpen, new IdParams { Id = @"fs:c:\users\me\desktop\a.txt" });
        await rig.Ok<object?>(s, Msg.DesktopOpen, new IdParams { Id = @"fs:c:\users\me\desktop\app.lnk" });
        await rig.Ok<object?>(s, Msg.DesktopOpen, new IdParams { Id = @"fs:c:\users\me\desktop\dir" });
        rig.Launcher.Opened.Should().Equal(@"C:\Users\me\Desktop\a.txt", @"C:\Users\me\Desktop\dir");
        rig.Launcher.Launched.Should().ContainSingle().Which.Target.Path.Should().Be(@"C:\Users\me\Desktop\app.lnk");
    }

    [Fact]
    public async Task Open_reports_a_launcher_failure()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Probe.Files.Add(@"C:\Apps\a.exe");
            r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0) } });
        });
        rig.Launcher.Result = LaunchResult.Failed;
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopOpen, new IdParams { Id = "sc:a" })).Should().Be("failed");
        rig.Launcher.Result = LaunchResult.NotFound;
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopOpen, new IdParams { Id = "sc:a" })).Should().Be("notFound");
    }

    // ---- desktop.addShortcut

    [Theory]
    [InlineData(@"relative\path.exe")]
    [InlineData(@"\\server\share\x.exe")]
    [InlineData(@"C:\a\..\..\x.exe")]
    [InlineData(@"C:\a\b.exe:stream")]
    [InlineData("")]
    public async Task Add_shortcut_rejects_paths_that_are_not_clean_local_absolute_paths(string path)
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopAddShortcut, new AddShortcutParams { Path = path })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Add_shortcut_refuses_a_path_the_user_never_picked()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(@"C:\Apps\a.exe"); r.Configure(d => d); });
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopAddShortcut, new AddShortcutParams { Path = @"C:\Apps\a.exe" })).Should().Be("forbidden");
        rig.Settings.Current.Desktop.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public async Task Add_shortcut_of_a_granted_but_missing_path_is_not_found()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        rig.Grants.Grant(@"C:\Apps\a.exe");
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopAddShortcut, new AddShortcutParams { Path = @"C:\Apps\a.exe" })).Should().Be("notFound");
    }

    [Fact]
    public async Task Add_shortcut_stores_it_places_it_and_returns_the_icon()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(@"C:\Apps\Editor.exe"); r.Configure(d => d with { IconSize = 80 }); });
        rig.Grants.Grant(@"C:\Apps");
        var icon = await rig.Ok<DesktopIcon>(rig.AddDesktop("m1"), Msg.DesktopAddShortcut, new AddShortcutParams { Path = "C:/Apps/Editor.exe" });
        icon.Id.Should().StartWith("sc:");
        icon.Name.Should().Be("Editor");
        icon.Target.Should().Be(@"C:\Apps\Editor.exe");
        icon.IconUrl.Should().Be(DynUrls.PathIcon(@"C:\Apps\Editor.exe", 80));
        icon.Kind.Should().Be(DesktopIconKind.Shortcut);
        (icon.X, icon.Y).Should().Be((0, 0));
        var stored = rig.Settings.Current.Desktop.Shortcuts.Single();
        stored.Should().Match<DesktopShortcut>(s => s.Id == icon.Id && s.Target == @"C:\Apps\Editor.exe" && s.MonitorId == null);
    }

    [Fact]
    public async Task Add_shortcut_gives_each_one_a_unique_id_and_the_next_free_cell()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(@"C:\A\a.txt"); r.Probe.Files.Add(@"C:\A\b.txt"); r.Configure(d => d); });
        rig.Grants.Grant(@"C:\A");
        var s = rig.AddDesktop("m1");
        var a = await rig.Ok<DesktopIcon>(s, Msg.DesktopAddShortcut, new AddShortcutParams { Path = @"C:\A\a.txt" });
        var b = await rig.Ok<DesktopIcon>(s, Msg.DesktopAddShortcut, new AddShortcutParams { Path = @"C:\A\b.txt" });
        a.Id.Should().NotBe(b.Id);
        (a.X, a.Y).Should().Be((0, 0));
        (b.X, b.Y).Should().Be((0, 1));
        a.Name.Should().Be("a.txt"); // documents keep their extension, programs lose it
    }

    [Fact]
    public async Task Add_shortcut_honours_explicit_coordinates_and_a_target_monitor()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Monitors.Monitors.Add(Kit.Monitor("m2", 1920, 0, 1920, 1080));
            r.Probe.Directories.Add(@"C:\Projects");
            r.Configure(d => d);
        });
        rig.Grants.Grant(@"C:\Projects");
        var icon = await rig.Ok<DesktopIcon>(rig.AddDesktop("m1"), Msg.DesktopAddShortcut, new AddShortcutParams { Path = @"C:\Projects", X = 5, Y = 3, MonitorId = "m2" });
        (icon.X, icon.Y).Should().Be((5, 3));
        icon.Name.Should().Be("Projects");
        rig.Settings.Current.Desktop.Shortcuts.Single().MonitorId.Should().Be("m2");
        var d2 = rig.AddDesktop("m2");
        rig.State(d2)!.Icons.Should().ContainSingle();
    }

    [Fact]
    public async Task Add_shortcut_with_an_unknown_monitor_is_a_bad_request()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(@"C:\A\a.txt"); r.Configure(d => d); });
        rig.Grants.Grant(@"C:\A");
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopAddShortcut, new AddShortcutParams { Path = @"C:\A\a.txt", MonitorId = "m99" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Add_shortcut_fails_cleanly_at_the_shortcut_limit()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Probe.Files.Add(@"C:\A\a.txt");
            var many = Enumerable.Range(0, 500).Select(i => DesktopRig.Shortcut("sc:" + i, 0, 0)).ToArray();
            r.Configure(d => d with { Shortcuts = many });
        });
        rig.Grants.Grant(@"C:\A");
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopAddShortcut, new AddShortcutParams { Path = @"C:\A\a.txt" })).Should().Be("failed");
        rig.Settings.Current.Desktop.Shortcuts.Should().HaveCount(500);
    }

    // ---- desktop.removeShortcut

    [Fact]
    public async Task Remove_shortcut_deletes_only_the_shortcut()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0), DesktopRig.Shortcut("sc:b", 1, 0) } }));
        var s = rig.AddDesktop("m1");
        await rig.Ok<object?>(s, Msg.DesktopRemoveShortcut, new IdParams { Id = "sc:a" });
        rig.Settings.Current.Desktop.Shortcuts.Select(x => x.Id).Should().Equal("sc:b");
        Kit.Code(await rig.Call(s, Msg.DesktopRemoveShortcut, new IdParams { Id = "sc:a" })).Should().Be("notFound");
    }

    [Fact]
    public async Task Remove_shortcut_never_removes_desktop_folder_items()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Folder.Entries.Add(new DesktopEntry("fs:c:\\a", "a", @"C:\a", false, false)); r.Configure(d => d); });
        var s = rig.AddDesktop("m1");
        Kit.Code(await rig.Call(s, Msg.DesktopRemoveShortcut, new IdParams { Id = "fs:c:\\a" })).Should().Be("badRequest");
        Kit.Code(await rig.Call(s, Msg.DesktopRemoveShortcut, new IdParams { Id = "special:thisPc" })).Should().Be("badRequest");
    }

    // ---- desktop.setWallpaper

    [Theory]
    [InlineData(@"C:\Pics\notes.txt")]
    [InlineData(@"C:\Pics\script.exe")]
    [InlineData(@"C:\Pics\noextension")]
    public async Task Set_wallpaper_requires_an_image_extension(string path)
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(path); r.Configure(d => d); });
        rig.Grants.Grant(@"C:\Pics");
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = path })).Should().Be("badRequest");
        rig.Wallpapers.Set.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_wallpaper_requires_a_granted_existing_file()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var s = rig.AddDesktop("m1");
        Kit.Code(await rig.Call(s, Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = @"C:\Pics\a.png" })).Should().Be("forbidden");
        rig.Grants.Grant(@"C:\Pics");
        Kit.Code(await rig.Call(s, Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = @"C:\Pics\a.png" })).Should().Be("notFound");
        Kit.Code(await rig.Call(s, Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = @"\\server\pics\a.png" })).Should().Be("badRequest");
        rig.Wallpapers.Set.Should().BeEmpty();
    }

    [Theory]
    [InlineData(@"C:\Pics\a.PNG")]
    [InlineData(@"C:\Pics\a.jpeg")]
    [InlineData(@"C:\Pics\a.webp")]
    public async Task Set_wallpaper_for_all_monitors_applies_it_and_remembers_it(string path)
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Probe.Files.Add(path);
            r.Configure(d => d with { Wallpapers = new Dictionary<string, string> { ["m1"] = @"C:\Old\x.png" } });
        });
        rig.Grants.Grant(@"C:\Pics");
        await rig.Ok<object?>(rig.AddDesktop("m1"), Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = path });
        rig.Wallpapers.Set.Should().ContainSingle().Which.Should().Be((path, (string?)null));
        rig.Settings.Current.Desktop.WallpaperPath.Should().Be(path);
        rig.Settings.Current.Desktop.Wallpapers.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_wallpaper_for_one_monitor_leaves_the_others_alone()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Monitors.Monitors.Add(Kit.Monitor("m2", 1920, 0, 1920, 1080));
            r.Probe.Files.Add(@"C:\Pics\b.png");
            r.Configure(d => d with { WallpaperPath = @"C:\Pics\default.png" });
        });
        rig.Grants.Grant(@"C:\Pics");
        await rig.Ok<object?>(rig.AddDesktop("m1"), Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = @"C:\Pics\b.png", MonitorId = "m2" });
        rig.Wallpapers.Set.Should().ContainSingle().Which.Should().Be((@"C:\Pics\b.png", (string?)"m2"));
        rig.Settings.Current.Desktop.WallpaperPath.Should().Be(@"C:\Pics\default.png");
        rig.Settings.Current.Desktop.Wallpapers["m2"].Should().Be(@"C:\Pics\b.png");
    }

    [Fact]
    public async Task Set_wallpaper_for_an_unknown_monitor_is_a_bad_request()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(@"C:\Pics\b.png"); r.Configure(d => d); });
        rig.Grants.Grant(@"C:\Pics");
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = @"C:\Pics\b.png", MonitorId = "m99" })).Should().Be("badRequest");
        rig.Wallpapers.Set.Should().BeEmpty();
    }

    [Fact]
    public async Task Set_wallpaper_busts_the_url_even_for_the_same_path()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(@"C:\Pics\a.png"); r.Configure(d => d with { WallpaperPath = @"C:\Pics\a.png" }); });
        rig.Grants.Grant(@"C:\Pics");
        var s = rig.AddDesktop("m1");
        var before = rig.State(s)!.WallpaperUrl;
        await rig.Ok<object?>(s, Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = @"C:\Pics\a.png" });
        rig.State(s)!.WallpaperUrl.Should().NotBe(before);
    }

    [Fact]
    public async Task Set_wallpaper_reports_a_refusal_of_the_wallpaper_service_and_keeps_the_settings()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Probe.Files.Add(@"C:\Pics\a.png"); r.Configure(d => d); }, new ThrowingWallpaper());
        rig.Grants.Grant(@"C:\Pics");
        Kit.Code(await rig.Call(rig.AddDesktop("m1"), Msg.DesktopSetWallpaper, new SetWallpaperParams { Path = @"C:\Pics\a.png" })).Should().Be("failed");
        rig.Settings.Current.Desktop.WallpaperPath.Should().BeNull();
    }

    internal sealed class ThrowingWallpaper : IWallpaperService
    {
        public string? GetWallpaperPath(string monitorId) => null;
        public void SetWallpaper(string path, string? monitorId) => throw new InvalidOperationException("COM said no");
    }
}
