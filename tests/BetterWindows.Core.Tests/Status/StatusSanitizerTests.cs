using BetterWindows.Core.Protocol;
using BetterWindows.Core.Status;

namespace BetterWindows.Core.Tests.Status;

/// <summary>Alias so other tests can reach the sanitizer without a namespace clash between Core.Status and Tests.Status.</summary>
internal static class StatusSanitizerAccess
{
    public static AudioState Audio(AudioState s) => StatusSanitizer.Audio(s);
}

public class StatusSanitizerTests
{
    [Theory]
    [InlineData(0.5, 0.5)]
    [InlineData(-1, 0)]
    [InlineData(2, 1)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    [InlineData(double.NegativeInfinity, 0)]
    public void Clamp01(double input, double expected) => StatusSanitizer.Clamp01(input).Should().Be(expected);

    [Theory]
    [InlineData(10, 10)]
    [InlineData(-3, 0)]
    [InlineData(double.NaN, 0)]
    [InlineData(double.PositiveInfinity, 0)]
    public void Seconds(double input, double expected) => StatusSanitizer.Seconds(input).Should().Be(expected);

    [Fact]
    public void Battery_percent_is_clamped_and_negative_minutes_dropped()
    {
        StatusSanitizer.Battery(new BatteryState { Percent = -4 }).Percent.Should().Be(0);
        StatusSanitizer.Battery(new BatteryState { Percent = 101 }).Percent.Should().Be(100);
        StatusSanitizer.Battery(new BatteryState { MinutesRemaining = -1 }).MinutesRemaining.Should().BeNull();
        StatusSanitizer.Battery(new BatteryState { MinutesRemaining = 90 }).MinutesRemaining.Should().Be(90);
    }

    [Fact]
    public void Network_signal_bars_are_clamped_and_the_name_cleaned()
    {
        var n = StatusSanitizer.Network(new NetworkState { SignalBars = -2, Name = "a\nb" });
        n.SignalBars.Should().Be(0);
        n.Name.Should().Be("a b");
        StatusSanitizer.Network(new NetworkState { SignalBars = 9 }).SignalBars.Should().Be(4);
        StatusSanitizer.Network(new NetworkState { Name = null }).Name.Should().BeNull();
    }

    [Fact]
    public void Bluetooth_devices_are_capped_and_battery_clamped()
    {
        var devices = Enumerable.Range(0, 100).Select(i => new BluetoothDevice { Id = "b" + i, Name = "n", BatteryPercent = i == 0 ? 300 : null }).ToArray();
        var s = StatusSanitizer.Bluetooth(new BluetoothState { Supported = true, Devices = devices });
        s.Devices.Should().HaveCount(StatusSanitizer.MaxDevices);
        s.Devices[0].BatteryPercent.Should().Be(100);
        s.Devices[1].BatteryPercent.Should().BeNull();
    }

    [Fact]
    public void Media_position_never_exceeds_a_known_duration_and_text_is_bounded()
    {
        var m = StatusSanitizer.Media(new MediaState { Available = true, Title = new string('t', 1000), PositionSec = 500, DurationSec = 200 });
        m.PositionSec.Should().Be(200);
        m.Title!.Length.Should().Be(256);
        StatusSanitizer.Media(new MediaState { PositionSec = 30, DurationSec = 0 }).PositionSec.Should().Be(30, "unknown duration: keep the position");
        StatusSanitizer.Media(new MediaState { PositionSec = double.NaN, DurationSec = -4 }).Should().Match<MediaState>(x => x.PositionSec == 0 && x.DurationSec == 0);
    }

    [Fact]
    public void Sanitizing_never_mutates_the_input()
    {
        var input = new AudioState { Volume = 5, DeviceName = new string('x', 999) };
        StatusSanitizer.Audio(input);
        input.Volume.Should().Be(5);
        input.DeviceName!.Length.Should().Be(999);
    }

    [Fact]
    public void Text_removes_control_characters()
    {
        StatusSanitizer.Text("a\u0000b\tc").Should().Be("a b c");
        StatusSanitizer.Text(null).Should().BeNull();
        StatusSanitizer.Text("abcdef", 3).Should().Be("abc");
    }

    [Fact]
    public void RevisionTracker_increments_only_when_the_key_changes_and_is_idempotent()
    {
        var t = new RevisionTracker();
        t.Current.Should().Be(0);
        t.Observe("a").Should().Be(1);
        t.Observe("a").Should().Be(1);
        t.Observe("a").Should().Be(1);
        t.Observe("b").Should().Be(2);
        t.Observe("a").Should().Be(3, "a different key than the last observed one");
        t.Current.Should().Be(3);
    }
}
