namespace BetterWindows.Native.Interop;

/// <summary>
/// Raise port events with a per-subscriber try/catch: an exception of a Core subscriber must never unwind into a SetWinEventHook callback, a WndProc, or a WinRT event thread
/// (that would crash the process or silently kill the hook). FROZEN helper: every Windows-layer class raises its events through it.
/// </summary>
public static class SafeEvent
{
    public static void Raise(Action? handler, Action<Exception>? onError = null)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action)d)(); } catch (Exception ex) { onError?.Invoke(ex); }
        }
    }

    public static void Raise<T>(Action<T>? handler, T arg, Action<Exception>? onError = null)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<T>)d)(arg); } catch (Exception ex) { onError?.Invoke(ex); }
        }
    }
}
