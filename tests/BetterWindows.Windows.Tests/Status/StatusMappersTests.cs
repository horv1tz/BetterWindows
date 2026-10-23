using BetterWindows.Core.Protocol;
using BetterWindows.Native.Status;

namespace BetterWindows.Native.Tests.Status;

public class NetworkMapperTests
{
    [Theory]
    [InlineData(0, false)]
    [InlineData(1, true)]
    [InlineData(2, true)]
    [InlineData(3, true)]
    [InlineData(4, false)]
    public void Connectivity_levels_map_to_connected(int level, bool expected) =>
        NetworkMapper.IsConnected(level).Should().Be(expected);

    [Fact]
    public void Missing_profile_is_kind_none()
    {
        NetworkMapper.Kind(hasProfile: false, isWlan: true, isWwan: false, ianaInterfaceType: 71).Should().Be(NetworkKind.None);
    }

    [Theory]
    [InlineData(true, false, 6, NetworkKind.Wifi)]          // WLAN profile wins over the adapter type
    [InlineData(false, false, 6, NetworkKind.Ethernet)]
    [InlineData(false, false, 71, NetworkKind.Wifi)]
    [InlineData(false, false, 243, NetworkKind.Cellular)]
    [InlineData(false, false, 244, NetworkKind.Cellular)]
    [InlineData(false, true, 999, NetworkKind.Cellular)]    // WWAN flag without a known IANA id
    [InlineData(false, false, 24, NetworkKind.Other)]       // loopback
    [InlineData(false, false, null, NetworkKind.Other)]
    public void Profile_facts_map_to_kind(bool isWlan, bool isWwan, int? iana, NetworkKind expected) =>
        NetworkMapper.Kind(hasProfile: true, isWlan: isWlan, isWwan: isWwan, ianaInterfaceType: iana).Should().Be(expected);

    [Theory]
    [InlineData(null, 0)]
    [InlineData(-1, 0)]
    [InlineData(0, 0)]
    [InlineData(3, 3)]
    [InlineData(4, 4)]
    [InlineData(5, 4)]
    [InlineData(9, 4)]
    public void Signal_bars_are_clamped_to_zero_to_four(int? raw, int expected) =>
        NetworkMapper.SignalBars(raw).Should().Be(expected);

    [Fact]
    public void Same_detects_each_field()
    {
        var a = new NetworkState { Kind = NetworkKind.Wifi, Connected = true, Name = "home", SignalBars = 3 };
        NetworkMapper.Same(a, a with { }).Should().BeTrue();
        NetworkMapper.Same(a, a with { SignalBars = 2 }).Should().BeFalse();
        NetworkMapper.Same(a, a with { Name = null }).Should().BeFalse();
    }
}

public class BluetoothMapperTests
{
    [Theory]
    [InlineData(null, null)]
    [InlineData((byte)85, 85)]
    [InlineData((byte)0, 0)]
    [InlineData((ushort)100, 100)]
    [InlineData(101, null)]
    [InlineData(-1, null)]
    [InlineData(3.5, null)]
    [InlineData("50", null)]
    [InlineData(true, null)]
    public void Battery_property_parsing_rejects_out_of_range_and_non_integral(object? raw, int? expected) =>
        BluetoothMapper.ParseBatteryPercent(raw).Should().Be(expected);

    [Fact]
    public void Device_comparison_is_element_wise_and_order_sensitive()
    {
        var a = new[] { new BluetoothDevice { Id = "1", Name = "Mouse", Connected = true, BatteryPercent = 40 } };
        var same = new[] { new BluetoothDevice { Id = "1", Name = "Mouse", Connected = true, BatteryPercent = 40 } };
        BluetoothMapper.SameDevices(a, same).Should().BeTrue();
        BluetoothMapper.SameDevices(a, new[] { same[0] with { BatteryPercent = 39 } }).Should().BeFalse();
        BluetoothMapper.SameDevices(a, Array.Empty<BluetoothDevice>()).Should().BeFalse();
    }

    [Fact]
    public void State_comparison_covers_flags_and_devices()
    {
        var s = new BluetoothState { Supported = true, Enabled = true, Devices = Array.Empty<BluetoothDevice>() };
        BluetoothMapper.Same(s, s with { Enabled = false }).Should().BeFalse();
        BluetoothMapper.Same(s, s with { Supported = false }).Should().BeFalse();
        BluetoothMapper.Same(s, s with { Devices = new[] { new BluetoothDevice { Id = "x" } } }).Should().BeFalse();
    }
}

public class AudioMapperTests
{
    private static readonly (string Id, string Name)[] Endpoints = { ("a", "Speakers"), ("b", "Headphones") };

    [Theory]
    [InlineData(double.NaN, 0.0)]
    [InlineData(double.PositiveInfinity, 0.0)]
    [InlineData(1.5, 1.0)]
    [InlineData(-0.2, 0.0)]
    [InlineData(0.4, 0.4)]
    public void Volume_is_clamped_and_never_nan(double input, double expected) =>
        AudioMapper.ClampVolume(input).Should().Be(expected);

    [Fact]
    public void Build_marks_the_default_and_takes_its_name_volume_and_mute()
    {
        var s = AudioMapper.Build(Endpoints, "b", 0.25, true);
        s.DeviceId.Should().Be("b");
        s.DeviceName.Should().Be("Headphones");
        s.Volume.Should().Be(0.25);
        s.Muted.Should().BeTrue();
        s.Devices.Select(d => d.IsDefault).Should().Equal(false, true);
    }

    [Fact]
    public void Build_without_default_reports_silence_and_no_default_flags()
    {
        var s = AudioMapper.Build(Endpoints, null, 0.9, true);
        s.DeviceId.Should().BeNull();
        s.DeviceName.Should().BeNull();
        s.Volume.Should().Be(0.0);
        s.Muted.Should().BeFalse();
        s.Devices.Should().OnlyContain(d => !d.IsDefault);
    }

    [Fact]
    public void Default_id_missing_from_the_list_is_treated_as_no_default()
    {
        var s = AudioMapper.Build(Endpoints, "gone", 0.5, false);
        s.DeviceId.Should().BeNull();
        s.Volume.Should().Be(0.0);
    }

    [Fact]
    public void Empty_endpoint_list_is_a_valid_state()
    {
        var s = AudioMapper.Build(Array.Empty<(string, string)>(), null, null, null);
        s.Devices.Should().BeEmpty();
        s.DeviceId.Should().BeNull();
    }

    [Fact]
    public void Same_compares_devices_and_scalars()
    {
        var a = AudioMapper.Build(Endpoints, "a", 0.5, false);
        AudioMapper.Same(a, AudioMapper.Build(Endpoints, "a", 0.5, false)).Should().BeTrue();
        AudioMapper.Same(a, AudioMapper.Build(Endpoints, "a", 0.6, false)).Should().BeFalse();
        AudioMapper.Same(a, AudioMapper.Build(Endpoints, "b", 0.5, false)).Should().BeFalse();
        AudioMapper.Same(a, AudioMapper.Build(new[] { ("a", "Renamed"), ("b", "Headphones") }, "a", 0.5, false)).Should().BeFalse();
    }
}

public class BrightnessMathTests
{
    [Theory]
    [InlineData(0.5, 50)]
    [InlineData(double.NaN, 0)]
    [InlineData(1.7, 100)]
    [InlineData(-1.0, 0)]
    [InlineData(0.505, 51)]
    public void Level_to_percent_rounds_half_away_and_clamps(double level, int percent) =>
        BrightnessMath.ToPercent(level).Should().Be(percent);

    [Theory]
    [InlineData(0, 0.0)]
    [InlineData(25, 0.25)]
    [InlineData(150, 1.0)]
    [InlineData(-3, 0.0)]
    public void Percent_to_level_clamps(int percent, double level) =>
        BrightnessMath.FromPercent(percent).Should().Be(level);

    [Fact]
    public void Same_compares_support_and_level()
    {
        var a = new BrightnessState { Supported = true, Level = 0.5 };
        BrightnessMath.Same(a, a with { }).Should().BeTrue();
        BrightnessMath.Same(a, a with { Level = 0.6 }).Should().BeFalse();
        BrightnessMath.Same(a, a with { Supported = false }).Should().BeFalse();
    }
}

public class ThemeMathTests
{
    [Fact]
    public void Hex_is_lower_case_and_padded()
    {
        ThemeMath.Hex(0, 0, 0).Should().Be("#000000");
        ThemeMath.Hex(255, 128, 1).Should().Be("#ff8001");
    }

    [Fact]
    public void Registry_accent_is_abgr_so_the_low_byte_is_red()
    {
        ThemeMath.FromAbgr(0xFF_FF_00_00).Should().Be("#0000ff");   // A=FF B=FF G=00 R=00 -> blue
        ThemeMath.FromAbgr(0x00_33_66_99).Should().Be("#996633");   // B=33 G=66 R=99
        ThemeMath.FromAbgr(0xFF_00_00_FF).Should().Be("#ff0000");   // red
    }

    [Theory]
    [InlineData("#ABCDEF", "#abcdef")]
    [InlineData("#0078d4", "#0078d4")]
    [InlineData("#abc", null)]
    [InlineData("abcdef", null)]
    [InlineData("#GG0000", null)]
    [InlineData(null, null)]
    public void Only_six_digit_hex_is_accepted(string? input, string? expected) =>
        ThemeMath.NormalizeHex(input).Should().Be(expected);

    [Theory]
    [InlineData(0, false, true)]
    [InlineData(1, true, true)]
    [InlineData(1, false, false)]
    [InlineData(null, false, false)]
    public void Reduce_transparency_is_disabled_transparency_or_high_contrast(int? enableTransparency, bool highContrast, bool expected) =>
        ThemeMath.ReduceTransparency(enableTransparency, highContrast).Should().Be(expected);

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(null, false)]
    public void Reduce_motion_follows_client_area_animation_and_unknown_keeps_animations(bool? animationsOn, bool expected) =>
        ThemeMath.ReduceMotion(animationsOn).Should().Be(expected);

    [Fact]
    public void Same_compares_every_field()
    {
        var a = new ThemeSnapshot(true, "#0078d4", false, false);
        ThemeMath.Same(a, a with { }).Should().BeTrue();
        ThemeMath.Same(a, a with { AppsUseLightTheme = false }).Should().BeFalse();
        ThemeMath.Same(a, a with { AccentColorHex = "#000000" }).Should().BeFalse();
        ThemeMath.Same(a, a with { ReduceMotion = true }).Should().BeFalse();
        ThemeMath.Same(a, a with { ReduceTransparency = true }).Should().BeFalse();
    }
}

public class SmtcMathTests
{
    private static readonly DateTimeOffset T0 = new(2026, 10, 23, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Playing_position_advances_by_time_since_last_update()
    {
        SmtcMath.PositionSeconds(TimeSpan.FromSeconds(10), T0, playing: true, now: T0.AddSeconds(5), durationSeconds: 100).Should().Be(15);
    }

    [Fact]
    public void Paused_position_does_not_advance()
    {
        SmtcMath.PositionSeconds(TimeSpan.FromSeconds(10), T0, playing: false, now: T0.AddSeconds(5), durationSeconds: 100).Should().Be(10);
    }

    [Fact]
    public void Position_is_clamped_to_duration_and_unbounded_when_duration_unknown()
    {
        SmtcMath.PositionSeconds(TimeSpan.FromSeconds(50), T0, true, T0.AddSeconds(60), 100).Should().Be(100);
        SmtcMath.PositionSeconds(TimeSpan.FromSeconds(50), T0, true, T0.AddSeconds(60), 0).Should().Be(110);
    }

    [Fact]
    public void Negative_or_future_inputs_never_produce_negative_or_extra_time()
    {
        SmtcMath.PositionSeconds(TimeSpan.FromSeconds(-4), T0, false, T0, 100).Should().Be(0);
        SmtcMath.PositionSeconds(TimeSpan.FromSeconds(3), T0.AddMinutes(1), true, T0, 100).Should().Be(3);
        SmtcMath.PositionSeconds(TimeSpan.FromSeconds(3), default, true, T0, 100).Should().Be(3);
    }

    [Fact]
    public void Duration_is_end_minus_start_and_never_negative()
    {
        SmtcMath.DurationSeconds(TimeSpan.Zero, TimeSpan.FromMinutes(3)).Should().Be(180);
        SmtcMath.DurationSeconds(TimeSpan.FromMinutes(3), TimeSpan.Zero).Should().Be(0);
    }

    [Theory]
    [InlineData(4, true)]
    [InlineData(5, false)]
    [InlineData(0, false)]
    public void Only_the_playing_status_counts_as_playing(int status, bool expected) => SmtcMath.IsPlaying(status).Should().Be(expected);

    [Theory]
    [InlineData(1.5, 15_000_000L)]
    [InlineData(0.0, 0L)]
    [InlineData(-1.0, null)]
    [InlineData(double.NaN, null)]
    [InlineData(double.PositiveInfinity, null)]
    public void Seek_requests_are_converted_to_hundred_nanosecond_ticks(double seconds, long? expected) =>
        SmtcMath.SeekTicks(seconds).Should().Be(expected);

    [Fact]
    public void Seek_without_a_position_sends_nothing()
    {
        SmtcMath.SeekTicks(null).Should().BeNull();
    }

    [Theory]
    [InlineData("Spotify.exe", "Spotify")]
    [InlineData("Microsoft.ZuneMusic_8wekyb3d8bbwe!Microsoft.ZuneMusic", "ZuneMusic")]
    [InlineData(@"C:\Program Files\App\tool.exe", "tool")]
    [InlineData("Chrome", "Chrome")]
    [InlineData("", null)]
    [InlineData(null, null)]
    [InlineData("   ", null)]
    public void App_name_is_derived_from_the_app_user_model_id(string? aumid, string? expected) =>
        SmtcMath.AppNameFromAumid(aumid).Should().Be(expected);

    [Fact]
    public void Media_state_comparison_covers_all_fields()
    {
        var a = new MediaState { Available = true, Title = "t", PositionSec = 1, DurationSec = 2, CanPlay = true };
        SmtcMath.Same(a, a with { }).Should().BeTrue();
        SmtcMath.Same(a, a with { Title = "u" }).Should().BeFalse();
        SmtcMath.Same(a, a with { CanSeek = true }).Should().BeFalse();
        SmtcMath.Same(a, a with { ThumbUrl = "https://app.local/dyn/art/1" }).Should().BeFalse();
    }
}

public class SystemPowerPlanTests
{
    [Fact]
    public void Lock_and_sleep_map_to_their_own_mechanisms()
    {
        SystemPowerPlan.Plan(PowerAction.Lock, false).Mechanism.Should().Be(PowerMechanism.LockWorkStation);
        SystemPowerPlan.Plan(PowerAction.Sleep, true).Mechanism.Should().Be(PowerMechanism.Suspend);
    }

    [Fact]
    public void Hibernate_uses_the_powrprof_call_only_when_hibernation_is_enabled()
    {
        SystemPowerPlan.Plan(PowerAction.Hibernate, true).Mechanism.Should().Be(PowerMechanism.Hibernate);
        var fallback = SystemPowerPlan.Plan(PowerAction.Hibernate, false);
        fallback.Mechanism.Should().Be(PowerMechanism.ShutdownExe);
        fallback.ShutdownArguments.Should().Be("/h");
    }

    [Fact]
    public void Restart_shutdown_and_logout_use_exit_windows_with_their_flags_and_fallbacks()
    {
        var restart = SystemPowerPlan.Plan(PowerAction.Restart, false);
        restart.ExitFlags.Should().Be(SystemPowerPlan.ExitReboot);
        restart.ShutdownArguments.Should().Be("/r /t 0");

        var shutdown = SystemPowerPlan.Plan(PowerAction.Shutdown, false);
        shutdown.ExitFlags.Should().Be(SystemPowerPlan.ExitShutdown);
        shutdown.ShutdownArguments.Should().Be("/s /t 0");

        var logout = SystemPowerPlan.Plan(PowerAction.Logout, false);
        logout.Mechanism.Should().Be(PowerMechanism.ExitWindows);
        logout.ExitFlags.Should().Be(SystemPowerPlan.ExitLogoff);
        logout.ShutdownArguments.Should().Be("/l");
    }

    [Fact]
    public void Exit_windows_reason_is_the_planned_flag()
    {
        SystemPowerPlan.ReasonPlanned.Should().Be(0x80000000u);
    }

    [Fact]
    public void Unknown_action_is_rejected()
    {
        var act = () => SystemPowerPlan.Plan((PowerAction)999, false);
        act.Should().Throw<ArgumentOutOfRangeException>();
    }
}
