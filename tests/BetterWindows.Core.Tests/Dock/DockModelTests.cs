using BetterWindows.Core.Dock;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.Dock;

public class DockModelTests
{
    private readonly FakeAppCatalog _catalog = new();
    private readonly List<TrackedWindow> _windows = new();
    private long _foreground;
    private IReadOnlyList<PinnedItem> _pins = Array.Empty<PinnedItem>();
    private IReadOnlyList<string> _order = Array.Empty<string>();
    private IReadOnlyList<string> _launching = Array.Empty<string>();

    private static PinnedItem Pin(string id, string target, string? name = null, PinnedKind kind = PinnedKind.App) => new() { Id = id, Kind = kind, Target = target, Name = name };
    private static PinnedItem Folder(string path, string? name = null) => Pin("dir:" + path.ToLowerInvariant(), path, name, PinnedKind.Folder);

    private TrackedWindow Win(long handle, string appId, string appName, Func<RawWindow, RawWindow>? tweak = null, string? exe = null, string? aumid = null)
    {
        var raw = FakeWindowSystem.Win(handle, "Title " + handle, exe ?? @"C:\Apps\" + appName.Replace(' ', '_') + ".exe", aumid, (int)handle + 1000);
        if (tweak is not null) raw = tweak(raw);
        var tw = new TrackedWindow(raw, appId, appName);
        _windows.Add(tw);
        return tw;
    }

    private DockState Build(Func<long, string?>? desktopOf = null)
    {
        var fg = _windows.FirstOrDefault(w => w.Handle == _foreground);
        return DockModel.Build(new DockModelInput
        {
            Pins = _pins,
            Windows = new WindowSnapshot(1, _windows.ToArray(), fg),
            FindApp = _catalog.FindById,
            RunningOrder = _order,
            Launching = _launching,
            DesktopOf = desktopOf,
        });
    }

    [Fact]
    public void An_empty_input_gives_an_empty_dock()
    {
        var s = Build();
        s.Items.Should().BeEmpty();
        s.Minimized.Should().BeEmpty();
        s.ActiveItemId.Should().BeNull();
    }

    [Fact]
    public void Pinned_apps_that_are_not_running_keep_the_user_order_and_catalog_names()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        var b = _catalog.Add("Beta", path: @"C:\b\beta.exe");
        _pins = new[] { Pin(b.Id, b.Path!), Pin(a.Id, a.Aumid!) };
        var s = Build();
        s.Items.Select(i => i.Id).Should().Equal(b.Id, a.Id);
        s.Items.Select(i => i.Name).Should().Equal("Beta", "Alpha");
        s.Items.Should().OnlyContain(i => i.Pinned && !i.Running && !i.Active && !i.Attention && i.Kind == DockItemKind.App && i.Windows.Count == 0);
        s.Items.Should().OnlyContain(i => i.Badge == null);
    }

    [Fact]
    public void A_pin_that_is_missing_from_the_catalog_is_still_shown_with_its_stored_name()
    {
        _pins = new[] { Pin("exe:c:\\portable\\tool.exe", @"C:\portable\tool.exe", "My Tool") };
        var item = Build().Items.Single();
        item.Name.Should().Be("My Tool");
        item.Pinned.Should().BeTrue();
        item.IconUrl.Should().Be(DynUrls.PathIcon(@"C:\portable\tool.exe", 256), "an uninstalled/portable exe is represented by its file icon");
    }

    [Fact]
    public void A_pin_without_name_or_catalog_entry_gets_a_name_derived_from_its_target()
    {
        _pins = new[] { Pin("exe:c:\\portable\\cool_tool.exe", @"C:\portable\cool_tool.exe"), Pin("aumid:Vendor.NiceApp_x!App", "Vendor.NiceApp_x!App") };
        Build().Items.Select(i => i.Name).Should().Equal("Cool Tool", "Nice App");
    }

    [Fact]
    public void The_catalog_name_wins_over_the_stored_pin_name()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!, "Old stored name") };
        Build().Items.Single().Name.Should().Be("Alpha");
    }

    [Fact]
    public void A_running_pinned_app_collects_its_windows_most_recent_first_and_is_marked_running()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!) };
        Win(2, a.Id, "Alpha"); Win(1, a.Id, "Alpha");
        var item = Build().Items.Single();
        item.Running.Should().BeTrue();
        item.Windows.Select(w => w.Handle).Should().Equal("2", "1");
        item.Windows.Should().OnlyContain(w => w.AppId == a.Id);
    }

    [Fact]
    public void Running_but_unpinned_apps_follow_the_pins_in_first_seen_order()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!) };
        Win(1, "exe:c:\\x\\zed.exe", "Zed"); Win(2, a.Id, "Alpha"); Win(3, "exe:c:\\x\\bee.exe", "Bee");
        _order = new[] { "exe:c:\\x\\zed.exe", "exe:c:\\x\\bee.exe", a.Id };
        var s = Build();
        s.Items.Select(i => i.Name).Should().Equal("Alpha", "Zed", "Bee");
        s.Items.Select(i => i.Pinned).Should().Equal(true, false, false);
    }

    [Fact]
    public void Apps_missing_from_the_first_seen_order_come_last_and_alphabetically()
    {
        Win(1, "exe:c:\\x\\zed.exe", "Zed"); Win(2, "exe:c:\\x\\bee.exe", "Bee"); Win(3, "exe:c:\\x\\cat.exe", "Cat");
        _order = new[] { "exe:c:\\x\\cat.exe" };
        Build().Items.Select(i => i.Name).Should().Equal("Cat", "Bee", "Zed");
    }

    [Fact]
    public void The_order_of_unpinned_apps_does_not_change_when_the_user_switches_windows()
    {
        Win(1, "exe:c:\\x\\one.exe", "One"); Win(2, "exe:c:\\x\\two.exe", "Two");
        _order = new[] { "exe:c:\\x\\one.exe", "exe:c:\\x\\two.exe" };
        var before = Build().Items.Select(i => i.Id).ToArray();
        _windows.Reverse(); // MRU order flips
        Build().Items.Select(i => i.Id).Should().Equal(before);
    }

    [Fact]
    public void The_active_item_is_the_one_holding_the_foreground_window()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!) };
        Win(1, a.Id, "Alpha"); Win(2, "exe:c:\\x\\bee.exe", "Bee");
        _foreground = 2;
        var s = Build();
        s.ActiveItemId.Should().Be("exe:c:\\x\\bee.exe");
        s.Items.Single(i => i.Active).Id.Should().Be("exe:c:\\x\\bee.exe");
        s.Items[1].Windows.Single().Active.Should().BeTrue();
        s.Items[0].Windows.Single().Active.Should().BeFalse();
    }

    [Fact]
    public void A_flashing_background_window_sets_attention_but_a_flashing_foreground_window_does_not()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!) };
        Win(1, a.Id, "Alpha", w => w with { IsFlashing = true });
        Build().Items.Single().Attention.Should().BeTrue();
        _foreground = 1;
        Build().Items.Single().Attention.Should().BeFalse();
    }

    [Fact]
    public void Minimised_windows_are_listed_separately_and_stay_in_their_item()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!) };
        Win(1, a.Id, "Alpha", w => w with { IsMinimized = true }); Win(2, a.Id, "Alpha"); Win(3, "exe:c:\\x\\bee.exe", "Bee", w => w with { IsMinimized = true });
        var s = Build();
        s.Minimized.Select(w => w.Handle).Should().Equal("1", "3");
        s.Minimized.Should().OnlyContain(w => w.Minimized);
        s.Items[0].Windows.Should().HaveCount(2);
    }

    [Fact]
    public void Window_refs_carry_state_and_icons()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        Win(1, a.Id, "Alpha", w => w with { IsMaximized = true, IsElevated = true });
        Win(2, "exe:c:\\x\\bee.exe", "Bee");
        var s = Build();
        var alpha = s.Items.Single(i => i.Name == "Alpha").Windows.Single();
        alpha.Maximized.Should().BeTrue();
        alpha.Elevated.Should().BeTrue();
        alpha.ThumbUrl.Should().BeNull();
        alpha.IconUrl.Should().Be(DynUrls.AppIcon(a.Id, 256));
        s.Items.Single(i => i.Name == "Bee").Windows.Single().IconUrl.Should().Be(DynUrls.WindowIcon(2, 256), "an app the catalog does not know falls back to the window icon");
    }

    [Fact]
    public void Item_icons_are_requested_at_256_and_use_a_stable_window_for_unknown_apps()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!) };
        Win(9, a.Id, "Alpha"); Win(5, "exe:c:\\x\\bee.exe", "Bee"); Win(3, "exe:c:\\x\\bee.exe", "Bee");
        var s = Build();
        s.Items[0].IconUrl.Should().Be(DynUrls.AppIcon(a.Id, 256));
        s.Items[1].IconUrl.Should().Be(DynUrls.WindowIcon(3, 256), "lowest handle: the URL must not change while the user switches between windows");
    }

    [Fact]
    public void A_title_is_truncated_and_sanitised()
    {
        Win(1, "exe:c:\\x\\bee.exe", "Bee", w => w with { Title = new string('x', 1000) + "\u0007" });
        Build().Items.Single().Windows.Single().Title.Length.Should().Be(256);
    }

    // ---- aliases: the pin and the window were identified differently -----------------------------------------------------------------

    [Fact]
    public void A_window_whose_exe_path_equals_the_pin_target_belongs_to_the_pin_even_with_another_identity()
    {
        // pinned through dock.pinPath (exe id), but the catalog knows the same exe under an AUMID
        _pins = new[] { Pin(@"exe:c:\apps\foo.exe", @"C:\Apps\Foo.exe", "Foo") };
        Win(1, "aumid:Vendor.Foo!App", "Foo", exe: @"C:\APPS\FOO.EXE");
        var s = Build();
        s.Items.Should().ContainSingle();
        s.Items[0].Id.Should().Be(@"exe:c:\apps\foo.exe");
        s.Items[0].Running.Should().BeTrue();
    }

    [Fact]
    public void A_window_whose_own_aumid_equals_the_pin_aumid_belongs_to_the_pin()
    {
        _pins = new[] { Pin("aumid:Chrome", "Chrome", "Chrome") };
        Win(1, @"exe:c:\chrome\chrome.exe", "Chrome", aumid: "chrome");
        var s = Build();
        s.Items.Should().ContainSingle();
        s.Items[0].Pinned.Should().BeTrue();
        s.Items[0].Running.Should().BeTrue();
    }

    [Fact]
    public void An_exact_id_match_wins_over_an_alias_match_of_an_earlier_pin()
    {
        _pins = new[] { Pin(@"exe:c:\apps\launcher.exe", @"C:\Apps\Shared.exe", "Launcher"), Pin("aumid:Vendor.Real!App", "Vendor.Real!App", "Real") };
        Win(1, "aumid:Vendor.Real!App", "Real", exe: @"C:\Apps\Shared.exe");
        var s = Build();
        s.Items.Single(i => i.Id == "aumid:Vendor.Real!App").Running.Should().BeTrue();
        s.Items.Single(i => i.Id.StartsWith("exe:")).Running.Should().BeFalse();
    }

    [Fact]
    public void Ids_are_matched_case_insensitively()
    {
        _pins = new[] { Pin("aumid:Vendor.App!Main", "Vendor.App!Main", "App") };
        Win(1, "AUMID:VENDOR.APP!MAIN", "App");
        Build().Items.Should().ContainSingle().Which.Running.Should().BeTrue();
    }

    // ---- folders, separators, duplicates ---------------------------------------------------------------------------------------------

    [Fact]
    public void Folders_come_after_apps_behind_a_separator()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Folder(@"C:\Users\me\Downloads"), Pin(a.Id, a.Aumid!) };
        Win(1, "exe:c:\\x\\bee.exe", "Bee");
        var s = Build();
        s.Items.Select(i => i.Kind).Should().Equal(DockItemKind.App, DockItemKind.App, DockItemKind.Separator, DockItemKind.Folder);
        s.Items[2].Id.Should().Be("separator:1");
        var folder = s.Items[3];
        folder.Id.Should().Be(@"dir:c:\users\me\downloads");
        folder.Name.Should().Be("Downloads");
        folder.Target.Should().Be(@"C:\Users\me\Downloads");
        folder.Pinned.Should().BeTrue();
        folder.Running.Should().BeFalse();
        folder.IconUrl.Should().Be(DynUrls.PathIcon(@"C:\Users\me\Downloads", 256));
        folder.AppId.Should().BeNull();
    }

    [Fact]
    public void A_folder_name_comes_from_the_pin_when_set_and_from_the_path_otherwise()
    {
        _pins = new[] { Folder(@"C:\Data", "My data"), Folder(@"D:\") };
        Build().Items.Where(i => i.Kind == DockItemKind.Folder).Select(i => i.Name).Should().Equal("My data", @"D:\");
    }

    [Fact]
    public void Only_folders_means_no_leading_separator()
    {
        _pins = new[] { Folder(@"C:\Data") };
        Build().Items.Should().ContainSingle().Which.Kind.Should().Be(DockItemKind.Folder);
    }

    [Fact]
    public void Duplicate_and_empty_pins_are_ignored()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!), Pin(a.Id.ToUpperInvariant(), a.Aumid!), Pin("", "x"), Pin("  ", "y") };
        Build().Items.Should().ContainSingle();
    }

    [Fact]
    public void Folder_pins_listed_before_app_pins_are_still_drawn_after_them()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Folder(@"C:\F"), Pin(a.Id, a.Aumid!) };
        Build().Items.Select(i => i.Kind).Should().Equal(DockItemKind.App, DockItemKind.Separator, DockItemKind.Folder);
    }

    // ---- launching, other desktops ---------------------------------------------------------------------------------------------------

    [Fact]
    public void A_pinned_app_that_is_being_launched_is_flagged_but_does_not_move()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        var b = _catalog.Add("Beta", aumid: "V.Beta!App");
        _pins = new[] { Pin(a.Id, a.Aumid!), Pin(b.Id, b.Aumid!) };
        _launching = new[] { b.Id };
        var s = Build();
        s.Items.Select(i => i.Id).Should().Equal(a.Id, b.Id);
        s.Items.Select(i => i.Launching).Should().Equal(false, true);
        s.Items[1].Running.Should().BeFalse();
    }

    [Fact]
    public void An_unpinned_app_being_launched_gets_a_transient_item_after_the_running_apps()
    {
        var c = _catalog.Add("Gamma", aumid: "V.Gamma!App");
        Win(1, "exe:c:\\x\\bee.exe", "Bee");
        _launching = new[] { c.Id };
        var s = Build();
        s.Items.Select(i => i.Name).Should().Equal("Bee", "Gamma");
        var transient = s.Items[1];
        transient.Launching.Should().BeTrue();
        transient.Pinned.Should().BeFalse();
        transient.Running.Should().BeFalse();
        transient.IconUrl.Should().Be(DynUrls.AppIcon(c.Id, 256));
        transient.Badge.Should().BeNull();
    }

    [Fact]
    public void Launching_disappears_as_soon_as_the_app_has_a_window()
    {
        var c = _catalog.Add("Gamma", aumid: "V.Gamma!App");
        _launching = new[] { c.Id };
        Win(1, c.Id, "Gamma");
        var item = Build().Items.Should().ContainSingle().Subject;
        item.Running.Should().BeTrue();
        item.Launching.Should().BeFalse();
    }

    [Fact]
    public void Windows_on_other_virtual_desktops_count_as_running_and_report_their_desktop()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!) };
        Win(1, a.Id, "Alpha", w => w with { IsCloaked = true, CloakedFlags = 2, IsCloakedByShell = true });
        Win(2, a.Id, "Alpha");
        var asked = new List<long>();
        var item = Build(h => { asked.Add(h); return "desktop-2"; }).Items.Single();
        item.Running.Should().BeTrue();
        item.Windows.Single(w => w.Handle == "1").DesktopId.Should().Be("desktop-2");
        item.Windows.Single(w => w.Handle == "2").DesktopId.Should().BeNull();
        asked.Should().Equal(1L);
    }

    [Fact]
    public void A_failing_desktop_lookup_does_not_break_the_model()
    {
        Win(1, "exe:c:\\x\\bee.exe", "Bee", w => w with { CloakedFlags = 2, IsCloaked = true });
        var s = Build(_ => throw new InvalidOperationException("com"));
        s.Items.Single().Windows.Single().DesktopId.Should().BeNull();
    }

    // ---- determinism -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_same_input_always_produces_the_same_output()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!), Folder(@"C:\F") };
        Win(1, a.Id, "Alpha"); Win(2, "exe:c:\\x\\bee.exe", "Bee", w => w with { IsMinimized = true });
        _launching = new[] { "aumid:Other" };
        ProtocolJson.Serialize(Build()).Should().Be(ProtocolJson.Serialize(Build()));
    }

    [Fact]
    public void The_model_serialises_to_valid_protocol_json()
    {
        var a = _catalog.Add("Alpha", aumid: "V.Alpha!App");
        _pins = new[] { Pin(a.Id, a.Aumid!), Folder(@"C:\F") };
        Win(1, a.Id, "Alpha");
        var json = ProtocolJson.Serialize(Build());
        var back = ProtocolJson.Deserialize<DockState>(json)!;
        back.Items.Should().HaveCount(3);
    }
}
