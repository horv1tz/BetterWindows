using System.Text.Json;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Protocol;

public class ProtocolTests
{
    [Fact]
    public void Default_settings_roundtrip_and_use_camelCase_enums()
    {
        var json = ProtocolJson.Serialize(new Settings());
        json.Should().Contain("\"position\":\"bottom\"").And.Contain("\"mode\":\"auto\"").And.Contain("\"missionControl\"");
        var back = ProtocolJson.Deserialize<Settings>(json)!;
        back.Dock.IconSize.Should().Be(56);
        back.HotCorners.TopLeft.Should().Be(HotCornerAction.MissionControl);
        back.Spotlight.Hotkey.Should().Be("Alt+Space");
    }

    [Fact]
    public void Partial_json_keeps_defaults_and_ignores_unknown_fields()
    {
        var s = ProtocolJson.Deserialize<Settings>("{\"dock\":{\"iconSize\":72,\"future\":1},\"zzz\":true}")!;
        s.Dock.IconSize.Should().Be(72);
        s.Dock.Magnification.Should().BeTrue();
        s.MenuBar.Use24Hour.Should().BeTrue();
    }

    [Fact]
    public void Message_catalog_is_consistent()
    {
        var all = MessageCatalog.All;
        all.Select(m => m.Type).Should().OnlyHaveUniqueItems();
        all.Should().Contain(m => m.Type == Msg.SurfaceReady && m.Kind == MessageKind.Call && m.Result == typeof(SurfaceInit));
        all.Should().Contain(m => m.Type == Msg.DockState && m.Kind == MessageKind.HostEvent && m.Payload == typeof(DockState));
        all.Should().Contain(m => m.Type == Msg.SurfaceResize && m.Kind == MessageKind.UiEvent);
        all.Count(m => m.Kind == MessageKind.Call).Should().BeGreaterThan(40);
    }

    [Fact]
    public void Any_fields_carry_arbitrary_json()
    {
        var w = ProtocolJson.Deserialize<WidgetInstance>("{\"id\":\"w1\",\"type\":\"notes\",\"config\":{\"text\":\"hi\",\"n\":[1,2]}}")!;
        w.Type.Should().Be(WidgetType.Notes);
        w.Config!["text"]!.GetValue<string>().Should().Be("hi");
        ProtocolJson.Serialize(w).Should().Contain("\"config\":{\"text\":\"hi\"");
    }

    [Fact]
    public void Hwnd_wire_format_roundtrips_64bit()
    {
        var wire = Hwnd.ToWire(0x7FFF_FFFF_FFFFL);
        Hwnd.Parse(wire).Should().Be(0x7FFF_FFFF_FFFFL);
        Hwnd.TryParse("abc", out _).Should().BeFalse();
    }

    [Fact]
    public void Unknown_enum_value_fails_deserialisation()
    {
        var act = () => ProtocolJson.Deserialize<DockSettings>("{\"position\":\"top\"}");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void DynUrls_are_relative_and_escaped()
    {
        DynUrls.AppIcon("aumid:Microsoft.Edge_8wekyb3d8bbwe!App", 64).Should().StartWith("/dyn/icon/app/aumid%3A").And.EndWith("?s=64");
        DynUrls.WindowThumb(12345, 320, 2).Should().Be("/dyn/thumb/12345?w=320&rev=2");
    }
}
