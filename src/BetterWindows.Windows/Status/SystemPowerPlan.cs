using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Status;

/// <summary>Which Win32 mechanism performs a power action.</summary>
internal enum PowerMechanism { LockWorkStation, Suspend, Hibernate, ExitWindows, ShutdownExe }

/// <summary>
/// One planned power step. For <see cref="PowerMechanism.ExitWindows"/> the flags are EWX_* values; <see cref="ShutdownArguments"/> is the fixed argument
/// list of the shutdown.exe fallback (never user-supplied).
/// </summary>
internal readonly record struct PowerPlanStep(PowerMechanism Mechanism, uint ExitFlags, string ShutdownArguments);

/// <summary>
/// Maps protocol power actions onto Win32 mechanisms (pure: the actual calls live in <c>Win32SystemPower</c>, and tests check only this mapping; no power
/// action is executed by tests).
/// </summary>
internal static class SystemPowerPlan
{
    /// <summary>EWX_LOGOFF.</summary>
    public const uint ExitLogoff = 0x00000000;
    /// <summary>EWX_SHUTDOWN.</summary>
    public const uint ExitShutdown = 0x00000001;
    /// <summary>EWX_REBOOT.</summary>
    public const uint ExitReboot = 0x00000002;
    /// <summary>SHTDN_REASON_FLAG_PLANNED: marks the shutdown as user-initiated so event-log consumers do not treat it as a crash.</summary>
    public const uint ReasonPlanned = 0x80000000;

    /// <summary>
    /// Plan for one action. Hibernate falls back to <c>shutdown.exe /h</c> when hibernation is disabled (the documented way to hibernate without the
    /// powrprof capability); restart, shutdown and logout use ExitWindowsEx and fall back to the matching shutdown.exe switches.
    /// </summary>
    public static PowerPlanStep Plan(PowerAction action, bool hibernationEnabled) => action switch
    {
        PowerAction.Lock => new PowerPlanStep(PowerMechanism.LockWorkStation, 0, ""),
        PowerAction.Sleep => new PowerPlanStep(PowerMechanism.Suspend, 0, ""),
        PowerAction.Hibernate => hibernationEnabled
            ? new PowerPlanStep(PowerMechanism.Hibernate, 0, "")
            : new PowerPlanStep(PowerMechanism.ShutdownExe, 0, "/h"),
        PowerAction.Restart => new PowerPlanStep(PowerMechanism.ExitWindows, ExitReboot, "/r /t 0"),
        PowerAction.Shutdown => new PowerPlanStep(PowerMechanism.ExitWindows, ExitShutdown, "/s /t 0"),
        PowerAction.Logout => new PowerPlanStep(PowerMechanism.ExitWindows, ExitLogoff, "/l"),
        _ => throw new ArgumentOutOfRangeException(nameof(action), action, "Unknown power action."),
    };
}
