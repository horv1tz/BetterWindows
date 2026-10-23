using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native;

/// <summary>
/// Brings an arbitrary top-level window to the foreground despite the foreground lock (SetForegroundWindow only succeeds for a process
/// that received the last input event, is the foreground process, etc.). Escalating, each step verified against GetForegroundWindow:
/// restore if minimised; plain SetForegroundWindow (works right after a click on our own surface); AttachThreadInput to the current
/// foreground thread (shares input state, the Task Manager recipe); a masked Alt tap through SendInput (counts as "last input event");
/// finally the undocumented-but-stable SwitchToThisWindow. The result is the TRUTH (what GetForegroundWindow says afterwards).
/// May block up to roughly one second; call from a worker thread, not a UI thread.
/// </summary>
internal static unsafe class ForegroundActivator
{
    private static readonly object s_gate = new();

    /// <summary>Activates <paramref name="target"/>. True only when it (or a window it owns, e.g. a modal dialog) ends up as the foreground window.</summary>
    public static bool Activate(HWND target, ILogger log)
    {
        lock (s_gate)
        {
            try
            {
                return ActivateCore(target, log);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Activate({Hwnd}) failed unexpectedly", target.ToLong());
                return false;
            }
        }
    }

    private static bool ActivateCore(HWND target, ILogger log)
    {
        if (target.IsNull || !PInvoke.IsWindow(target))
        {
            return false;
        }

        int pid = WindowNative.GetProcessId(target, out uint targetThread);
        if (targetThread == 0)
        {
            return false;
        }

        // 1) A minimised window must be restored first (SW_RESTORE returns it to maximised/normal as it was). ShowWindowAsync posts the
        //    request so a hung target cannot block us; then wait briefly for the state change.
        if (PInvoke.IsIconic(target))
        {
            PInvoke.ShowWindowAsync(target, SHOW_WINDOW_CMD.SW_RESTORE);
            WaitUntil(() => !PInvoke.IsIconic(target), 400);
        }

        if (IsForeground(target))
        {
            return true;
        }

        // 2) The plain call. AllowSetForegroundWindow(pid) grants the TARGET process the right too (least privilege instead of ASFW_ANY),
        //    which matters for apps that finish their own activation (UWP frames) after we return.
        PInvoke.AllowSetForegroundWindow((uint)pid);
        SetForeground(target);
        if (WaitUntil(() => IsForeground(target), 40))
        {
            return true;
        }

        // 3) AttachThreadInput: while our thread shares the input queue of the foreground thread, the system treats us as that thread.
        HWND fg = PInvoke.GetForegroundWindow();
        uint fgThread = fg.IsNull ? 0 : PInvoke.GetWindowThreadProcessId(fg, null);
        uint self = PInvoke.GetCurrentThreadId();
        // Never attach to a hung thread: AttachThreadInput would stall us with it.
        bool canAttach = fgThread != 0 && fgThread != self && !PInvoke.IsHungAppWindow(fg);
        bool attachedFg = canAttach && PInvoke.AttachThreadInput(self, fgThread, true);
        bool attachedTarget = targetThread != self && targetThread != fgThread && !PInvoke.IsHungAppWindow(target)
            && PInvoke.AttachThreadInput(self, targetThread, true);
        try
        {
            SetForeground(target);
        }
        finally
        {
            if (attachedTarget)
            {
                PInvoke.AttachThreadInput(self, targetThread, false);
            }

            if (attachedFg)
            {
                PInvoke.AttachThreadInput(self, fgThread, false);
            }
        }

        if (WaitUntil(() => IsForeground(target), 40))
        {
            return true;
        }

        // 4) Last resort: a synthetic key tap makes us the process that "received the last input event". Alt is the classic choice; a
        //    dummy unassigned key pressed in between stops the foreground app from entering its menu-bar mode on the Alt release.
        if (!ProcessSecurity.IsLikelyBlockedByUipi(WindowNative.GetProcessId(fg, out _)))
        {
            KeyboardInput.Send(new[]
            {
                KeyboardInput.Down(Win32Consts.VK_MENU),
                KeyboardInput.Down(Win32Consts.VK_UNASSIGNED_MASK),
                KeyboardInput.Up(Win32Consts.VK_UNASSIGNED_MASK),
                KeyboardInput.Up(Win32Consts.VK_MENU),
            });
            SetForeground(target);
            if (WaitUntil(() => IsForeground(target), 40))
            {
                return true;
            }
        }

        // 5) SwitchToThisWindow(fUnknown: true) is what Alt+Tab uses internally; undocumented but stable for two decades.
        PInvoke.SwitchToThisWindow(target, true);

        // Windows on another virtual desktop need a moment for the desktop switch that the activation triggers.
        uint cloaked = WindowNative.GetCloakedFlags(target);
        bool ok = WaitUntil(() => IsForeground(target), cloaked != 0 ? 500 : 150);
        if (!ok)
        {
            log.LogDebug("Could not bring {Hwnd} to the foreground (foreground is {Fg})", target.ToLong(), PInvoke.GetForegroundWindow().ToLong());
        }

        return ok;
    }

    private static void SetForeground(HWND target)
    {
        PInvoke.SetForegroundWindow(target);
        PInvoke.BringWindowToTop(target);
    }

    /// <summary>True when <paramref name="target"/> or a window in its owner chain (modal dialog, owned popup) is the foreground window.</summary>
    internal static bool IsForeground(HWND target)
    {
        HWND fg = PInvoke.GetForegroundWindow();
        if (fg.IsNull)
        {
            return false;
        }

        if (fg == target)
        {
            return true;
        }

        HWND fgRoot = PInvoke.GetAncestor(fg, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
        HWND targetRoot = PInvoke.GetAncestor(target, GET_ANCESTOR_FLAGS.GA_ROOTOWNER);
        return !targetRoot.IsNull && fgRoot == targetRoot;
    }

    private static bool WaitUntil(Func<bool> condition, int timeoutMs)
    {
        long deadline = Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (condition())
            {
                return true;
            }

            if (Environment.TickCount64 >= deadline)
            {
                return false;
            }

            Thread.Sleep(10);
        }
    }
}
