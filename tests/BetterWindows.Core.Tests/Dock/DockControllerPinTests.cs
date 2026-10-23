using BetterWindows.Core.Dock;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.Dock;

public class DockControllerPinTests
{
    private static RawWindow W(long handle, string exe = @"C:\Tools\tool.exe", string? aumid = null, int pid = 100) => FakeWindowSystem.Win(handle, "Window " + handle, exe, aumid, pid);

    private static DockHarness Setup(Action<DockHarness>? arrange = null)
    {
        var d = new DockHarness();
        d.Catalog.Add("Alpha", aumid: "V.Alpha!App");
        d.Catalog.Add("Beta", aumid: "V.Beta!App", path: @"C:\Beta\beta.exe");
        d.Catalog.Add("Gamma", path: @"C:\Gamma\gamma.exe");
        d.Settings.Set(s => s with { General = s.General with { FirstRunDone = true } });
        arrange?.Invoke(d);
        d.Start();
        d.AddDock();
        return d;
    }

    private static string[] PinIds(DockHarness d) => d.Settings.Current.Dock.Pins.Select(p => p.Id).ToArray();
    private static Task<string> Pin(DockHarness d, string appId, int? index = null) => d.CallAsync(Msg.DockPin, new DockPinParams { AppId = appId, Index = index });
    private static Task<string> PinPath(DockHarness d, string path, int? index = null) => d.CallAsync(Msg.DockPinPath, new DockPinPathParams { Path = path, Index = index });

    // ---- dock.pin -----------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Pin_adds_a_catalog_app_with_its_launch_target_and_name()
    {
        using var d = Setup();
        (await Pin(d, "aumid:V.Alpha!App")).Should().BeEmpty();
        var pin = d.Settings.Current.Dock.Pins.Single();
        (pin.Id, pin.Kind, pin.Target, pin.Name).Should().Be(("aumid:V.Alpha!App", PinnedKind.App, "V.Alpha!App", "Alpha"));
        d.Settle();
        d.LastDockState()!.Items.Single().Pinned.Should().BeTrue();
    }

    [Fact]
    public async Task Pin_of_an_exe_catalog_app_stores_the_path_as_target()
    {
        using var d = Setup();
        await Pin(d, @"exe:c:\gamma\gamma.exe");
        d.Settings.Current.Dock.Pins.Single().Target.Should().Be(@"C:\Gamma\gamma.exe");
    }

    [Fact]
    public async Task Pin_resolves_an_app_id_to_the_canonical_catalog_entry()
    {
        using var d = Setup();
        await Pin(d, "aumid:v.beta!app");
        d.Settings.Current.Dock.Pins.Single().Id.Should().Be("aumid:V.Beta!App");
    }

    [Fact]
    public async Task Pin_respects_the_index_and_clamps_it()
    {
        using var d = Setup();
        await Pin(d, "aumid:V.Alpha!App");
        await Pin(d, "aumid:V.Beta!App", 0);
        await Pin(d, @"exe:c:\gamma\gamma.exe", 99);
        PinIds(d).Should().Equal("aumid:V.Beta!App", "aumid:V.Alpha!App", @"exe:c:\gamma\gamma.exe");
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = Array.Empty<PinnedItem>() } });
        await Pin(d, "aumid:V.Alpha!App", -3);
        PinIds(d).Should().Equal("aumid:V.Alpha!App");
    }

    [Fact]
    public async Task Pinning_twice_keeps_one_pin()
    {
        using var d = Setup();
        await Pin(d, "aumid:V.Alpha!App");
        (await Pin(d, "aumid:V.Alpha!App", 0)).Should().BeEmpty();
        PinIds(d).Should().ContainSingle();
    }

    [Fact]
    public async Task Pin_from_a_running_app_resolves_the_target_from_its_identity()
    {
        using var d = Setup(x => x.Windows.Add(W(1, exe: @"C:\Tools\My_Tool.exe", pid: 5)));
        (await Pin(d, @"exe:c:\tools\my_tool.exe")).Should().BeEmpty();
        var pin = d.Settings.Current.Dock.Pins.Single();
        (pin.Id, pin.Target, pin.Name).Should().Be((@"exe:c:\tools\my_tool.exe", @"C:\Tools\My_Tool.exe", "My Tool"));
    }

    [Fact]
    public async Task Pin_from_a_running_app_with_an_unknown_aumid_uses_the_window_aumid()
    {
        using var d = Setup(x => x.Windows.Add(W(1, exe: @"C:\Tools\tool.exe", aumid: "Vendor.Tool!Main", pid: 5)));
        await Pin(d, "aumid:Vendor.Tool!Main");
        var pin = d.Settings.Current.Dock.Pins.Single();
        (pin.Id, pin.Target).Should().Be(("aumid:Vendor.Tool!Main", "Vendor.Tool!Main"));
    }

    [Theory]
    [InlineData("aumid:Unknown!App", "notFound")]
    [InlineData(@"exe:c:\nowhere\x.exe", "notFound")]
    [InlineData("frame:ApplicationFrameWindow:12", "badRequest")]
    [InlineData("proc:12", "badRequest")]
    [InlineData("nonsense", "badRequest")]
    [InlineData(@"dir:c:\folders", "badRequest")]
    [InlineData("exe:relative\\path.exe", "badRequest")]
    [InlineData("", "badRequest")]
    public async Task Pin_rejects_unknown_and_unpinnable_ids(string appId, string code)
    {
        using var d = Setup();
        (await Pin(d, appId)).Should().Be(code);
        d.Settings.Current.Dock.Pins.Should().BeEmpty();
    }

    [Fact]
    public async Task A_running_app_without_a_usable_path_cannot_be_pinned()
    {
        using var d = Setup(x => x.Windows.Add(W(1, exe: @"\\server\share\x.exe", pid: 5)));
        (await Pin(d, @"exe:\\server\share\x.exe")).Should().Be("badRequest");
    }

    [Fact]
    public async Task An_exe_chosen_in_the_file_dialog_can_be_pinned_by_its_exe_id_but_others_cannot()
    {
        using var d = Setup();
        d.Probe.Paths[@"C:\Portable\app.exe"] = PathKind.File;
        (await Pin(d, @"exe:c:\portable\app.exe")).Should().Be("notFound", "not granted");
        d.Grants.Grant(@"C:\Portable\app.exe");
        (await Pin(d, @"exe:c:\portable\app.exe")).Should().BeEmpty();
        d.Settings.Current.Dock.Pins.Single().Target.Should().BeEquivalentTo(@"C:\Portable\app.exe", "the id carries the lower-cased path; Windows paths are case-insensitive");
    }

    [Fact]
    public async Task Pinning_beyond_the_limit_is_rejected()
    {
        using var d = Setup();
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = Enumerable.Range(0, PinsEditor.MaxPins).Select(i => new PinnedItem { Id = "exe:c:\\p" + i + ".exe", Target = "x" }).ToList() } });
        (await Pin(d, "aumid:V.Alpha!App")).Should().Be("badRequest");
    }

    // ---- dock.unpin ---------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Unpin_removes_the_pin_and_keeps_the_running_app_visible()
    {
        using var d = Setup(x => x.Windows.Add(W(1, aumid: "V.Alpha!App")));
        await Pin(d, "aumid:V.Alpha!App");
        await Pin(d, "aumid:V.Beta!App");
        (await d.CallAsync(Msg.DockUnpin, new DockItemParams { ItemId = "aumid:V.Alpha!App" })).Should().BeEmpty();
        PinIds(d).Should().Equal("aumid:V.Beta!App");
        d.Settle();
        var alpha = d.LastDockState()!.Items.Single(i => i.Id == "aumid:V.Alpha!App");
        (alpha.Pinned, alpha.Running).Should().Be((false, true));
    }

    [Fact]
    public async Task Unpin_of_something_that_is_not_pinned_is_not_found()
    {
        using var d = Setup();
        (await d.CallAsync(Msg.DockUnpin, new DockItemParams { ItemId = "aumid:V.Alpha!App" })).Should().Be("notFound");
        (await d.CallAsync(Msg.DockUnpin, new DockItemParams { ItemId = "" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Unpin_works_for_folders()
    {
        using var d = Setup();
        d.Settings.Set(s => s with { Dock = s.Dock with { Pins = new[] { new PinnedItem { Id = @"dir:c:\d", Kind = PinnedKind.Folder, Target = @"C:\D" } } } });
        (await d.CallAsync(Msg.DockUnpin, new DockItemParams { ItemId = @"dir:c:\d" })).Should().BeEmpty();
        d.Settings.Current.Dock.Pins.Should().BeEmpty();
    }

    // ---- dock.reorder -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Reorder_applies_the_new_order()
    {
        using var d = Setup();
        foreach (var id in new[] { "aumid:V.Alpha!App", "aumid:V.Beta!App", @"exe:c:\gamma\gamma.exe" }) await Pin(d, id);
        (await d.CallAsync(Msg.DockReorder, new DockReorderParams { ItemIds = new[] { @"exe:c:\gamma\gamma.exe", "aumid:V.Alpha!App", "aumid:V.Beta!App" } })).Should().BeEmpty();
        PinIds(d).Should().Equal(@"exe:c:\gamma\gamma.exe", "aumid:V.Alpha!App", "aumid:V.Beta!App");
        d.Settle();
        d.LastDockState()!.Items.Select(i => i.Id).Should().Equal(PinIds(d));
    }

    [Fact]
    public async Task Reorder_with_an_unknown_id_changes_nothing()
    {
        using var d = Setup();
        await Pin(d, "aumid:V.Alpha!App"); await Pin(d, "aumid:V.Beta!App");
        (await d.CallAsync(Msg.DockReorder, new DockReorderParams { ItemIds = new[] { "aumid:V.Beta!App", "aumid:Ghost!App" } })).Should().Be("notFound");
        PinIds(d).Should().Equal("aumid:V.Alpha!App", "aumid:V.Beta!App");
    }

    [Fact]
    public async Task Reorder_keeps_pins_the_ui_did_not_mention_and_ignores_duplicates()
    {
        using var d = Setup();
        await Pin(d, "aumid:V.Alpha!App"); await Pin(d, "aumid:V.Beta!App");
        await d.CallAsync(Msg.DockReorder, new DockReorderParams { ItemIds = new[] { "aumid:V.Beta!App", "aumid:V.Beta!App" } });
        PinIds(d).Should().Equal("aumid:V.Beta!App", "aumid:V.Alpha!App");
    }

    [Fact]
    public async Task Reorder_requires_the_item_list()
    {
        using var d = Setup();
        (await d.CallAsync(Msg.DockReorder, new { })).Should().Be("badRequest");
        (await d.CallAsync(Msg.DockReorder, new DockReorderParams { ItemIds = Enumerable.Range(0, 101).Select(i => "x" + i).ToArray() })).Should().Be("badRequest");
    }

    // ---- dock.pinPath -------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task PinPath_pins_a_granted_folder_with_a_lower_case_dir_id()
    {
        using var d = Setup();
        d.Probe.Paths[@"C:\Users\Me\Downloads"] = PathKind.Directory;
        d.Grants.Grant(@"C:\Users\Me\Downloads");
        (await PinPath(d, @"C:\Users\Me\Downloads\")).Should().BeEmpty();
        var pin = d.Settings.Current.Dock.Pins.Single();
        (pin.Id, pin.Kind, pin.Target, pin.Name).Should().Be((@"dir:c:\users\me\downloads", PinnedKind.Folder, @"C:\Users\Me\Downloads", "Downloads"));
        d.Settle();
        var folder = d.LastDockState()!.Items.Single();
        (folder.Kind, folder.Target).Should().Be((DockItemKind.Folder, @"C:\Users\Me\Downloads"));
    }

    [Fact]
    public async Task PinPath_pins_an_exe_and_a_lnk_as_apps_with_exe_ids()
    {
        using var d = Setup();
        d.Probe.Paths[@"C:\Portable\Cool_App.exe"] = PathKind.File;
        d.Probe.Paths[@"D:\Shortcuts\Thing.lnk"] = PathKind.File;
        d.Grants.Grant(@"C:\Portable\Cool_App.exe"); d.Grants.Grant(@"D:\Shortcuts\Thing.lnk");
        (await PinPath(d, @"C:\Portable\Cool_App.exe")).Should().BeEmpty();
        (await PinPath(d, @"D:\Shortcuts\Thing.lnk")).Should().BeEmpty();
        var pins = d.Settings.Current.Dock.Pins;
        pins.Select(p => p.Id).Should().Equal(@"exe:c:\portable\cool_app.exe", @"exe:d:\shortcuts\thing.lnk");
        pins.Should().OnlyContain(p => p.Kind == PinnedKind.App);
        pins.Select(p => p.Name).Should().Equal("Cool App", "Thing");
        pins[0].Target.Should().Be(@"C:\Portable\Cool_App.exe");
    }

    [Fact]
    public async Task PinPath_uses_the_catalog_identity_when_the_exe_is_a_known_app()
    {
        using var d = Setup();
        d.Probe.Paths[@"C:\Beta\beta.exe"] = PathKind.File;
        d.Grants.Grant(@"C:\Beta\beta.exe");
        await PinPath(d, @"C:\Beta\beta.exe");
        var pin = d.Settings.Current.Dock.Pins.Single();
        (pin.Id, pin.Target, pin.Name).Should().Be(("aumid:V.Beta!App", "V.Beta!App", "Beta"));
    }

    [Fact]
    public async Task PinPath_keeps_folders_after_apps_and_clamps_the_index()
    {
        using var d = Setup();
        d.Probe.Paths[@"C:\F1"] = PathKind.Directory; d.Probe.Paths[@"C:\P\a.exe"] = PathKind.File;
        d.Grants.Grant(@"C:\F1"); d.Grants.Grant(@"C:\P\a.exe");
        await PinPath(d, @"C:\F1");
        await Pin(d, "aumid:V.Alpha!App");
        await PinPath(d, @"C:\P\a.exe", 99);
        PinIds(d).Should().Equal("aumid:V.Alpha!App", @"exe:c:\p\a.exe", @"dir:c:\f1");
        await PinPath(d, @"C:\F1", 0);
        PinIds(d).Should().HaveCount(3, "already pinned: idempotent");
    }

    [Fact]
    public async Task PinPath_requires_a_path_from_the_file_dialog()
    {
        using var d = Setup();
        d.Probe.Paths[@"C:\Secret\x.exe"] = PathKind.File;
        (await PinPath(d, @"C:\Secret\x.exe")).Should().Be("notFound");
        d.Grants.Grant(@"C:\Secret");
        (await PinPath(d, @"C:\Secret\x.exe")).Should().BeEmpty("a granted folder also grants its children");
    }

    [Theory]
    [InlineData(@"relative\path.exe")]
    [InlineData(@"\\server\share\x.exe")]
    [InlineData(@"\\?\C:\x.exe")]
    [InlineData(@"C:\a\..\..\x.exe")]
    [InlineData(@"C:\ads.exe:stream")]
    [InlineData("C:\\wild*.exe")]
    [InlineData("")]
    public async Task PinPath_rejects_invalid_paths_even_if_they_were_granted(string path)
    {
        using var d = Setup();
        d.Probe.Paths[path] = PathKind.File;
        d.Grants.Grant(path);
        (await PinPath(d, path)).Should().Be("badRequest");
        d.Settings.Current.Dock.Pins.Should().BeEmpty();
    }

    [Fact]
    public async Task PinPath_rejects_missing_paths_and_unsupported_file_types()
    {
        using var d = Setup();
        d.Grants.Grant(@"C:\Stuff");
        (await PinPath(d, @"C:\Stuff\gone.exe")).Should().Be("notFound");
        d.Probe.Paths[@"C:\Stuff\notes.txt"] = PathKind.File;
        (await PinPath(d, @"C:\Stuff\notes.txt")).Should().Be("badRequest");
        d.Probe.Paths[@"C:\Stuff\run.bat"] = PathKind.File;
        (await PinPath(d, @"C:\Stuff\run.bat")).Should().Be("badRequest");
        d.Settings.Current.Dock.Pins.Should().BeEmpty();
    }

    [Fact]
    public async Task A_pinned_folder_can_be_opened_from_the_dock()
    {
        using var d = Setup();
        d.Probe.Paths[@"C:\Users\Me\Docs"] = PathKind.Directory;
        d.Grants.Grant(@"C:\Users\Me\Docs");
        await PinPath(d, @"C:\Users\Me\Docs");
        (await d.CallAsync(Msg.DockActivate, new DockItemParams { ItemId = @"dir:c:\users\me\docs" })).Should().BeEmpty();
        d.Launcher.Opened.Should().Equal(@"C:\Users\Me\Docs");
    }

    [Fact]
    public async Task A_pinned_exe_that_is_running_shows_as_running_and_is_activated_not_launched()
    {
        using var d = Setup(x => { x.Probe.Paths[@"C:\Tools\tool.exe"] = PathKind.File; x.Grants.Grant(@"C:\Tools\tool.exe"); x.Windows.Add(W(1, exe: @"C:\Tools\tool.exe", pid: 5)); });
        await PinPath(d, @"C:\Tools\tool.exe");
        d.Settle();
        var item = d.LastDockState()!.Items.Single();
        (item.Pinned, item.Running).Should().Be((true, true));
        await d.CallAsync(Msg.DockActivate, new DockItemParams { ItemId = item.Id });
        d.Windows.Calls.Should().Equal(new (string, long)[] { ("activate", 1) });
        d.Launcher.Launched.Should().BeEmpty();
    }
}
