using System.ComponentModel;
using System.Diagnostics;

namespace BetterWindows.Host.Lifecycle;

/// <summary>Answers "is the process that owns this journal / marker still the same live process?". Faked in tests.</summary>
public interface IProcessProbe
{
    /// <summary>
    /// True if a process with <paramref name="pid"/> exists and, when <paramref name="expectedStartUtc"/> is known, started at that time (+-5 s). A different start time
    /// means the pid was recycled by another program, i.e. the original owner is dead.
    /// </summary>
    bool IsAlive(int pid, DateTimeOffset? expectedStartUtc);
}

/// <summary>Real probe on <see cref="Process"/>. Access-denied means "exists" (a process of another user/integrity level is still a live process).</summary>
public sealed class SystemProcessProbe : IProcessProbe
{
    /// <summary>Allowed difference between the recorded and the actual start time (clock rounding, FILETIME vs. DateTime conversions).</summary>
    public static readonly TimeSpan StartTimeTolerance = TimeSpan.FromSeconds(5);

    public bool IsAlive(int pid, DateTimeOffset? expectedStartUtc)
    {
        if (pid <= 0) return false;
        try
        {
            using var p = Process.GetProcessById(pid);
            if (p.HasExited) return false;
            if (expectedStartUtc is { } expected)
            {
                var actual = new DateTimeOffset(p.StartTime.ToUniversalTime());
                if ((actual - expected).Duration() > StartTimeTolerance) return false;
            }
            return true;
        }
        catch (ArgumentException) { return false; }          // no such process
        catch (InvalidOperationException) { return false; }  // exited between lookup and query
        catch (Win32Exception) { return true; }               // access denied: it exists, we just may not inspect it
    }
}
