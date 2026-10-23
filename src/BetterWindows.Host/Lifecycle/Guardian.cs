using System.Diagnostics;
using System.IO;
using BetterWindows.Core.Services;
using BetterWindows.Native;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// Starts <c>BetterWindows.exe --guardian &lt;ownerPid&gt;</c> once, BEFORE the first change to the user's shell (hiding the taskbar / desktop icons). The guardian is created as
/// a child of Explorer where possible, so killing this process or its whole tree (<c>taskkill /f /t</c>) does not take it along (docs/IPC.md section 15).
/// </summary>
public sealed class GuardianLauncher
{
    private readonly ILogger<GuardianLauncher> _log;
    private int _started;

    public GuardianLauncher(ILogger<GuardianLauncher> log) { _log = log; }

    /// <summary>True once a guardian process was started by this instance.</summary>
    public bool IsRunning => Volatile.Read(ref _started) == 1;

    /// <summary>Idempotent. Returns whether a guardian is running (false: it could not be started; the next start's journal check is the remaining safety net).</summary>
    public bool EnsureStarted()
    {
        if (Interlocked.CompareExchange(ref _started, 1, 0) != 0) return true;
        try
        {
            var exe = Environment.ProcessPath;
            if (string.IsNullOrEmpty(exe)) { Volatile.Write(ref _started, 0); return false; }
            var command = BuildCommandLine(exe, Environment.ProcessId, Environment.GetEnvironmentVariable(AppPaths.EnvVar));
            var explorer = NativeHelpers.FindExplorerPid();
            var pid = NativeHelpers.StartDetached(exe, command, explorer);
            if (pid <= 0)
            {
                Volatile.Write(ref _started, 0);
                _log.LogWarning("Could not start the guardian process");
                return false;
            }
            _log.LogInformation("Guardian process {Pid} started (parent: {Parent})", pid, explorer > 0 ? "Explorer " + explorer : "this process");
            return true;
        }
        catch (Exception ex)
        {
            Volatile.Write(ref _started, 0);
            _log.LogWarning(ex, "Could not start the guardian process");
            return false;
        }
    }

    /// <summary><c>"C:\...\BetterWindows.exe" --guardian 1234 [--data-dir "D:\x"]</c> (the data dir travels so the guardian reads the same journals even without the env var).</summary>
    public static string BuildCommandLine(string exePath, int ownerPid, string? dataDir)
    {
        var cmd = $"\"{exePath}\" --guardian {ownerPid}";
        if (!string.IsNullOrEmpty(dataDir)) cmd += $" --data-dir \"{dataDir.TrimEnd('\\')}\"";
        return cmd;
    }
}

/// <summary>
/// The guardian process body: wait for the owner to die for ANY reason (crash, <c>taskkill /f</c>, power loss of the session) and then run the static restorers. The restorers
/// are owner-checked (they act only when the journal's owner is dead), so a guardian can never undo the state of a newer instance. No WPF, no WebView2, no DI.
/// </summary>
public static class GuardianRunner
{
    public static int Run(int ownerPid, string logsDir)
    {
        Process? owner = null;
        try
        {
            try { owner = Process.GetProcessById(ownerPid); }
            catch (ArgumentException) { /* the owner is already gone: restore right away */ }
            Note(logsDir, $"guardian {Environment.ProcessId} watching owner {ownerPid}");
            owner?.WaitForExit();
        }
        catch (Exception ex)
        {
            Note(logsDir, $"guardian wait failed: {ex.Message}");
        }
        finally { owner?.Dispose(); }

        // Give a clean shutdown that is still flushing its journal a moment, then restore whatever is left (idempotent; no-op when the owner restored itself).
        Thread.Sleep(500);
        try { TaskbarRestorer.RestoreNow(); } catch (Exception ex) { Note(logsDir, $"taskbar restore failed: {ex.Message}"); }
        try { DesktopIconsRestorer.RestoreNow(); } catch (Exception ex) { Note(logsDir, $"desktop icons restore failed: {ex.Message}"); }
        Note(logsDir, $"guardian done for owner {ownerPid}");
        return 0;
    }

    private static void Note(string logsDir, string message)
    {
        try
        {
            Directory.CreateDirectory(logsDir);
            File.AppendAllText(Path.Combine(logsDir, "guardian.log"), $"{DateTimeOffset.Now:yyyy-MM-dd HH:mm:ss.fff zzz} {message}{Environment.NewLine}");
        }
        catch (Exception) { /* best effort */ }
    }
}
