using System.Diagnostics;
using System.Runtime.InteropServices;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Security;
using Windows.Win32.System.Power;
using Windows.Win32.System.Shutdown;
using static Windows.Win32.PInvoke;

namespace BetterWindows.Native;

/// <summary>
/// Lock, sleep, hibernate, restart, shutdown and logout. The mapping from action to mechanism is <see cref="SystemPowerPlan"/> (pure, tested).
/// Execution runs on a thread-pool worker so the caller never blocks on ExitWindowsEx or SetSuspendState. Every action is logged; failures fall back
/// to the matching <c>shutdown.exe</c> switch with fixed arguments (never UI-supplied). Logout/restart/shutdown take the documented
/// <c>SeShutdownPrivilege</c> first, which is what ExitWindowsEx needs for a shutdown or reboot.
/// </summary>
public sealed class Win32SystemPower : ISystemPower
{
    private readonly ILogger _log;

    public Win32SystemPower(ILogger<Win32SystemPower>? log = null)
    {
        _log = log ?? NullLogger<Win32SystemPower>.Instance;
    }

    /// <inheritdoc />
    public void Perform(PowerAction action)
    {
        // Fire and forget by design: the Core answers the UI immediately; the outcome is logged, not returned.
        _ = Task.Run(() => Execute(action));
    }

    private void Execute(PowerAction action)
    {
        try
        {
            var step = SystemPowerPlan.Plan(action, HibernationEnabled());
            _log.LogInformation("Power action {Action} via {Mechanism}", action, step.Mechanism);
            switch (step.Mechanism)
            {
                case PowerMechanism.LockWorkStation:
                    if (!LockWorkStation()) _log.LogWarning("LockWorkStation failed: {Error}", Marshal.GetLastWin32Error());
                    break;
                case PowerMechanism.Suspend:
                    // bHibernate=false requests S3 sleep. Windows may still hibernate instead when the power plan's sleep action is set to hibernate: that is
                    // the user's own power policy, which this call deliberately does not override.
                    if (!SetSuspendState(false, false, false)) _log.LogWarning("Sleep request failed: {Error}", Marshal.GetLastWin32Error());
                    break;
                case PowerMechanism.Hibernate:
                    if (!SetSuspendState(true, false, false)) _log.LogWarning("Hibernate request failed: {Error}", Marshal.GetLastWin32Error());
                    break;
                case PowerMechanism.ExitWindows:
                    if (!EnableShutdownPrivilege() || !ExitWindowsEx((EXIT_WINDOWS_FLAGS)step.ExitFlags, (SHUTDOWN_REASON)SystemPowerPlan.ReasonPlanned))
                    {
                        _log.LogWarning("ExitWindowsEx failed ({Error}); falling back to shutdown.exe {Args}", Marshal.GetLastWin32Error(), step.ShutdownArguments);
                        RunShutdownExe(step.ShutdownArguments);
                    }
                    break;
                case PowerMechanism.ShutdownExe:
                    RunShutdownExe(step.ShutdownArguments);
                    break;
            }
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Power action {Action} failed", action);
        }
    }

    /// <summary>GetPwrCapabilities: HiberFilePresent is TRUE when hibernation is enabled (hiberfil.sys exists).</summary>
    private bool HibernationEnabled()
    {
        try
        {
            return GetPwrCapabilities(out SYSTEM_POWER_CAPABILITIES caps) && caps.HiberFilePresent;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "GetPwrCapabilities failed");
            return false;
        }
    }

    /// <summary>
    /// Enables SeShutdownPrivilege on the current process token (documented requirement of ExitWindowsEx for shutdown/reboot). Returns false when the
    /// privilege is not held by the account at all (ERROR_NOT_ALL_ASSIGNED).
    /// </summary>
    private bool EnableShutdownPrivilege()
    {
        try
        {
            unsafe
            {
                HANDLE token = default;
                if (!OpenProcessToken(GetCurrentProcess(), TOKEN_ACCESS_MASK.TOKEN_ADJUST_PRIVILEGES | TOKEN_ACCESS_MASK.TOKEN_QUERY, &token)) return false;
                try
                {
                    if (!LookupPrivilegeValue(null, "SeShutdownPrivilege", out LUID luid)) return false;
                    var tp = new TOKEN_PRIVILEGES { PrivilegeCount = 1 };
                    tp.Privileges[0] = new LUID_AND_ATTRIBUTES { Luid = luid, Attributes = TOKEN_PRIVILEGES_ATTRIBUTES.SE_PRIVILEGE_ENABLED };
                    var adjusted = AdjustTokenPrivileges(token, false, &tp, 0, null, null);
                    // AdjustTokenPrivileges returns TRUE even when nothing was adjusted; the last error tells whether the privilege was actually assigned.
                    return adjusted && Marshal.GetLastWin32Error() == 0;
                }
                finally
                {
                    if (token != default) CloseHandle(token);
                }
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Enabling SeShutdownPrivilege failed");
            return false;
        }
    }

    /// <summary>
    /// Runs <c>shutdown.exe</c> from the system directory with a fixed argument list (no shell, no window). Used only as the fallback path, so a failed
    /// <c>ExitWindowsEx</c> still ends in the same action.
    /// </summary>
    private void RunShutdownExe(string arguments)
    {
        try
        {
            var exe = Path.Combine(Environment.SystemDirectory, "shutdown.exe");
            using var process = Process.Start(new ProcessStartInfo(exe, arguments) { UseShellExecute = false, CreateNoWindow = true });
            _log.LogInformation("shutdown.exe {Args} started", arguments);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "shutdown.exe {Args} could not be started", arguments);
        }
    }
}
