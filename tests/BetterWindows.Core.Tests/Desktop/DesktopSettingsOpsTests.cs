using BetterWindows.Core.Desktop;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Desktop;

public class DesktopSettingsOpsTests
{
    private static readonly MonitorInfo M1 = Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true, workBottom: 40);
    private static readonly MonitorInfo M2 = Kit.Monitor("m2", 1920, 0, 1920, 1080);
    private static readonly IReadOnlyList<MonitorInfo> Both = new[] { M1, M2 };
    private static readonly IReadOnlyList<DesktopEntry> None = Array.Empty<DesktopEntry>();

    private static Settings With(Func<DesktopSettings, DesktopSettings> f) => new() { Desktop = f(new DesktopSettings { Enabled = true }) };
    private static DesktopShortcut Sc(string id, int x, int y, string? monitor = null) => new() { Id = id, Name = id, Target = @"C:\x\" + id, X = x, Y = y, MonitorId = monitor };
    private static DesktopEntry Entry(string id, string name = "item") => new(id, name, @"C:\Users\me\Desktop\" + name, false, false);

    // ---- MoveIcon

    [Fact]
    public void Moving_to_a_free_cell_changes_only_that_shortcut()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0), Sc("sc:b", 1, 0) } });
        var (next, found) = DesktopSettingsOps.MoveIcon(s, "sc:a", new GridCell(5, 4), None, Both);
        found.Should().BeTrue();
        next.Desktop.Shortcuts.Single(x => x.Id == "sc:a").Should().Match<DesktopShortcut>(x => x.X == 5 && x.Y == 4);
        next.Desktop.Shortcuts.Single(x => x.Id == "sc:b").Should().Match<DesktopShortcut>(x => x.X == 1 && x.Y == 0);
    }

    [Fact]
    public void Moving_onto_an_occupied_cell_swaps_the_two_icons()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0), Sc("sc:b", 2, 3) } });
        var (next, _) = DesktopSettingsOps.MoveIcon(s, "sc:a", new GridCell(2, 3), None, Both);
        next.Desktop.Shortcuts.Single(x => x.Id == "sc:a").Should().Match<DesktopShortcut>(x => x.X == 2 && x.Y == 3);
        next.Desktop.Shortcuts.Single(x => x.Id == "sc:b").Should().Match<DesktopShortcut>(x => x.X == 0 && x.Y == 0);
    }

    [Fact]
    public void Moving_outside_the_grid_clamps_to_the_edge_cell()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0) } });
        var (next, _) = DesktopSettingsOps.MoveIcon(s, "sc:a", new GridCell(-4, 9999), None, Both);
        var grid = DesktopLayout.GridFor(M1, 64);
        next.Desktop.Shortcuts.Single().Should().Match<DesktopShortcut>(x => x.X == 0 && x.Y == grid.Rows - 1);
    }

    [Fact]
    public void Moving_to_the_current_cell_changes_nothing()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 2, 2) } });
        var (next, found) = DesktopSettingsOps.MoveIcon(s, "sc:a", new GridCell(2, 2), None, Both);
        found.Should().BeTrue();
        next.Should().BeSameAs(s);
    }

    [Fact]
    public void Moving_an_unknown_icon_reports_not_found()
    {
        var (next, found) = DesktopSettingsOps.MoveIcon(With(d => d), "sc:nope", new GridCell(1, 1), None, Both);
        found.Should().BeFalse();
        next.Desktop.Shortcuts.Should().BeEmpty();
    }

    [Fact]
    public void A_desktop_folder_item_is_moved_through_placements_and_other_new_items_are_frozen_where_they_were()
    {
        var s = With(d => d);
        var entries = new[] { Entry("fs:a", "a"), Entry("fs:b", "b") };
        var (next, found) = DesktopSettingsOps.MoveIcon(s, "fs:a", new GridCell(6, 2), entries, Both);
        found.Should().BeTrue();
        next.Desktop.Placements["fs:a"].Should().Match<DesktopCell>(c => c.X == 6 && c.Y == 2);
        // b was auto-placed at (0,1) after a was at (0,0); moving a away must not shift b
        next.Desktop.Placements["fs:b"].Should().Match<DesktopCell>(c => c.X == 0 && c.Y == 1);
    }

    [Fact]
    public void A_folder_item_can_swap_with_a_shortcut()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 3, 3) } });
        var (next, _) = DesktopSettingsOps.MoveIcon(s, "fs:a", new GridCell(3, 3), new[] { Entry("fs:a") }, Both);
        next.Desktop.Shortcuts.Single().Should().Match<DesktopShortcut>(x => x.X == 0 && x.Y == 0); // took the folder item's old cell
        next.Desktop.Placements["fs:a"].Should().Match<DesktopCell>(c => c.X == 3 && c.Y == 3);
    }

    [Fact]
    public void Folder_items_are_not_movable_when_the_desktop_folder_is_hidden()
    {
        var s = With(d => d with { ShowDesktopFolder = false });
        DesktopSettingsOps.MoveIcon(s, "fs:a", new GridCell(1, 1), new[] { Entry("fs:a") }, Both).Found.Should().BeFalse();
    }

    // ---- AddShortcut

    [Fact]
    public void A_new_shortcut_takes_the_first_free_cell_columns_first()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0), Sc("sc:b", 0, 1) } });
        var r = DesktopSettingsOps.AddShortcut(s, None, Both, M1, null, "sc:new", "New", @"C:\x\new.exe", null);
        r.Failure.Should().Be(AddShortcutFailure.None);
        r.Icon.Should().Match<DesktopIcon>(i => i.X == 0 && i.Y == 2 && i.Id == "sc:new" && i.Kind == DesktopIconKind.Shortcut);
        r.Settings.Desktop.Shortcuts.Should().HaveCount(3);
        r.Icon!.IconUrl.Should().Be(DynUrls.PathIcon(@"C:\x\new.exe", 64));
    }

    [Fact]
    public void An_explicit_free_cell_is_honoured_an_occupied_one_goes_to_the_nearest_free_cell()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 4, 4) } });
        DesktopSettingsOps.AddShortcut(s, None, Both, M1, null, "sc:n1", "n", @"C:\x\n", new GridCell(6, 1)).Icon!
            .Should().Match<DesktopIcon>(i => i.X == 6 && i.Y == 1);
        var near = DesktopSettingsOps.AddShortcut(s, None, Both, M1, null, "sc:n2", "n", @"C:\x\n", new GridCell(4, 4)).Icon!;
        (near.X, near.Y).Should().NotBe((4, 4));
        Math.Abs(near.X - 4).Should().BeLessThanOrEqualTo(1);
        Math.Abs(near.Y - 4).Should().BeLessThanOrEqualTo(1);
    }

    [Fact]
    public void A_shortcut_for_another_monitor_is_stored_with_that_monitor_and_placed_on_its_own_grid()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0) } });
        var r = DesktopSettingsOps.AddShortcut(s, None, Both, M2, "m2", "sc:b", "b", @"C:\x\b", null);
        r.Icon!.Should().Match<DesktopIcon>(i => i.X == 0 && i.Y == 0); // m2 is empty: first cell
        r.Settings.Desktop.Shortcuts.Last().MonitorId.Should().Be("m2");
    }

    [Fact]
    public void New_desktop_folder_placements_are_frozen_when_a_shortcut_is_added()
    {
        var s = With(d => d);
        var r = DesktopSettingsOps.AddShortcut(s, new[] { Entry("fs:a") }, Both, M1, null, "sc:n", "n", @"C:\x\n", null);
        r.Settings.Desktop.Placements.Should().ContainKey("fs:a");
        r.Icon!.Should().Match<DesktopIcon>(i => i.X == 0 && i.Y == 1); // the folder item holds (0,0)
    }

    [Fact]
    public void The_shortcut_limit_is_enforced()
    {
        var many = Enumerable.Range(0, DesktopSettingsOps.MaxShortcuts).Select(i => Sc("sc:" + i, 0, 0)).ToArray();
        var r = DesktopSettingsOps.AddShortcut(With(d => d with { Shortcuts = many }), None, Both, M1, null, "sc:x", "x", @"C:\x", null);
        r.Failure.Should().Be(AddShortcutFailure.TooMany);
        r.Icon.Should().BeNull();
    }

    [Fact]
    public void A_full_desktop_refuses_a_new_shortcut()
    {
        var tiny = Kit.Monitor("m1", 0, 0, 120, 160, primary: true);
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0) } });
        DesktopSettingsOps.AddShortcut(s, None, new[] { tiny }, tiny, null, "sc:x", "x", @"C:\x", null).Failure.Should().Be(AddShortcutFailure.DesktopFull);
    }

    // ---- RemoveShortcut

    [Fact]
    public void Removing_a_shortcut_drops_it_and_reports_found()
    {
        var s = With(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0), Sc("sc:b", 1, 0) } });
        var (next, found) = DesktopSettingsOps.RemoveShortcut(s, "sc:a");
        found.Should().BeTrue();
        next.Desktop.Shortcuts.Select(x => x.Id).Should().Equal("sc:b");
        DesktopSettingsOps.RemoveShortcut(next, "sc:a").Found.Should().BeFalse();
    }

    // ---- ReconcilePlacements

    [Fact]
    public void Reconcile_persists_the_cells_of_new_desktop_folder_items()
    {
        var s = With(d => d);
        var next = DesktopSettingsOps.ReconcilePlacements(s, new[] { Entry("fs:a"), Entry("fs:b") }, Both);
        next.Desktop.Placements.Keys.Should().BeEquivalentTo("fs:a", "fs:b");
        next.Desktop.Placements["fs:b"].Should().Match<DesktopCell>(c => c.X == 0 && c.Y == 1);
    }

    [Fact]
    public void Reconcile_is_a_fixed_point_returning_the_same_instance()
    {
        var entries = new[] { Entry("fs:a") };
        var once = DesktopSettingsOps.ReconcilePlacements(With(d => d), entries, Both);
        DesktopSettingsOps.ReconcilePlacements(once, entries, Both).Should().BeSameAs(once);
    }

    [Fact]
    public void Reconcile_prunes_placements_of_items_that_disappeared()
    {
        var s = With(d => d with { Placements = new Dictionary<string, DesktopCell> { ["fs:gone"] = new() { X = 1, Y = 1 }, ["fs:a"] = new() { X = 2, Y = 2 } } });
        var next = DesktopSettingsOps.ReconcilePlacements(s, new[] { Entry("fs:a") }, Both);
        next.Desktop.Placements.Keys.Should().BeEquivalentTo("fs:a");
    }

    [Fact]
    public void Reconcile_never_prunes_on_an_empty_enumeration()
    {
        var s = With(d => d with { Placements = new Dictionary<string, DesktopCell> { ["fs:a"] = new() { X = 2, Y = 2 } } });
        DesktopSettingsOps.ReconcilePlacements(s, None, Both).Should().BeSameAs(s);
    }

    [Fact]
    public void Reconcile_does_nothing_while_the_layer_or_the_desktop_folder_is_off()
    {
        var entries = new[] { Entry("fs:a") };
        var off = new Settings { Desktop = new DesktopSettings { Enabled = false } };
        DesktopSettingsOps.ReconcilePlacements(off, entries, Both).Should().BeSameAs(off);
        var noFolder = With(d => d with { ShowDesktopFolder = false });
        DesktopSettingsOps.ReconcilePlacements(noFolder, entries, Both).Should().BeSameAs(noFolder);
    }

    // ---- SetWallpaper

    [Fact]
    public void Setting_the_wallpaper_for_all_monitors_sets_the_default_and_clears_overrides()
    {
        var s = With(d => d with { Wallpapers = new Dictionary<string, string> { ["m2"] = @"C:\old.png" } });
        var next = DesktopSettingsOps.SetWallpaper(s, @"C:\new.png", null);
        next.Desktop.WallpaperPath.Should().Be(@"C:\new.png");
        next.Desktop.Wallpapers.Should().BeEmpty();
    }

    [Fact]
    public void Setting_the_wallpaper_for_one_monitor_only_touches_its_override()
    {
        var s = With(d => d with { WallpaperPath = @"C:\default.png" });
        var next = DesktopSettingsOps.SetWallpaper(s, @"C:\m2.png", "m2");
        next.Desktop.WallpaperPath.Should().Be(@"C:\default.png");
        next.Desktop.Wallpapers["m2"].Should().Be(@"C:\m2.png");
        DesktopSettingsOps.WallpaperOverride(next.Desktop, "m2").Should().Be(@"C:\m2.png");
        DesktopSettingsOps.WallpaperOverride(next.Desktop, "m1").Should().Be(@"C:\default.png");
    }

    [Fact]
    public void The_wallpaper_override_map_is_capped_without_refusing_the_new_choice()
    {
        var map = Enumerable.Range(0, DesktopSettingsOps.MaxWallpapers).ToDictionary(i => "old" + i, i => @"C:\o" + i + ".png");
        var next = DesktopSettingsOps.SetWallpaper(With(d => d with { Wallpapers = map }), @"C:\mine.png", "mine");
        next.Desktop.Wallpapers.Should().HaveCount(DesktopSettingsOps.MaxWallpapers);
        next.Desktop.Wallpapers["mine"].Should().Be(@"C:\mine.png");
    }

    [Fact]
    public void Without_any_setting_there_is_no_wallpaper_override()
    {
        DesktopSettingsOps.WallpaperOverride(new DesktopSettings(), "m1").Should().BeNull();
    }
}
