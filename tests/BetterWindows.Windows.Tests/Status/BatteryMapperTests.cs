using BetterWindows.Core.Protocol;
using BetterWindows.Native.Status;

namespace BetterWindows.Native.Tests.Status;

public class BatteryMapperTests
{
    private static BatteryInputs Inputs(
        bool winRt = false, int status = 0, int percent = -1, int supply = 0, TimeSpan? remaining = null,
        bool win32 = false, byte ac = 255, byte flag = 255, byte life = 255, uint seconds = BatteryMapper.UnknownSeconds) =>
        new(winRt, status, percent, supply, remaining ?? TimeSpan.Zero, win32, ac, flag, life, seconds);

    [Fact]
    public void Desktop_without_battery_is_not_present_and_on_mains()
    {
        var s = BatteryMapper.Map(Inputs(winRt: true, status: 0, win32: true, ac: 1, flag: 128, life: 255));
        s.Should().BeEquivalentTo(new BatteryState { Present = false, Percent = 0, Charging = false, AcOnline = true, MinutesRemaining = null });
    }

    [Fact]
    public void Discharging_laptop_reports_percent_and_minutes_from_winrt()
    {
        var s = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtDischarging, percent: 50, supply: 1,
            remaining: TimeSpan.FromHours(2), win32: true, ac: 0, flag: 1, life: 50, seconds: 3600));
        s.Present.Should().BeTrue();
        s.Percent.Should().Be(50);
        s.Charging.Should().BeFalse();
        s.AcOnline.Should().BeFalse();
        s.MinutesRemaining.Should().Be(120);
    }

    [Fact]
    public void Charging_on_mains_has_no_remaining_estimate()
    {
        var s = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtCharging, percent: 70, supply: 2,
            remaining: TimeSpan.FromHours(1), win32: true, ac: 1, flag: BatteryMapper.FlagCharging, life: 70, seconds: 1800));
        s.Charging.Should().BeTrue();
        s.AcOnline.Should().BeTrue();
        s.MinutesRemaining.Should().BeNull();
    }

    [Fact]
    public void Win32_only_uses_flag_ac_percent_and_lifetime()
    {
        var s = BatteryMapper.Map(Inputs(win32: true, ac: 0, flag: 1, life: 80, seconds: 3600));
        s.Should().BeEquivalentTo(new BatteryState { Present = true, Percent = 80, Charging = false, AcOnline = false, MinutesRemaining = 60 });
    }

    [Fact]
    public void Unknown_win32_presence_falls_back_to_winrt()
    {
        var notPresent = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtNotPresent, win32: true, flag: BatteryMapper.FlagUnknown));
        notPresent.Present.Should().BeFalse();
        var present = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtDischarging, percent: 40, win32: true, flag: BatteryMapper.FlagUnknown, ac: 0));
        present.Present.Should().BeTrue();
        present.Percent.Should().Be(40);
    }

    [Fact]
    public void Zero_winrt_percent_while_initialising_yields_to_win32()
    {
        var s = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtIdle, percent: 0, win32: true, ac: 1, flag: 0, life: 60));
        s.Percent.Should().Be(60);
    }

    [Fact]
    public void Out_of_range_percent_without_win32_is_zero()
    {
        var s = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtDischarging, percent: 150, supply: 1));
        s.Percent.Should().Be(0);
    }

    [Fact]
    public void Implausible_winrt_remaining_time_is_ignored_in_favour_of_win32()
    {
        var fromWin32 = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtDischarging, percent: 30, supply: 1,
            remaining: TimeSpan.FromDays(30), win32: true, ac: 0, flag: 2, life: 30, seconds: 600));
        fromWin32.MinutesRemaining.Should().Be(10);

        var none = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtDischarging, percent: 30, supply: 1,
            remaining: TimeSpan.Zero, win32: true, ac: 0, flag: 2, life: 30, seconds: BatteryMapper.UnknownSeconds));
        none.MinutesRemaining.Should().BeNull();
    }

    [Fact]
    public void Missing_ac_line_status_uses_winrt_supply_and_charging_implies_mains()
    {
        var s = BatteryMapper.Map(Inputs(winRt: true, status: BatteryMapper.WinRtDischarging, percent: 90, supply: BatteryMapper.WinRtSupplyAdequate));
        s.AcOnline.Should().BeTrue();
    }

    [Fact]
    public void Same_compares_every_field()
    {
        var a = new BatteryState { Present = true, Percent = 10, Charging = true, AcOnline = true, MinutesRemaining = 5 };
        BatteryMapper.Same(a, a with { }).Should().BeTrue();
        BatteryMapper.Same(a, a with { Percent = 11 }).Should().BeFalse();
        BatteryMapper.Same(a, a with { MinutesRemaining = null }).Should().BeFalse();
    }
}
