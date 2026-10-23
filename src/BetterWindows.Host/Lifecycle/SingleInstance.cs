using System.Threading;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>Names of the per-session kernel objects (docs/IPC.md section 15). <c>Local\</c> = per Windows session, so fast-user-switching sessions do not collide.</summary>
public static class HostObjectNames
{
    /// <summary>Held for the lifetime of the shell; its existence means "an instance is running in this session".</summary>
    public const string SingleInstanceMutex = @"Local\BetterWindows.SingleInstance";
    /// <summary>Signalled by a second launch: the running instance opens the settings window.</summary>
    public const string OpenSettingsEvent = @"Local\BetterWindows.OpenSettings";
    /// <summary>Signalled by <c>--restore-taskbar</c>: the running instance performs its ordered shutdown (which restores the shell).</summary>
    public const string QuitEvent = @"Local\BetterWindows.Quit";
}

/// <summary>
/// Single-instance guard: a named mutex that is only ever CREATED (never owned, so there is no thread affinity and an abandoned owner cannot wedge the next start),
/// plus two named auto-reset events: "open settings" (a second launch asks the first to show its settings window) and "quit" (<c>--restore-taskbar</c> asks the first to exit).
/// Listening uses thread-pool registered waits; callbacks are marshalled by the caller.
/// </summary>
public sealed class SingleInstance : IDisposable
{
    private readonly Mutex _mutex;
    private EventWaitHandle? _openSettings, _quit;
    private RegisteredWaitHandle? _openReg, _quitReg;
    private int _disposed;

    private SingleInstance(Mutex mutex) { _mutex = mutex; }

    /// <summary>Creates the mutex. Returns null if another instance already holds it.</summary>
    public static SingleInstance? TryAcquire(string mutexName = HostObjectNames.SingleInstanceMutex)
    {
        var m = new Mutex(false, mutexName, out var createdNew);
        if (createdNew) return new SingleInstance(m);
        m.Dispose();
        return null;
    }

    /// <summary>True if some instance in this session holds the mutex (used by <c>--restore-taskbar</c> to wait for the owner to leave).</summary>
    public static bool IsRunning(string mutexName = HostObjectNames.SingleInstanceMutex)
    {
        try
        {
            if (!Mutex.TryOpenExisting(mutexName, out var m)) return false;
            m.Dispose();
            return true;
        }
        catch (Exception) { return false; }
    }

    /// <summary>Second launch: ask the first instance to open its settings window. Returns false if the event could not be signalled.</summary>
    public static bool SignalOpenSettings(string eventName = HostObjectNames.OpenSettingsEvent) => Signal(eventName);

    /// <summary><c>--restore-taskbar</c>: ask the first instance to shut down. Returns false if no instance is listening.</summary>
    public static bool SignalQuit(string eventName = HostObjectNames.QuitEvent)
    {
        try
        {
            if (!EventWaitHandle.TryOpenExisting(eventName, out var e)) return false;
            using (e) return e.Set();
        }
        catch (Exception) { return false; }
    }

    private static bool Signal(string eventName)
    {
        try
        {
            // Create-or-open: if the first instance has not created its listener yet the signal is remembered and consumed once it starts listening.
            using var e = new EventWaitHandle(false, EventResetMode.AutoReset, eventName);
            return e.Set();
        }
        catch (Exception) { return false; }
    }

    /// <summary>Starts listening for the two events. Callbacks run on thread-pool threads and must not block (post to the dispatcher).</summary>
    public void StartListening(Action openSettings, Action quit, ILogger? log = null,
        string openSettingsName = HostObjectNames.OpenSettingsEvent, string quitName = HostObjectNames.QuitEvent)
    {
        if (Volatile.Read(ref _disposed) != 0) return;
        try
        {
            _openSettings = new EventWaitHandle(false, EventResetMode.AutoReset, openSettingsName);
            _quit = new EventWaitHandle(false, EventResetMode.AutoReset, quitName);
            _openReg = ThreadPool.RegisterWaitForSingleObject(_openSettings, (_, timedOut) => Safe(openSettings, timedOut, log), null, Timeout.Infinite, false);
            _quitReg = ThreadPool.RegisterWaitForSingleObject(_quit, (_, timedOut) => Safe(quit, timedOut, log), null, Timeout.Infinite, false);
        }
        catch (Exception ex) { log?.LogWarning(ex, "Could not listen for second-instance / quit signals"); }
    }

    private static void Safe(Action action, bool timedOut, ILogger? log)
    {
        if (timedOut) return;
        try { action(); } catch (Exception ex) { log?.LogError(ex, "Single-instance signal handler failed"); }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        try { _openReg?.Unregister(null); } catch (Exception) { }
        try { _quitReg?.Unregister(null); } catch (Exception) { }
        _openSettings?.Dispose();
        _quit?.Dispose();
        _mutex.Dispose();
    }
}
