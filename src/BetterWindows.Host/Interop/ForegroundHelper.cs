using Windows.Win32;
using Windows.Win32.Foundation;

namespace BetterWindows.Host.Interop;

/// <summary>
/// Taking the foreground reliably. Windows only lets a process call SetForegroundWindow successfully if it received the last input event or the foreground process allowed
/// it (the "foreground lock"). A hotkey (RegisterHotKey) delivers input to us, so Alt+Space normally works directly; a hot corner or a command-line launch has no input, so
/// the documented-in-practice recipe applies: attach our input queue to the foreground thread's for the duration of the call.
/// </summary>
internal static class ForegroundHelper
{
    public static unsafe nint GetForeground() => (nint)PInvoke.GetForegroundWindow().Value;

    /// <summary>True when <paramref name="hwnd"/> is the foreground window after the attempt. Never throws.</summary>
    public static unsafe bool TryActivate(nint hwnd)
    {
        if (hwnd == 0 || !PInvoke.IsWindow(hwnd.ToHwnd())) return false;
        var target = hwnd.ToHwnd();
        if (GetForeground() == hwnd) return true;

        // 1. The cheap path: works whenever we are allowed to (we got the hotkey / the user just clicked us).
        if (PInvoke.SetForegroundWindow(target) && GetForeground() == hwnd) return true;

        // 2. Share the input queue of the current foreground thread, which is what makes SetForegroundWindow legal.
        var fg = PInvoke.GetForegroundWindow();
        var attached = false;
        uint fgThread = 0;
        var me = PInvoke.GetCurrentThreadId();
        try
        {
            if (fg.Value != null)
            {
                fgThread = PInvoke.GetWindowThreadProcessId(fg, null);
                if (fgThread != 0 && fgThread != me) attached = PInvoke.AttachThreadInput(me, fgThread, true);
            }
            PInvoke.BringWindowToTop(target);
            PInvoke.SetForegroundWindow(target);
        }
        finally
        {
            if (attached) PInvoke.AttachThreadInput(me, fgThread, false);
        }
        if (GetForeground() == hwnd) return true;

        // 3. Last resort: undocumented but stable for 20 years (it is what Alt+Tab itself uses).
        PInvoke.SwitchToThisWindow(target, true);
        return GetForeground() == hwnd;
    }
}
