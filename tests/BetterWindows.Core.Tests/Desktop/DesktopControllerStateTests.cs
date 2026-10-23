using BetterWindows.Core.Controllers;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Desktop;

public class DesktopControllerStateTests
{
    private static MonitorInfo[] TwoMonitors() => new[]
    {
        Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true, workTop: 28),
        Kit.Monitor("m2", 1920, 0, 1920, 1080),
    };

    private static void UseTwoMonitors(DesktopRig r) { r.Monitors.Monitors.Clear(); r.Monitors.Monitors.AddRange(TwoMonitors()); }

    [Fact]
    public async Task A_disabled_desktop_publishes_a_minimal_disabled_state()
    {
        using var rig = await DesktopRig.StartAsync();
        var s = rig.AddDesktop("m1");
        var st = rig.State(s)!;
        st.Enabled.Should().BeFalse();
        st.MonitorId.Should().Be("m1");
        st.Icons.Should().BeEmpty();
        st.Widgets.Should().BeEmpty();
        st.WallpaperUrl.Should().BeNull();
    }

    [Fact]
    public async Task The_state_carries_the_icons_of_the_monitor_with_icon_urls_and_kinds()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { IconSize = 96, Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 2, 1, target: @"C:\Apps\a.exe") } }));
        var st = rig.State(rig.AddDesktop("m1"))!;
        st.Enabled.Should().BeTrue();
        st.IconSize.Should().Be(96);
        var icon = st.Icons.Should().ContainSingle().Subject;
        icon.Should().Match<DesktopIcon>(i => i.Id == "sc:a" && i.X == 2 && i.Y == 1 && i.Kind == DesktopIconKind.Shortcut && i.Target == @"C:\Apps\a.exe");
        icon.IconUrl.Should().Be(DynUrls.PathIcon(@"C:\Apps\a.exe", 96));
    }

    [Fact]
    public async Task Each_monitor_surface_gets_its_own_snapshot()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            UseTwoMonitors(r);
            r.Configure(d => d with
            {
                Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0), DesktopRig.Shortcut("sc:b", 0, 0, "m2") },
                Widgets = new[] { new WidgetInstance { Id = "w1", Type = WidgetType.Clock, Size = WidgetSize.Small, X = 96, Y = 96 }, new WidgetInstance { Id = "w2", Type = WidgetType.Clock, Size = WidgetSize.Small, X = 96, Y = 96, MonitorId = "m2" } },
            });
        });
        var d1 = rig.AddDesktop("m1");
        var d2 = rig.AddDesktop("m2");
        var s1 = rig.State(d1)!;
        var s2 = rig.State(d2)!;
        s1.MonitorId.Should().Be("m1");
        s2.MonitorId.Should().Be("m2");
        s1.Icons.Select(i => i.Id).Should().Equal("sc:a");
        s2.Icons.Select(i => i.Id).Should().Equal("sc:b");
        s1.Widgets.Select(w => w.Id).Should().Equal("w1");
        s2.Widgets.Select(w => w.Id).Should().Equal("w2");
    }

    [Fact]
    public async Task A_surface_for_an_unknown_monitor_receives_nothing()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var ghost = rig.AddDesktop("m99");
        rig.State(ghost).Should().BeNull();
    }

    [Fact]
    public async Task A_surface_without_a_monitor_id_is_the_primary_monitor()
    {
        using var rig = await DesktopRig.StartAsync(r => { UseTwoMonitors(r); r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:a", 0, 0) } }); });
        rig.State(rig.AddDesktop(null))!.MonitorId.Should().Be("m1");
    }

    [Fact]
    public async Task Wallpapers_are_per_monitor_with_the_default_as_fallback()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            UseTwoMonitors(r);
            r.Configure(d => d with { WallpaperPath = @"C:\Pics\default.png", Wallpapers = new Dictionary<string, string> { ["m2"] = @"C:\Pics\second.png" } });
        });
        var u1 = rig.State(rig.AddDesktop("m1"))!.WallpaperUrl;
        var u2 = rig.State(rig.AddDesktop("m2"))!.WallpaperUrl;
        u1.Should().StartWith("/dyn/wallpaper/m1?rev=");
        u2.Should().StartWith("/dyn/wallpaper/m2?rev=");
    }

    [Fact]
    public async Task Without_any_wallpaper_the_url_is_absent_and_the_system_wallpaper_fills_in()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        rig.State(rig.AddDesktop("m1"))!.WallpaperUrl.Should().BeNull();

        using var rig2 = await DesktopRig.StartAsync(r => { r.Wallpapers.Paths["m1"] = @"C:\Windows\Web\wall.jpg"; r.Configure(d => d); });
        rig2.State(rig2.AddDesktop("m1"))!.WallpaperUrl.Should().StartWith("/dyn/wallpaper/m1?rev=");
    }

    [Fact]
    public async Task Changing_the_wallpaper_setting_changes_the_url_revision()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { WallpaperPath = @"C:\Pics\a.png" }));
        var surface = rig.AddDesktop("m1");
        var before = rig.State(surface)!.WallpaperUrl;
        rig.Settings.Set(s => Kit.WithDesktop(s, d => d with { WallpaperPath = @"C:\Pics\b.png" }));
        var after = rig.State(surface)!.WallpaperUrl;
        after.Should().NotBe(before);
    }

    [Fact]
    public async Task The_wallpaper_fit_and_icon_size_follow_the_settings()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { WallpaperFit = WallpaperFit.Center, IconSize = 48 }));
        var st = rig.State(rig.AddDesktop("m1"))!;
        st.WallpaperFit.Should().Be(WallpaperFit.Center);
        st.IconSize.Should().Be(48);
    }

    [Fact]
    public async Task Desktop_folder_items_are_merged_into_the_primary_monitor_and_their_cells_are_persisted()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            UseTwoMonitors(r);
            r.Folder.Entries.Add(new DesktopEntry("special:thisPc", "This PC", "shell:MyComputerFolder", false, true));
            r.Folder.Entries.Add(new DesktopEntry(@"fs:c:\users\me\desktop\notes.txt", "notes.txt", @"C:\Users\me\Desktop\notes.txt", false, false));
            r.Configure(d => d);
        });
        var s1 = rig.State(rig.AddDesktop("m1"))!;
        var s2 = rig.State(rig.AddDesktop("m2"))!;
        s1.Icons.Select(i => i.Id).Should().Equal("special:thisPc", @"fs:c:\users\me\desktop\notes.txt");
        s1.Icons.Select(i => i.Kind).Should().Equal(DesktopIconKind.Special, DesktopIconKind.File);
        s2.Icons.Should().BeEmpty();
        await Kit.Eventually(() => rig.Settings.Current.Desktop.Placements.Count == 2);
        rig.Settings.Current.Desktop.Placements["special:thisPc"].Should().Match<DesktopCell>(c => c.X == 0 && c.Y == 0);
    }

    [Fact]
    public async Task Hiding_the_desktop_folder_removes_its_items_from_the_state()
    {
        using var rig = await DesktopRig.StartAsync(r => { r.Folder.Entries.Add(new DesktopEntry("fs:c:\\x", "x", @"C:\x", false, false)); r.Configure(d => d); });
        var surface = rig.AddDesktop("m1");
        rig.State(surface)!.Icons.Should().HaveCount(1);
        rig.Settings.Set(s => Kit.WithDesktop(s, d => d with { ShowDesktopFolder = false }));
        rig.State(surface)!.Icons.Should().BeEmpty();
    }

    [Fact]
    public async Task A_change_in_the_desktop_folder_is_pushed_to_the_surface()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var surface = rig.AddDesktop("m1");
        rig.State(surface)!.Icons.Should().BeEmpty();
        rig.Folder.Entries.Add(new DesktopEntry(@"fs:c:\new.txt", "new.txt", @"C:\new.txt", false, false));
        rig.Folder.RaiseChanged();
        await Kit.Eventually(() => rig.State(surface)!.Icons.Count == 1);
    }

    [Fact]
    public async Task A_removed_desktop_file_loses_its_stored_placement()
    {
        using var rig = await DesktopRig.StartAsync(r =>
        {
            r.Folder.Entries.Add(new DesktopEntry("fs:c:\\a", "a", @"C:\a", false, false));
            r.Folder.Entries.Add(new DesktopEntry("fs:c:\\b", "b", @"C:\b", false, false));
            r.Configure(d => d);
        });
        await Kit.Eventually(() => rig.Settings.Current.Desktop.Placements.Count == 2);
        rig.Folder.Entries.RemoveAt(0);
        rig.Folder.RaiseChanged();
        await Kit.Eventually(() => rig.Settings.Current.Desktop.Placements.Keys.SequenceEqual(new[] { "fs:c:\\b" }));
    }

    [Fact]
    public async Task Changing_the_icon_size_republishes_the_state()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d));
        var surface = rig.AddDesktop("m1");
        rig.State(surface)!.IconSize.Should().Be(64);
        rig.Settings.Set(s => Kit.WithDesktop(s, d => d with { IconSize = 80 }));
        rig.State(surface)!.IconSize.Should().Be(80);
    }

    [Fact]
    public async Task Widgets_are_kept_inside_the_work_area_when_they_are_displayed()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with
        {
            Widgets = new[] { new WidgetInstance { Id = "w", Type = WidgetType.Clock, Size = WidgetSize.Small, X = 99999, Y = 99999 } },
        }));
        var w = rig.State(rig.AddDesktop("m1"))!.Widgets.Single();
        w.X.Should().BeLessThanOrEqualTo(1920 - 160);
        w.Y.Should().BeLessThanOrEqualTo(1040 - 160);
        rig.Settings.Current.Desktop.Widgets.Single().X.Should().Be(99999); // display-only: the stored value is not rewritten
    }

    [Fact]
    public async Task A_monitor_that_appears_later_gets_its_state()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { Shortcuts = new[] { DesktopRig.Shortcut("sc:b", 0, 0, "m2") } }));
        var d2 = rig.AddDesktop("m2");
        rig.State(d2).Should().BeNull();
        rig.Monitors.Monitors.Add(Kit.Monitor("m2", 1920, 0, 1920, 1080));
        rig.Monitors.RaiseChanged();
        await Kit.Eventually(() => { rig.Flush(); return d2.Events(Msg.DesktopState).Count > 0; });
        d2.LastEvent<DesktopState>(Msg.DesktopState)!.Icons.Select(i => i.Id).Should().Equal("sc:b");
    }

    // ---- native icons

    [Fact]
    public async Task An_enabled_layer_hides_the_native_icons()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { HideNativeIcons = true }));
        rig.NativeIcons.NativeIconsVisible.Should().BeFalse();
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task The_native_icons_stay_visible_unless_the_layer_is_enabled_and_asks_to_hide_them(bool hide, bool enabled)
    {
        using var rig = await DesktopRig.StartAsync(r => r.Settings.Set(s => Kit.WithDesktop(s, d => d with { Enabled = enabled, HideNativeIcons = hide })));
        rig.NativeIcons.NativeIconsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Toggling_the_setting_at_runtime_hides_and_shows_the_native_icons()
    {
        using var rig = await DesktopRig.StartAsync();
        rig.NativeIcons.NativeIconsVisible.Should().BeTrue();
        rig.Settings.Set(s => Kit.WithDesktop(s, d => d with { Enabled = true, HideNativeIcons = true }));
        await Kit.Eventually(() => rig.NativeIcons.NativeIconsVisible == false);
        rig.Settings.Set(s => Kit.WithDesktop(s, d => d with { HideNativeIcons = false }));
        await Kit.Eventually(() => rig.NativeIcons.NativeIconsVisible == true);
    }

    [Fact]
    public async Task Stopping_restores_the_native_icons()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { HideNativeIcons = true }));
        rig.NativeIcons.NativeIconsVisible.Should().BeFalse();
        rig.Controller.Stop();
        rig.NativeIcons.Restores.Should().Be(1);
        rig.NativeIcons.NativeIconsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task Settings_changes_after_stop_do_not_touch_the_native_icons_again()
    {
        using var rig = await DesktopRig.StartAsync(r => r.Configure(d => d with { HideNativeIcons = true }));
        rig.Controller.Stop();
        rig.Settings.Set(s => Kit.WithDesktop(s, d => d with { Enabled = true, HideNativeIcons = true }));
        await Task.Delay(50);
        rig.NativeIcons.NativeIconsVisible.Should().BeTrue();
    }

    [Fact]
    public async Task The_controller_registers_every_desktop_call()
    {
        using var rig = await DesktopRig.StartAsync();
        rig.H.Router.RegisteredTypes.Where(t => t.StartsWith("desktop.")).Should().BeEquivalentTo(
            Msg.DesktopMoveIcon, Msg.DesktopOpen, Msg.DesktopAddShortcut, Msg.DesktopRemoveShortcut, Msg.DesktopSetWallpaper,
            Msg.DesktopMoveWidget, Msg.DesktopAddWidget, Msg.DesktopRemoveWidget, Msg.DesktopUpdateWidget);
    }
}
