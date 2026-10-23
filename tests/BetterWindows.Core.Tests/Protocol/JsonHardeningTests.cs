using System.Text.Json;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Protocol;

public class JsonHardeningTests
{
    [Fact]
    public void Non_finite_doubles_serialise_as_zero()
    {
        ProtocolJson.Serialize(new MediaState { PositionSec = double.NaN, DurationSec = double.NegativeInfinity }).Should().Contain("\"positionSec\":0").And.Contain("\"durationSec\":0");
    }

    [Theory]
    [InlineData("1.5", 2)]
    [InlineData("2.0", 2)]
    [InlineData("1e2", 100)]
    [InlineData("-1.5", -2)]
    [InlineData("\"7\"", 7)]
    [InlineData("3000000000", int.MaxValue)]
    public void Int_fields_accept_any_number(string literal, int expected)
    {
        ProtocolJson.Deserialize<DockPinParams>("{\"appId\":\"a\",\"index\":" + literal + "}")!.Index.Should().Be(expected);
    }

    [Fact]
    public void Explicit_null_keeps_defaults_for_non_nullable_members_and_nested_records()
    {
        var s = ProtocolJson.Deserialize<Settings>("{\"dock\":null,\"appearance\":{\"accent\":null}}")!;
        s.Dock.Should().NotBeNull();
        s.Dock.IconSize.Should().Be(56);
        s.Appearance.Accent.Should().Be("system");
    }

    [Fact]
    public void Null_for_a_non_nullable_enum_or_number_is_an_error_not_a_silent_default()
    {
        // (config loading normalises nulls on the raw JSON tree before binding; the wire protocol rejects them)
        var act = () => ProtocolJson.Deserialize<Settings>("{\"appearance\":{\"mode\":null}}");
        act.Should().Throw<JsonException>();
    }

    [Fact]
    public void Nullable_members_still_accept_null()
    {
        var p = ProtocolJson.Deserialize<DockPinParams>("{\"appId\":\"a\",\"index\":null}")!;
        p.Index.Should().BeNull();
    }

    [Fact]
    public void Ui_input_types_require_their_non_defaulted_fields()
    {
        var act = () => ProtocolJson.Deserialize<DockItemParams>("{}");
        act.Should().Throw<JsonException>();
        ProtocolJson.Deserialize<WeatherGetParams>("{}")!.Force.Should().BeFalse(); // has a default
        ProtocolJson.Deserialize<PickPathParams>("{\"kind\":\"file\"}")!.Multiple.Should().BeFalse();
    }

    [Fact]
    public void Host_to_ui_types_stay_lenient_about_missing_fields()
    {
        ProtocolJson.Deserialize<DockItem>("{\"id\":\"x\"}")!.Name.Should().Be("");
    }

    [Fact]
    public void Settings_limits_come_from_the_protocol_json()
    {
        SettingsLimits.All["DockSettings.iconSize"].Should().Be(new FieldLimit(Min: 32, Max: 128));
        SettingsLimits.All["AppearanceSettings.accent"].Pattern.Should().NotBeNullOrEmpty();
        SettingsLimits.All["DockSettings.pins"].MaxItems.Should().Be(100);
    }

    [Fact]
    public void Initial_state_table_matches_the_protocol()
    {
        InitialState.For(SurfaceKind.Dock).Should().Contain(new[] { Msg.DockState, Msg.SettingsChanged, Msg.ThemeChanged, Msg.LocaleChanged });
        InitialState.For(SurfaceKind.Dock).Should().NotContain(Msg.StatusBattery);
        InitialState.For(SurfaceKind.Menubar).Should().Contain(new[] { Msg.StatusBattery, Msg.ActiveAppChanged, Msg.TrayState });
        InitialState.For(SurfaceKind.Desktop).Should().Contain(Msg.DesktopState).And.NotContain(Msg.StatusAudio);
        InitialState.IsState(Msg.DockBounce).Should().BeFalse();
    }
}
