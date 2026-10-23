using System.Text.Json.Nodes;
using BetterWindows.Core.Config;
using BetterWindows.Core.Protocol;
using SettingsDto = BetterWindows.Core.Protocol.Settings;

namespace BetterWindows.Core.Tests.Config;

public class SettingsValidatorTests
{
    private static SettingsDto Bind(string json) => SettingsValidator.Bind(JsonNode.Parse(json));
    private static SettingsDto With(Func<SettingsDto, SettingsDto> f) => SettingsValidator.Normalize(f(new SettingsDto()));

    private static PinnedItem Pin(string id, string target, PinnedKind kind = PinnedKind.App, string? name = null) => new() { Id = id, Kind = kind, Target = target, Name = name };

    [Fact]
    public void Defaults_are_a_fixed_point()
    {
        var d = new SettingsDto();
        ProtocolJson.Serialize(SettingsValidator.Normalize(d)).Should().Be(ProtocolJson.Serialize(d));
    }

    [Fact]
    public void Normalize_is_idempotent_on_a_messy_value()
    {
        var messy = With(s => s with
        {
            Dock = s.Dock with { IconSize = 500, Pins = new[] { Pin("aumid:A", " A "), Pin("aumid:A", "dup"), Pin("exe:c:/x/../y.exe", "c:/x/../y.exe") } },
            Hotkeys = s.Hotkeys with { Exit = "ctrl+alt+q ", ToggleTaskbar = "CTRL+ALT+Q" },
            Spotlight = s.Spotlight with { Providers = new[] { "APPS", "apps", "bogus" } },
        });
        ProtocolJson.Serialize(SettingsValidator.Normalize(messy)).Should().Be(ProtocolJson.Serialize(messy));
    }

    [Fact]
    public void Pins_without_a_target_or_with_a_bad_id_are_dropped_and_ids_are_unique()
    {
        var s = With(x => x with { Dock = x.Dock with { Pins = new[]
        {
            Pin("aumid:A", "A"), Pin("aumid:B", ""), Pin("aumid:C", "   "), Pin("bogus-id", "x"), Pin("", "x"), Pin("aumid:A", "again"), Pin("AUMID:a", "x"),
            Pin("exe:c:\\app\\a.exe", "C:\\app\\a.exe"), Pin("dir:C:\\Docs", "C:\\Docs", PinnedKind.Folder),
        } } });
        s.Dock.Pins.Select(p => p.Id).Should().Equal("aumid:A", "exe:c:\\app\\a.exe", "dir:C:\\Docs");
    }

    [Fact]
    public void Pin_paths_are_normalised_unsafe_paths_drop_the_pin_and_kind_follows_the_id()
    {
        var s = With(x => x with { Dock = x.Dock with { Pins = new[]
        {
            Pin("exe:c:/tools/../apps/a.exe", "c:/tools/../apps/a.exe", PinnedKind.Folder),
            Pin("exe:c:\\unc\\a.exe", "\\\\server\\share\\a.exe"),
            Pin("dir:C:\\Docs", "C:\\Docs\\..\\Docs", PinnedKind.App),
            Pin("aumid:Contoso.App!Main", "Contoso.App!Main", PinnedKind.Folder),
        } } });
        s.Dock.Pins.Should().HaveCount(3);
        s.Dock.Pins[0].Target.Should().Be("C:\\apps\\a.exe");
        s.Dock.Pins[0].Kind.Should().Be(PinnedKind.App);
        s.Dock.Pins[1].Kind.Should().Be(PinnedKind.Folder);
        s.Dock.Pins[1].Target.Should().Be("C:\\Docs");
        s.Dock.Pins[2].Kind.Should().Be(PinnedKind.App);
    }

    [Fact]
    public void Pins_are_capped_names_trimmed_and_blank_names_become_null()
    {
        var pins = Enumerable.Range(0, 150).Select(i => Pin("aumid:App" + i, "t", name: i == 0 ? "  Mail  " : i == 1 ? "   " : null)).ToArray();
        var s = With(x => x with { Dock = x.Dock with { Pins = pins } });
        s.Dock.Pins.Should().HaveCount(100);
        s.Dock.Pins[0].Name.Should().Be("Mail");
        s.Dock.Pins[1].Name.Should().BeNull();
    }

    [Fact]
    public void Widgets_get_unique_ids_clamped_positions_and_object_configs_only()
    {
        var s = With(x => x with { Desktop = x.Desktop with { Widgets = new[]
        {
            new WidgetInstance { Id = "w1", X = 99999, Y = -99999, Config = JsonNode.Parse("""{ "style": "analog" }""") },
            new WidgetInstance { Id = "w1", X = 1 },
            new WidgetInstance { Id = "", X = 1 },
            new WidgetInstance { Id = "w2", X = double.NaN, Y = double.PositiveInfinity, Config = JsonNode.Parse("\"text\"") },
            new WidgetInstance { Id = "w3", MonitorId = "  ", Config = JsonNode.Parse("[1]") },
        } } });
        s.Desktop.Widgets.Select(w => w.Id).Should().Equal("w1", "w2", "w3");
        s.Desktop.Widgets[0].X.Should().Be(16384);
        s.Desktop.Widgets[0].Y.Should().Be(-16384);
        s.Desktop.Widgets[0].Config!["style"]!.GetValue<string>().Should().Be("analog");
        s.Desktop.Widgets[1].X.Should().Be(0);
        s.Desktop.Widgets[1].Y.Should().Be(0);
        s.Desktop.Widgets[1].Config.Should().BeNull();
        s.Desktop.Widgets[2].MonitorId.Should().BeNull();
        s.Desktop.Widgets[2].Config.Should().BeNull();
    }

    [Fact]
    public void Widget_list_is_capped_and_oversized_configs_are_dropped()
    {
        var big = new JsonObject { ["text"] = new string('x', 70 * 1024) };
        var s = With(x => x with { Desktop = x.Desktop with { Widgets = Enumerable.Range(0, 130).Select(i => new WidgetInstance { Id = "w" + i, Config = i == 0 ? big : null }).ToArray() } });
        s.Desktop.Widgets.Should().HaveCount(100);
        s.Desktop.Widgets[0].Config.Should().BeNull();
    }

    [Fact]
    public void Shortcuts_need_unique_ids_and_a_local_path_or_web_target()
    {
        var s = With(x => x with { Desktop = x.Desktop with { Shortcuts = new[]
        {
            new DesktopShortcut { Id = "sc:1", Name = "  Notes ", Target = "c:/docs/notes.txt", X = -5, Y = 99999 },
            new DesktopShortcut { Id = "sc:1", Target = "C:\\other.txt" },
            new DesktopShortcut { Id = "sc:2", Target = "relative\\file.txt" },
            new DesktopShortcut { Id = "sc:3", Target = "\\\\server\\share\\f.txt" },
            new DesktopShortcut { Id = "sc:4", Target = "https://example.com/" },
            new DesktopShortcut { Id = "sc:5", Target = "javascript:alert(1)" },
            new DesktopShortcut { Id = "sc:6", Target = "C:\\ok.exe", MonitorId = "m0000abcd" },
        } } });
        s.Desktop.Shortcuts.Select(x => x.Id).Should().Equal("sc:1", "sc:4", "sc:6");
        s.Desktop.Shortcuts[0].Name.Should().Be("Notes");
        s.Desktop.Shortcuts[0].Target.Should().Be("C:\\docs\\notes.txt");
        s.Desktop.Shortcuts[0].X.Should().Be(0);
        s.Desktop.Shortcuts[0].Y.Should().Be(10000);
        s.Desktop.Shortcuts[2].MonitorId.Should().Be("m0000abcd");
    }

    [Fact]
    public void Shortcut_list_is_capped_at_500()
    {
        var s = With(x => x with { Desktop = x.Desktop with { Shortcuts = Enumerable.Range(0, 600).Select(i => new DesktopShortcut { Id = "sc:" + i, Target = "C:\\f" + i + ".txt" }).ToArray() } });
        s.Desktop.Shortcuts.Should().HaveCount(500);
    }

    [Fact]
    public void Wallpaper_paths_must_be_local_absolute_paths()
    {
        var s = With(x => x with { Desktop = x.Desktop with
        {
            WallpaperPath = "C:/pics/../walls/a.jpg",
            Wallpapers = new Dictionary<string, string> { ["m1"] = "D:\\b.png", ["m2"] = "\\\\evil\\share\\x.png", ["m3"] = "rel.png", [" "] = "C:\\ok.png", ["m4"] = "C:\\a:stream" },
        } });
        s.Desktop.WallpaperPath.Should().Be("C:\\walls\\a.jpg");
        s.Desktop.Wallpapers.Should().BeEquivalentTo(new Dictionary<string, string> { ["m1"] = "D:\\b.png" });
        With(x => x with { Desktop = x.Desktop with { WallpaperPath = "\\\\host\\share\\a.jpg" } }).Desktop.WallpaperPath.Should().BeNull();
        With(x => x with { Desktop = x.Desktop with { WallpaperPath = new string('a', 5000) } }).Desktop.WallpaperPath.Should().BeNull();
    }

    [Fact]
    public void Placements_are_clamped_to_the_grid_and_capped()
    {
        var map = Enumerable.Range(0, 2100).ToDictionary(i => "fs:" + i.ToString("D4"), i => new DesktopCell { X = i == 0 ? -3 : i, Y = 20000 });
        var s = With(x => x with { Desktop = x.Desktop with { Placements = map } });
        s.Desktop.Placements.Should().HaveCount(2000);
        s.Desktop.Placements["fs:0000"].X.Should().Be(0);
        s.Desktop.Placements["fs:0000"].Y.Should().Be(10000);
    }

    [Theory]
    [InlineData("red")]
    [InlineData("#12345")]
    [InlineData("#gggggg")]
    [InlineData("javascript:alert(1)")]
    [InlineData("#112233); background: url(x")]
    public void Invalid_accents_fall_back_to_system(string accent)
    {
        With(x => x with { Appearance = x.Appearance with { Accent = accent } }).Appearance.Accent.Should().Be("system");
    }

    [Theory]
    [InlineData("#0a84ff")]
    [InlineData("#0A84FF80")]
    [InlineData("system")]
    public void Valid_accents_are_kept(string accent)
    {
        With(x => x with { Appearance = x.Appearance with { Accent = accent } }).Appearance.Accent.Should().Be(accent);
    }

    [Theory]
    [InlineData("de", "auto")]
    [InlineData("EN", "auto")]
    [InlineData("ru", "ru")]
    [InlineData("auto", "auto")]
    public void Language_must_match_the_pattern(string input, string expected)
    {
        With(x => x with { General = x.General with { Language = input } }).General.Language.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://duckduckgo.com/?q={query}", true)]
    [InlineData("http://example.org/s/{query}/x", true)]
    [InlineData("https://example.org/?q=", false)]
    [InlineData("https://example.org/?q={query}&r={query}", false)]
    [InlineData("ftp://example.org/{query}", false)]
    [InlineData("file:///C:/{query}", false)]
    [InlineData("javascript:{query}", false)]
    [InlineData("https://user:pw@example.org/{query}", false)]
    [InlineData("{query}", false)]
    public void The_web_search_url_needs_http_or_https_and_exactly_one_placeholder(string url, bool valid)
    {
        var s = With(x => x with { Spotlight = x.Spotlight with { WebSearchUrl = url } });
        s.Spotlight.WebSearchUrl.Should().Be(valid ? url : new SpotlightSettings().WebSearchUrl);
    }

    [Fact]
    public void Provider_lists_are_canonicalised_deduplicated_and_never_silently_emptied()
    {
        With(x => x with { Spotlight = x.Spotlight with { Providers = new[] { "Apps", "files", "FILES", "nope", "calc" } } }).Spotlight.Providers.Should().Equal("apps", "files", "calc");
        With(x => x with { Spotlight = x.Spotlight with { Providers = new[] { "x", "y" } } }).Spotlight.Providers.Should().HaveCount(7, "all entries invalid -> defaults");
        With(x => x with { Spotlight = x.Spotlight with { Providers = Array.Empty<string>() } }).Spotlight.Providers.Should().BeEmpty("an explicitly empty list is a valid subset");
    }

    [Fact]
    public void Hotkeys_are_canonicalised_and_unparseable_ones_reset()
    {
        var s = With(x => x with
        {
            Spotlight = x.Spotlight with { Hotkey = "alt + space" },
            Hotkeys = x.Hotkeys with { Launchpad = "win+ctrl+l", MissionControl = "ctrl+alt+nope", Exit = "Control+Alt+Q", ToggleTaskbar = "super+shift+ctrl+alt+t" },
        });
        s.Spotlight.Hotkey.Should().Be("Alt+Space");
        s.Hotkeys.Launchpad.Should().Be("Ctrl+Win+L");
        s.Hotkeys.MissionControl.Should().Be("Ctrl+Alt+M");
        s.Hotkeys.Exit.Should().Be("Ctrl+Alt+Q");
        s.Hotkeys.ToggleTaskbar.Should().Be("Ctrl+Alt+Shift+Win+T");
    }

    [Theory]
    [InlineData("F4", "F4")]
    [InlineData("f12", "F12")]
    [InlineData("Ctrl+Num5", "Ctrl+Num5")]
    [InlineData("Shift+Backtick", "Shift+Backtick")]
    [InlineData("Alt+Esc", "Alt+Esc")]
    public void Valid_hotkey_forms(string input, string canonical)
    {
        HotkeySyntax.TryCanonicalize(input, out var c).Should().BeTrue();
        c.Should().Be(canonical);
    }

    [Theory]
    [InlineData("")]
    [InlineData("Q")]
    [InlineData("Ctrl")]
    [InlineData("Ctrl+")]
    [InlineData("Ctrl+Ctrl+Q")]
    [InlineData("Ctrl+F25")]
    [InlineData("Ctrl++Q")]
    [InlineData("Hyper+Q")]
    [InlineData("Ctrl+Alt+Shift+Win+Ctrl+Q")]
    public void Invalid_hotkey_forms(string input)
    {
        HotkeySyntax.TryCanonicalize(input, out _).Should().BeFalse();
    }

    [Fact]
    public void Optional_hotkeys_may_be_empty_but_the_escape_hatches_may_not()
    {
        var s = With(x => x with
        {
            Spotlight = x.Spotlight with { Hotkey = "" },
            Hotkeys = x.Hotkeys with { Launchpad = "  ", MissionControl = "", Exit = "", ToggleTaskbar = "   " },
        });
        s.Spotlight.Hotkey.Should().BeEmpty();
        s.Hotkeys.Launchpad.Should().BeEmpty();
        s.Hotkeys.MissionControl.Should().BeEmpty();
        s.Hotkeys.Exit.Should().Be("Ctrl+Alt+Q");
        s.Hotkeys.ToggleTaskbar.Should().Be("Ctrl+Alt+Shift+T");
    }

    [Fact]
    public void Exit_and_toggle_taskbar_must_be_different()
    {
        var s = With(x => x with { Hotkeys = x.Hotkeys with { Exit = "Ctrl+Alt+Z", ToggleTaskbar = "ctrl+alt+z" } });
        s.Hotkeys.Exit.Should().Be("Ctrl+Alt+Z");
        s.Hotkeys.ToggleTaskbar.Should().Be("Ctrl+Alt+Shift+T");
        var s2 = With(x => x with { Hotkeys = x.Hotkeys with { Exit = "Ctrl+Alt+Shift+T", ToggleTaskbar = "Ctrl+Alt+Shift+T" } });
        s2.Hotkeys.Exit.Should().NotBe(s2.Hotkeys.ToggleTaskbar);
    }

    [Fact]
    public void Weather_coordinates_out_of_range_are_forgotten()
    {
        var s = With(x => x with { Weather = x.Weather with { Latitude = 91, Longitude = -181, LocationName = "  Oslo  " } });
        s.Weather.Latitude.Should().BeNull();
        s.Weather.Longitude.Should().BeNull();
        s.Weather.LocationName.Should().Be("Oslo");
        With(x => x with { Weather = x.Weather with { Latitude = 59.9, Longitude = 10.7 } }).Weather.Latitude.Should().Be(59.9);
        With(x => x with { Weather = x.Weather with { Latitude = double.NaN } }).Weather.Latitude.Should().BeNull();
    }

    [Fact]
    public void Launchpad_order_never_lists_folder_members_or_hidden_apps_and_unknown_ids_go()
    {
        var s = With(x => x with { Launchpad = new LaunchpadSettings
        {
            Folders = new[]
            {
                new LaunchpadFolder { Id = "folder:1", Name = " Work ", AppIds = new[] { "aumid:A", "aumid:A", "exe:c:\\b.exe", "junk" } },
                new LaunchpadFolder { Id = "folder:2", Name = "Other", AppIds = new[] { "aumid:A", "aumid:C" } },
                new LaunchpadFolder { Id = "folder:1", Name = "dup" },
            },
            Hidden = new[] { "aumid:H", "aumid:H", "junk" },
            Order = new[] { "aumid:A", "folder:1", "aumid:H", "aumid:D", "folder:2", "folder:ghost", "aumid:D", "exe:c:\\b.exe", "junk" },
        } });
        s.Launchpad.Folders.Should().HaveCount(2);
        s.Launchpad.Folders[0].Name.Should().Be("Work");
        s.Launchpad.Folders[0].AppIds.Should().Equal("aumid:A", "exe:c:\\b.exe");
        s.Launchpad.Folders[1].AppIds.Should().Equal(new[] { "aumid:C" }, "an app lives in one folder only");
        s.Launchpad.Hidden.Should().Equal("aumid:H");
        s.Launchpad.Order.Should().Equal("folder:1", "aumid:D", "folder:2");
    }

    [Fact]
    public void Free_text_loses_control_characters()
    {
        var s = With(x => x with { Weather = x.Weather with { LocationName = "Os\u0007lo\r\n" }, Dock = x.Dock with { Pins = new[] { Pin("aumid:A", "A", name: "A\u0000B") } } });
        s.Weather.LocationName.Should().Be("Os lo");
        s.Dock.Pins[0].Name.Should().Be("A B");
    }

    [Fact]
    public void Control_characters_in_a_pin_target_drop_the_pin()
    {
        With(x => x with { Dock = x.Dock with { Pins = new[] { Pin("aumid:A", "A\u0001B") } } }).Dock.Pins.Should().BeEmpty();
    }

    [Fact]
    public void Explicit_nulls_and_partial_sections_in_a_typed_value_do_not_throw()
    {
        var weird = new SettingsDto { Dock = null!, Desktop = new DesktopSettings { Shortcuts = null!, Widgets = null!, Wallpapers = null! }, Launchpad = new LaunchpadSettings { Folders = null!, Order = null!, Hidden = null! } };
        var act = () => SettingsValidator.Normalize(weird);
        act.Should().NotThrow();
        act().Dock.IconSize.Should().Be(56);
    }

    [Fact]
    public void Binding_garbage_never_throws()
    {
        foreach (var json in new[] { "null", "5", "\"x\"", "[]", "{}", """{ "dock": [], "desktop": { "widgets": "no", "shortcuts": [1, "x", null, {}] }, "launchpad": { "folders": [ { "appIds": 5 } ] } }""" })
        {
            var act = () => Bind(json);
            act.Should().NotThrow(json);
        }
        Bind("""{ "desktop": { "shortcuts": [1, "x", null, {}] } }""").Desktop.Shortcuts.Should().BeEmpty("an element without id/target is dropped");
    }

    [Fact]
    public void Feature_flags_round_trip()
    {
        Bind("""{ "general": { "features": { "trayHosting": true, "liveThumbnails": true } } }""").General.Features.Should().BeEquivalentTo(new FeatureFlags { TrayHosting = true, LiveThumbnails = true });
    }
}
