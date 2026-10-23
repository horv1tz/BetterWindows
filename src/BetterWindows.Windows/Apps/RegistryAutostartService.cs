using System.Security;
using BetterWindows.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Win32;

namespace BetterWindows.Native;

/// <summary>
/// Start with Windows via <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Run</c> (per user, no elevation, visible in Settings > Apps > Startup).
/// <see cref="IsEnabled"/> is true only if the value exists, points at THIS executable and the user has not switched it off in Task Manager/Settings
/// (<c>...\Explorer\StartupApproved\Run</c>). Writing never throws: access problems are logged.
/// </summary>
public sealed class RegistryAutostartService : IAutostartService
{
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ApprovedKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";

    private readonly ILogger _log;
    private readonly string? _exePath;
    private readonly string _runKeyPath;
    private readonly string _approvedKeyPath;

    /// <summary>Creates the service for the current process image.</summary>
    public RegistryAutostartService(ILogger<RegistryAutostartService>? logger = null)
        : this(logger, Environment.ProcessPath, RunKeyPath, ApprovedKeyPath)
    {
    }

    /// <summary>Test seam: custom executable and registry locations (a scratch key under HKCU).</summary>
    internal RegistryAutostartService(ILogger? logger, string? exePath, string runKeyPath, string approvedKeyPath)
    {
        _log = logger ?? NullLogger.Instance;
        _exePath = exePath;
        _runKeyPath = runKeyPath;
        _approvedKeyPath = approvedKeyPath;
    }

    /// <inheritdoc />
    public bool IsEnabled
    {
        get
        {
            try
            {
                using var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: false);
                if (run?.GetValue(AutostartCommand.ValueName) is not string command) return false;
                if (!AutostartCommand.SamePath(AutostartCommand.ExtractExePath(command), _exePath)) return false;

                using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: false);
                return AutostartCommand.IsApprovedByUser(approved?.GetValue(AutostartCommand.ValueName) as byte[]);
            }
            catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
            {
                _log.LogDebug(ex, "Cannot read the autostart registration");
                return false;
            }
        }
    }

    /// <inheritdoc />
    public void SetEnabled(bool enabled)
    {
        try
        {
            if (enabled) Enable();
            else Disable();
        }
        catch (Exception ex) when (ex is SecurityException or UnauthorizedAccessException or IOException)
        {
            _log.LogWarning(ex, "Cannot {Action} autostart (registry access denied?)", enabled ? "enable" : "disable");
        }
    }

    private void Enable()
    {
        if (string.IsNullOrEmpty(_exePath))
        {
            _log.LogWarning("Cannot enable autostart: the executable path is unknown");
            return;
        }
        // CreateSubKey opens the key if it exists and creates it (a missing Run key is legal on a fresh profile) otherwise.
        using (var run = Registry.CurrentUser.CreateSubKey(_runKeyPath, writable: true))
        {
            if (run == null)
            {
                _log.LogWarning("Cannot open the Run key for writing");
                return;
            }
            var command = AutostartCommand.Build(_exePath);
            if (run.GetValue(AutostartCommand.ValueName) as string != command)
                run.SetValue(AutostartCommand.ValueName, command, RegistryValueKind.String);
        }
        // The user asked for autostart in OUR settings now: an old "disabled" flag set in Task Manager must not silently veto it.
        ClearApprovedFlag();
    }

    private void Disable()
    {
        using (var run = Registry.CurrentUser.OpenSubKey(_runKeyPath, writable: true))
            run?.DeleteValue(AutostartCommand.ValueName, throwOnMissingValue: false);
        ClearApprovedFlag();
    }

    private void ClearApprovedFlag()
    {
        using var approved = Registry.CurrentUser.OpenSubKey(_approvedKeyPath, writable: true);
        approved?.DeleteValue(AutostartCommand.ValueName, throwOnMissingValue: false);
    }
}
