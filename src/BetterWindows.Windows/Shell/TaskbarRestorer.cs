using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native;

/// <summary>
/// Stateless, dependency-free, idempotent: puts the native taskbar(s) back exactly as they were before BetterWindows touched them, using
/// the persisted snapshot (<c>%LOCALAPPDATA%\BetterWindows\state\taskbar.json</c>). Called from crash paths, from the next start after an
/// unclean exit, and by <c>BetterWindows.exe --restore-taskbar</c>. Works without any other service being constructed and never throws.
/// Without a snapshot it only repairs a taskbar that is currently hidden (visible again, auto-hide off) and never overrides the user's
/// own auto-hide preference. If Explorer is not running the snapshot is kept for the next attempt.
/// </summary>
public static class TaskbarRestorer
{
    /// <summary>Serializes the controller and the restorer inside this process (a different process is harmless: every step is idempotent).</summary>
    internal static readonly object Gate = new();

    private static long s_stamp;

    /// <summary>
    /// Incremented by every <see cref="RestoreNow()"/>. A live <see cref="Win32TaskbarController"/> compares it with the value it saw at its last
    /// Apply: a change means somebody restored the taskbar behind its back (crash handler, rescue command), so it must stop re-hiding it.
    /// </summary>
    internal static long Stamp => Interlocked.Read(ref s_stamp);

    /// <summary>Restores the taskbar from the persisted snapshot. Safe to call repeatedly, from any thread, at any time.</summary>
    public static void RestoreNow() => RestoreNow(new Win32TaskbarSurface(), FileTaskbarSnapshotStore.Default, NullLogger.Instance);

    /// <summary>Restore against explicit surface/store (tests).</summary>
    internal static void RestoreNow(ITaskbarSurface surface, ITaskbarSnapshotStore store, ILogger log)
    {
        // A crash handler must not hang behind a stuck controller: wait a bounded time, then go ahead without the lock (all steps are idempotent).
        bool locked = false;
        try
        {
            Interlocked.Increment(ref s_stamp); // before restoring: a watcher must not undo what we are about to do
            locked = Monitor.TryEnter(Gate, TimeSpan.FromSeconds(3));
            TaskbarEngine.Restore(surface, store, log);
        }
        catch (Exception)
        {
            // Never throws, by contract.
        }
        finally
        {
            if (locked)
            {
                Monitor.Exit(Gate);
            }
        }
    }
}
