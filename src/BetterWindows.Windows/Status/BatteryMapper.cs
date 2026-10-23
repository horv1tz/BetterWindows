using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Status;

/// <summary>
/// Raw battery facts from the two independent OS sources, as plain numbers so the merge logic is a pure function (unit-tested on any OS).
/// WinRT: <c>Windows.System.Power.PowerManager</c> (BatteryStatus NotPresent=0 Discharging=1 Idle=2 Charging=3; PowerSupplyStatus NotPresent=0 Inadequate=1 Adequate=2).
/// Win32: <c>GetSystemPowerStatus</c> (ACLineStatus 0 offline / 1 online / 255 unknown; BatteryFlag bit 1 high, 2 low, 4 critical, 8 charging, 128 NO SYSTEM BATTERY, 255 unknown;
/// BatteryLifePercent 0..100 or 255 unknown; BatteryLifeTime seconds or 0xFFFFFFFF unknown).
/// </summary>
internal readonly record struct BatteryInputs(
    bool WinRtAvailable, int WinRtStatus, int WinRtPercent, int WinRtSupply, TimeSpan WinRtRemaining,
    bool Win32Available, byte AcLineStatus, byte BatteryFlag, byte BatteryLifePercent, uint BatteryLifeTimeSeconds);

/// <summary>Merges WinRT PowerManager values with the GetSystemPowerStatus cross-check into the protocol <see cref="BatteryState"/>.</summary>
internal static class BatteryMapper
{
    public const int WinRtNotPresent = 0, WinRtDischarging = 1, WinRtIdle = 2, WinRtCharging = 3;
    public const int WinRtSupplyAdequate = 2;
    public const byte FlagCharging = 8, FlagNoBattery = 128, FlagUnknown = 255;
    public const uint UnknownSeconds = 0xFFFFFFFF;

    /// <summary>Estimates above this are treated as "unknown" (PowerManager returns TimeSpan.MaxValue-like values when it cannot estimate).</summary>
    private static readonly TimeSpan MaxPlausibleRemaining = TimeSpan.FromDays(7);

    public static BatteryState Map(in BatteryInputs i)
    {
        // --- presence. GetSystemPowerStatus is authoritative for "this machine has no system battery" (flag 128): it is what desktops report and PowerManager
        // sometimes reports NotPresent transiently while a battery re-enumerates. 255 = the OS does not know -> fall back to WinRT.
        bool? win32Present = i.Win32Available && i.BatteryFlag != FlagUnknown ? (i.BatteryFlag & FlagNoBattery) == 0 : null;
        bool? winRtPresent = i.WinRtAvailable ? i.WinRtStatus != WinRtNotPresent : null;
        var present = win32Present ?? winRtPresent ?? false;

        // --- AC line. Win32 first (0/1), WinRT supply status as the fallback. A machine without a battery is by definition on mains.
        bool? ac = i.Win32Available && i.AcLineStatus is 0 or 1 ? i.AcLineStatus == 1 : null;
        ac ??= i.WinRtAvailable ? i.WinRtSupply == WinRtSupplyAdequate : null;

        if (!present)
            return new BatteryState { Present = false, Percent = 0, Charging = false, AcOnline = ac ?? true, MinutesRemaining = null };

        // --- charge level
        var win32Percent = i.Win32Available && i.BatteryLifePercent <= 100 ? (int?)i.BatteryLifePercent : null;
        int? winRtPercent = i.WinRtAvailable && i.WinRtPercent is >= 0 and <= 100 ? i.WinRtPercent : null;
        // PowerManager reports 0 while it is still initialising; if Win32 knows better, believe Win32.
        var percent = winRtPercent is null or 0 && win32Percent is > 0 ? win32Percent.Value : winRtPercent ?? win32Percent ?? 0;

        // --- charging: either source says so (at 100% on mains Win32 clears the bit and WinRT says Idle: both -> not charging)
        var charging = (i.Win32Available && i.BatteryFlag != FlagUnknown && (i.BatteryFlag & FlagCharging) != 0)
                       || (i.WinRtAvailable && i.WinRtStatus == WinRtCharging);
        var acOnline = ac ?? charging;

        // --- remaining runtime: only meaningful while running on battery
        int? minutes = null;
        if (!charging && !acOnline)
        {
            if (i.WinRtAvailable && i.WinRtRemaining > TimeSpan.Zero && i.WinRtRemaining < MaxPlausibleRemaining)
                minutes = Math.Max(1, (int)Math.Round(i.WinRtRemaining.TotalMinutes, MidpointRounding.AwayFromZero));
            else if (i.Win32Available && i.BatteryLifeTimeSeconds != UnknownSeconds && i.BatteryLifeTimeSeconds > 0 && i.BatteryLifeTimeSeconds < MaxPlausibleRemaining.TotalSeconds)
                minutes = Math.Max(1, (int)Math.Round(i.BatteryLifeTimeSeconds / 60.0, MidpointRounding.AwayFromZero));
        }

        return new BatteryState { Present = true, Percent = Math.Clamp(percent, 0, 100), Charging = charging, AcOnline = acOnline, MinutesRemaining = minutes };
    }

    /// <summary>Value equality for snapshots (records compare reference-equal members only for collections; BatteryState has none, but keep one place).</summary>
    public static bool Same(BatteryState a, BatteryState b) =>
        a.Present == b.Present && a.Percent == b.Percent && a.Charging == b.Charging && a.AcOnline == b.AcOnline && a.MinutesRemaining == b.MinutesRemaining;
}
