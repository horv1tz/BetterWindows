using Windows.Win32;
using Windows.Win32.UI.HiDpi;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Switches the CURRENT THREAD to the PerMonitorV2 DPI awareness context for the lifetime of the scope and restores the previous
/// context on dispose. The Host process is PerMonitorV2 through its manifest, so this is a no-op there; it makes the Windows layer
/// return PHYSICAL pixels even when hosted by a DPI-unaware process (test runners, tools) where Win32 would otherwise virtualise
/// coordinates. Thread-affine: create and dispose on the same thread.
/// </summary>
internal readonly struct DpiAwarenessScope : IDisposable
{
    private readonly DPI_AWARENESS_CONTEXT _previous;
    private readonly bool _changed;

    private DpiAwarenessScope(DPI_AWARENESS_CONTEXT previous, bool changed)
    {
        _previous = previous;
        _changed = changed;
    }

    /// <summary>Enter PerMonitorV2 (DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = -4) on the current thread.</summary>
    public static DpiAwarenessScope PerMonitorV2()
    {
        try
        {
            // SetThreadDpiAwarenessContext returns the previous context, or NULL when the argument was invalid (old OS): then nothing changed.
            var previous = PInvoke.SetThreadDpiAwarenessContext(new DPI_AWARENESS_CONTEXT((IntPtr)(-4)));
            return !previous.IsNull ? new DpiAwarenessScope(previous, true) : default;
        }
        catch (EntryPointNotFoundException)
        {
            return default;
        }
    }

    /// <summary>Restores the thread's previous DPI awareness context.</summary>
    public void Dispose()
    {
        if (_changed)
        {
            PInvoke.SetThreadDpiAwarenessContext(_previous);
        }
    }
}
