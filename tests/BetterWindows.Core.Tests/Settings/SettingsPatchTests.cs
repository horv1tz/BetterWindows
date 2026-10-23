using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;

using SettingsDtoAlias = BetterWindows.Core.Protocol.Settings;

namespace BetterWindows.Core.Tests.Config;

public class SettingsPatchTests
{
    private static JsonNode P(string json) => JsonNode.Parse(json)!;

    private static async Task<ProtocolException> Rejected(SettingsFixture f, string json)
    {
        var act = async () => await f.Service.PatchAsync(P(json));
        return (await act.Should().ThrowAsync<ProtocolException>()).Which;
    }

    [Fact]
    public async Task Objects_merge_and_primitives_replace()
    {
        using var f = new SettingsFixture();
        var s = await f.Service.PatchAsync(P("""{ "dock": { "iconSize": 80 }, "menuBar": { "showSeconds": true } }"""));
        s.Dock.IconSize.Should().Be(80);
        s.Dock.MagnifiedSize.Should().Be(88, "siblings of a patched key are untouched");
        s.MenuBar.ShowSeconds.Should().BeTrue();
        s.MenuBar.ShowDate.Should().BeTrue();
    }

    [Fact]
    public async Task Arrays_replace_wholesale()
    {
        using var f = new SettingsFixture();
        await f.Service.PatchAsync(P("""{ "dock": { "pins": [ { "id": "aumid:A", "kind": "app", "target": "A" }, { "id": "aumid:B", "kind": "app", "target": "B" } ] } }"""));
        var s = await f.Service.PatchAsync(P("""{ "dock": { "pins": [ { "id": "aumid:C", "kind": "app", "target": "C" } ] } }"""));
        s.Dock.Pins.Select(p => p.Id).Should().Equal("aumid:C");
    }

    [Fact]
    public async Task Maps_merge_entry_by_entry_and_null_removes_an_entry()
    {
        using var f = new SettingsFixture();
        await f.Service.PatchAsync(P("""{ "desktop": { "wallpapers": { "m1": "C:\\a.png", "m2": "C:\\b.png" } } }"""));
        var s = await f.Service.PatchAsync(P("""{ "desktop": { "wallpapers": { "m3": "C:\\c.png", "m1": null } } }"""));
        s.Desktop.Wallpapers.Keys.Should().BeEquivalentTo("m2", "m3");
    }

    [Fact]
    public async Task Null_resets_to_the_default_nullable_to_null_and_arrays_to_their_default()
    {
        using var f = new SettingsFixture();
        await f.Service.PatchAsync(P("""{ "dock": { "iconSize": 100, "position": "left" }, "desktop": { "wallpaperPath": "C:\\w.png" }, "spotlight": { "providers": ["apps"] }, "weather": { "latitude": 10 } }"""));
        var s = await f.Service.PatchAsync(P("""{ "dock": { "iconSize": null, "position": null }, "desktop": { "wallpaperPath": null }, "spotlight": { "providers": null }, "weather": { "latitude": null } }"""));
        s.Dock.IconSize.Should().Be(56);
        s.Dock.Position.Should().Be(DockPosition.Bottom);
        s.Desktop.WallpaperPath.Should().BeNull();
        s.Spotlight.Providers.Should().HaveCount(7);
        s.Weather.Latitude.Should().BeNull();
    }

    [Fact]
    public async Task Null_for_a_whole_section_resets_the_section()
    {
        using var f = new SettingsFixture();
        await f.Service.PatchAsync(P("""{ "menuBar": { "showSeconds": true, "autoHide": true } }"""));
        var s = await f.Service.PatchAsync(P("""{ "menuBar": null }"""));
        s.MenuBar.Should().BeEquivalentTo(new MenuBarSettings());
    }

    [Fact]
    public async Task Unknown_key_is_a_bad_request_naming_the_path()
    {
        using var f = new SettingsFixture();
        var e = await Rejected(f, """{ "dock": { "nope": 1, "iconSize": 70 } }""");
        e.Code.Should().Be("badRequest");
        e.Message.Should().Contain("dock.nope");
        f.Service.Current.Dock.IconSize.Should().Be(56, "a rejected patch changes nothing, not even its valid keys");
    }

    [Fact]
    public async Task Unknown_top_level_key_and_unknown_key_inside_a_list_element_and_a_map_value_are_named()
    {
        using var f = new SettingsFixture();
        (await Rejected(f, """{ "bogus": true }""")).Message.Should().Contain("bogus");
        (await Rejected(f, """{ "dock": { "pins": [ { "id": "aumid:A", "kind": "app", "target": "A", "color": "red" } ] } }""")).Message.Should().Contain("dock.pins[0].color");
        (await Rejected(f, """{ "desktop": { "placements": { "fs:x": { "x": 1, "y": 2, "z": 3 } } } }""")).Message.Should().Contain("desktop.placements.fs:x.z");
    }

    [Fact]
    public async Task Version_is_not_patchable()
    {
        using var f = new SettingsFixture();
        (await Rejected(f, """{ "version": 5 }""")).Message.Should().Contain("version");
        f.Service.Current.Version.Should().Be(1);
    }

    [Theory]
    [InlineData("[1]")]
    [InlineData("\"x\"")]
    [InlineData("5")]
    public async Task A_patch_that_is_not_an_object_is_rejected(string json)
    {
        using var f = new SettingsFixture();
        (await Rejected(f, json)).Code.Should().Be("badRequest");
    }

    [Theory]
    [InlineData("""{ "dock": { "iconSize": "abc" } }""", "dock.iconSize")]
    [InlineData("""{ "dock": { "position": "sideways" } }""", "dock.position")]
    [InlineData("""{ "dock": "x" }""", "dock")]
    [InlineData("""{ "dock": { "pins": {} } }""", "dock.pins")]
    [InlineData("""{ "general": { "autostart": "yes" } }""", "general.autostart")]
    public async Task Wrong_types_and_unknown_enum_values_are_rejected_with_their_path(string json, string path)
    {
        using var f = new SettingsFixture();
        (await Rejected(f, json)).Message.Should().Contain(path);
    }

    [Fact]
    public async Task A_numeric_string_is_accepted_for_an_int()
    {
        using var f = new SettingsFixture();
        (await f.Service.PatchAsync(P("""{ "dock": { "iconSize": "72" } }"""))).Dock.IconSize.Should().Be(72);
    }

    [Fact]
    public async Task A_patch_larger_than_256_KB_is_rejected()
    {
        using var f = new SettingsFixture();
        var huge = new JsonObject { ["desktop"] = new JsonObject { ["widgets"] = new JsonArray(Enumerable.Range(0, 3000).Select(i => (JsonNode)new JsonObject { ["id"] = "w" + i, ["config"] = new JsonObject { ["text"] = new string('x', 100) } }).ToArray()) } };
        (await Rejected(f, huge.ToJsonString())).Message.Should().Contain("KB");
    }

    [Fact]
    public async Task Numbers_are_clamped()
    {
        using var f = new SettingsFixture();
        var s = await f.Service.PatchAsync(P("""{ "dock": { "iconSize": 1000, "magnifiedSize": 5 }, "spotlight": { "maxResults": 0 }, "hotCorners": { "dwellMs": 99999 }, "desktop": { "iconSize": -4 } }"""));
        s.Dock.IconSize.Should().Be(128);
        s.Dock.MagnifiedSize.Should().Be(128, "magnifiedSize is at least iconSize");
        s.Spotlight.MaxResults.Should().Be(1);
        s.HotCorners.DwellMs.Should().Be(2000);
        s.Desktop.IconSize.Should().Be(32);
    }

    [Fact]
    public async Task Magnified_size_follows_a_later_icon_size_increase()
    {
        using var f = new SettingsFixture();
        await f.Service.PatchAsync(P("""{ "dock": { "magnifiedSize": 60, "iconSize": 40 } }"""));
        var s = await f.Service.PatchAsync(P("""{ "dock": { "iconSize": 100 } }"""));
        s.Dock.MagnifiedSize.Should().Be(100);
        (await f.Service.PatchAsync(P("""{ "dock": { "magnifiedSize": 500 } }"""))).Dock.MagnifiedSize.Should().Be(192);
    }

    [Fact]
    public async Task Patch_result_equals_Current_and_raises_Changed_once()
    {
        using var f = new SettingsFixture();
        var seen = new List<SettingsDtoAlias>();
        f.Service.Changed += s => seen.Add(s);
        var r = await f.Service.PatchAsync(P("""{ "menuBar": { "showSeconds": true } }"""));
        seen.Should().HaveCount(1);
        ProtocolJson.Serialize(seen[0]).Should().Be(ProtocolJson.Serialize(r)).And.Be(ProtocolJson.Serialize(f.Service.Current));
    }

    [Fact]
    public async Task A_patch_that_changes_nothing_raises_no_event_and_schedules_no_write()
    {
        using var f = new SettingsFixture();
        var n = 0;
        f.Service.Changed += _ => n++;
        await f.Service.PatchAsync(P("""{ "dock": { "iconSize": 56 } }"""));
        n.Should().Be(0);
        f.PassDebounce();
        File.Exists(f.ConfigFile).Should().BeFalse();
    }

    [Fact]
    public async Task Cancelled_token_cancels_the_call()
    {
        using var f = new SettingsFixture();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var act = async () => await f.Service.PatchAsync(P("{}"), cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }
}
