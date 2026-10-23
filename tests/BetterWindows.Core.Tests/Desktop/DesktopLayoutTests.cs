using BetterWindows.Core.Desktop;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Desktop;

public class DesktopLayoutTests
{
    private static readonly MonitorInfo M1 = Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true, workBottom: 40);
    private static readonly MonitorInfo M2 = Kit.Monitor("m2", 1920, 0, 1920, 1080);
    private static readonly IReadOnlyList<MonitorInfo> Both = new[] { M1, M2 };

    private static Settings Enabled(Func<DesktopSettings, DesktopSettings>? f = null) =>
        new() { Desktop = (f ?? (d => d))(new DesktopSettings { Enabled = true }) };

    private static DesktopShortcut Sc(string id, int x, int y, string? monitor = null) =>
        new() { Id = id, Name = id, Target = @"C:\x\" + id + ".exe", X = x, Y = y, MonitorId = monitor };

    private static DesktopEntry Entry(string id, string name = "item", bool folder = false, bool special = false) =>
        new(id, name, special ? "shell:MyComputerFolder" : @"C:\Users\me\Desktop\" + name, folder, special);

    [Fact]
    public void Shortcuts_keep_their_stored_cells()
    {
        var s = Enabled(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0), Sc("sc:b", 3, 2) } });
        var r = DesktopLayout.Resolve(s, Array.Empty<DesktopEntry>(), Both, M1);
        r.Icons.Select(i => (i.Id, i.Cell)).Should().Equal(("sc:a", new GridCell(0, 0)), ("sc:b", new GridCell(3, 2)));
        r.NewPlacements.Should().BeEmpty();
    }

    [Fact]
    public void A_shortcut_id_without_the_prefix_is_exposed_with_it()
    {
        var s = Enabled(d => d with { Shortcuts = new[] { Sc("abc", 0, 0) } });
        DesktopLayout.Resolve(s, Array.Empty<DesktopEntry>(), Both, M1).Icons.Single().Id.Should().Be("sc:abc");
    }

    [Fact]
    public void Two_shortcuts_on_one_cell_are_separated()
    {
        var s = Enabled(d => d with { Shortcuts = new[] { Sc("sc:a", 1, 1), Sc("sc:b", 1, 1) } });
        var icons = DesktopLayout.Resolve(s, Array.Empty<DesktopEntry>(), Both, M1).Icons;
        icons[0].Cell.Should().Be(new GridCell(1, 1));
        icons[1].Cell.Should().NotBe(new GridCell(1, 1));
        icons.Select(i => i.Cell).Distinct().Should().HaveCount(2);
    }

    [Fact]
    public void A_stored_cell_outside_the_grid_is_clamped_for_display_only()
    {
        var s = Enabled(d => d with { Shortcuts = new[] { Sc("sc:a", 500, 500) } });
        var r = DesktopLayout.Resolve(s, Array.Empty<DesktopEntry>(), Both, M1);
        r.Icons.Single().Cell.Should().Be(new GridCell(r.Grid.Columns - 1, r.Grid.Rows - 1));
        s.Desktop.Shortcuts[0].X.Should().Be(500); // the stored value is untouched
    }

    [Fact]
    public void Shortcuts_live_on_their_monitor_and_an_unplugged_one_falls_back_to_primary()
    {
        var s = Enabled(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0), Sc("sc:b", 0, 0, "m2"), Sc("sc:c", 0, 1, "gone") } });
        DesktopLayout.Resolve(s, Array.Empty<DesktopEntry>(), Both, M1).Icons.Select(i => i.Id).Should().BeEquivalentTo("sc:a", "sc:c");
        DesktopLayout.Resolve(s, Array.Empty<DesktopEntry>(), Both, M2).Icons.Select(i => i.Id).Should().BeEquivalentTo("sc:b");
    }

    [Fact]
    public void Desktop_folder_items_without_a_placement_fill_free_cells_and_are_reported_as_new_placements()
    {
        var s = Enabled(d => d with { Shortcuts = new[] { Sc("sc:a", 0, 0) } });
        var entries = new[] { Entry("fs:c:\\one", "one"), Entry("special:thisPc", "This PC", special: true) };
        var r = DesktopLayout.Resolve(s, entries, Both, M1);
        r.Icons.Single(i => i.Id == "fs:c:\\one").Cell.Should().Be(new GridCell(0, 1));
        r.Icons.Single(i => i.Id == "special:thisPc").Cell.Should().Be(new GridCell(0, 2));
        r.NewPlacements.Keys.Should().BeEquivalentTo("fs:c:\\one", "special:thisPc");
        r.NewPlacements["fs:c:\\one"].Should().Be(new GridCell(0, 1));
    }

    [Fact]
    public void Stored_placements_win_over_auto_placement_and_are_not_new()
    {
        var s = Enabled(d => d with { Placements = new Dictionary<string, DesktopCell> { ["fs:c:\\one"] = new() { X = 4, Y = 3 } } });
        var r = DesktopLayout.Resolve(s, new[] { Entry("fs:c:\\one"), Entry("fs:c:\\two") }, Both, M1);
        r.Icons.Single(i => i.Id == "fs:c:\\one").Cell.Should().Be(new GridCell(4, 3));
        r.Icons.Single(i => i.Id == "fs:c:\\two").Cell.Should().Be(new GridCell(0, 0));
        r.NewPlacements.Keys.Should().BeEquivalentTo("fs:c:\\two");
    }

    [Fact]
    public void Items_are_kinds_file_folder_or_special()
    {
        var entries = new[] { Entry("fs:a", "a"), Entry("fs:b", "b", folder: true), Entry("special:recycleBin", "Recycle Bin", special: true) };
        var kinds = DesktopLayout.Resolve(Enabled(), entries, Both, M1).Icons.ToDictionary(i => i.Id, i => i.Kind);
        kinds["fs:a"].Should().Be(DesktopIconKind.File);
        kinds["fs:b"].Should().Be(DesktopIconKind.Folder);
        kinds["special:recycleBin"].Should().Be(DesktopIconKind.Special);
    }

    [Fact]
    public void Desktop_folder_items_appear_only_on_the_primary_monitor()
    {
        var entries = new[] { Entry("fs:a") };
        DesktopLayout.Resolve(Enabled(), entries, Both, M1).Icons.Should().HaveCount(1);
        DesktopLayout.Resolve(Enabled(), entries, Both, M2).Icons.Should().BeEmpty();
    }

    [Fact]
    public void Turning_the_desktop_folder_off_leaves_only_shortcuts()
    {
        var s = Enabled(d => d with { ShowDesktopFolder = false, Shortcuts = new[] { Sc("sc:a", 0, 0) } });
        DesktopLayout.Resolve(s, new[] { Entry("fs:a") }, Both, M1).Icons.Select(i => i.Id).Should().Equal("sc:a");
    }

    [Fact]
    public void Duplicate_and_empty_entry_ids_are_ignored()
    {
        var entries = new[] { Entry("fs:a", "first"), Entry("fs:a", "second"), Entry("", "nameless") };
        var icons = DesktopLayout.Resolve(Enabled(), entries, Both, M1).Icons;
        icons.Should().ContainSingle().Which.Name.Should().Be("first");
    }

    [Fact]
    public void A_full_grid_stacks_extra_icons_on_the_last_cell_instead_of_dropping_them()
    {
        var tiny = Kit.Monitor("m1", 0, 0, 120, 160, primary: true); // 1 column x 1 row
        var entries = new[] { Entry("fs:a"), Entry("fs:b") };
        var r = DesktopLayout.Resolve(Enabled(), entries, new[] { tiny }, tiny);
        r.Icons.Should().HaveCount(2);
        r.Icons.Select(i => i.Cell).Should().OnlyContain(c => c == new GridCell(0, 0));
    }

    [Fact]
    public void The_grid_uses_the_work_area_of_the_monitor()
    {
        var withBar = Kit.Monitor("m1", 0, 0, 1920, 1080, primary: true, workTop: 28);
        DesktopLayout.GridFor(withBar, 64).CellOrigin(new GridCell(0, 0)).Y.Should().Be(28 + DesktopGrid.Inset);
    }

    [Fact]
    public void The_special_targets_are_exactly_the_two_allowed_literals()
    {
        DesktopLayout.SpecialTarget("special:thisPc").Should().Be("shell:MyComputerFolder");
        DesktopLayout.SpecialTarget("special:recycleBin").Should().Be("shell:RecycleBinFolder");
        DesktopLayout.SpecialTarget("special:whatever").Should().BeNull();
        DesktopLayout.SpecialTarget("fs:c:\\x").Should().BeNull();
    }
}
