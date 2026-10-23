using BetterWindows.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Host.Interop;

/// <summary>
/// Thin, checked wrappers over the window styling Win32 calls the surface windows need. Every call reports failure through its return value (callers log); nothing throws.
/// </summary>
internal static class WindowStyling
{
    public static readonly nint HwndTopmost = new(-1);
    public static readonly nint HwndNoTopmost = new(-2);
    public static readonly nint HwndBottom = new(1);
    public static readonly nint HwndTop = new(0);

    /// <summary>
    /// Adds/removes extended window styles (read-modify-write) and, if something changed, forces the non-client area to be recalculated with SWP_FRAMECHANGED (without it the
    /// new style may not take effect until the next resize). WHY not rely on WPF: WPF has no property for WS_EX_NOACTIVATE, and WS_EX_TOOLWINDOW set through ShowInTaskbar
    /// alone does not remove the window from Alt+Tab for every window style.
    /// </summary>
    public static bool ModifyExStyle(nint hwnd, nint add, nint remove)
    {
        if (hwnd == 0) return false;
        var h = hwnd.ToHwnd();
        var current = HostPInvoke.GetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);
        var next = (current | add) & ~remove;
        if (next == current) return true;
        // SetWindowLongPtr returns 0 both on failure and when the previous value was 0: clear the last error first to tell them apart.
        System.Runtime.InteropServices.Marshal.SetLastPInvokeError(0);
        var prev = HostPInvoke.SetWindowLongPtr(h, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE, next);
        if (prev == 0 && System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != 0) return false;
        return HostPInvoke.SetWindowPos(h, default, 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOZORDER | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_FRAMECHANGED);
    }

    public static nint GetExStyle(nint hwnd) => hwnd == 0 ? 0 : HostPInvoke.GetWindowLongPtr(hwnd.ToHwnd(), WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE);

    public static nint GetStyle(nint hwnd) => hwnd == 0 ? 0 : HostPInvoke.GetWindowLongPtr(hwnd.ToHwnd(), WINDOW_LONG_PTR_INDEX.GWL_STYLE);

    /// <summary>
    /// The behaviour set of an overlay-class surface window: tool window (no taskbar button, not in Alt+Tab) and optionally no-activate (a click must not steal focus from the
    /// foreground app, otherwise "the active app" in the menu bar would turn into BetterWindows).
    /// </summary>
    public static bool ApplyToolWindow(nint hwnd, bool noActivate) =>
        ModifyExStyle(hwnd, Win32Const.WS_EX_TOOLWINDOW | (noActivate ? Win32Const.WS_EX_NOACTIVATE : 0), Win32Const.WS_EX_APPWINDOW);

    /// <summary>Moves/sizes a window in physical px without activating it. <paramref name="insertAfter"/> is a z-order anchor (HWND_TOPMOST...) or null to keep the z-order.</summary>
    public static bool SetBounds(nint hwnd, NativeRect rect, nint? insertAfter, bool show = false, bool noRedraw = false)
    {
        if (hwnd == 0) return false;
        var flags = SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER;
        if (insertAfter is null) flags |= SET_WINDOW_POS_FLAGS.SWP_NOZORDER;
        if (show) flags |= SET_WINDOW_POS_FLAGS.SWP_SHOWWINDOW;
        if (noRedraw) flags |= SET_WINDOW_POS_FLAGS.SWP_NOREDRAW;
        return HostPInvoke.SetWindowPos(hwnd.ToHwnd(), new HWND(insertAfter ?? 0), rect.Left, rect.Top, Math.Max(1, rect.Width), Math.Max(1, rect.Height), flags);
    }

    /// <summary>Z-order only (no move/size/activation): used to re-assert topmost or bottom.</summary>
    public static bool SetZOrder(nint hwnd, nint insertAfter)
    {
        if (hwnd == 0) return false;
        return HostPInvoke.SetWindowPos(hwnd.ToHwnd(), new HWND(insertAfter), 0, 0, 0, 0,
            SET_WINDOW_POS_FLAGS.SWP_NOMOVE | SET_WINDOW_POS_FLAGS.SWP_NOSIZE | SET_WINDOW_POS_FLAGS.SWP_NOACTIVATE | SET_WINDOW_POS_FLAGS.SWP_NOOWNERZORDER);
    }

    public static bool TryGetWindowRect(nint hwnd, out NativeRect rect)
    {
        rect = default;
        if (hwnd == 0 || !HostPInvoke.GetWindowRect(hwnd.ToHwnd(), out var r)) return false;
        rect = r.ToNativeRect();
        return true;
    }

    public static bool TryGetClientSize(nint hwnd, out int width, out int height)
    {
        width = height = 0;
        if (hwnd == 0 || !HostPInvoke.GetClientRect(hwnd.ToHwnd(), out var r)) return false;
        width = r.right - r.left;
        height = r.bottom - r.top;
        return true;
    }

    public static bool IsAlive(nint hwnd) => hwnd != 0 && HostPInvoke.IsWindow(hwnd.ToHwnd());

    /// <summary>ShowWindow(SW_HIDE). ShowWindow's return value is the PREVIOUS visibility, not success, so only the handle can be validated.</summary>
    public static bool Hide(nint hwnd)
    {
        if (hwnd == 0) return false;
        HostPInvoke.ShowWindow(hwnd.ToHwnd(), SHOW_WINDOW_CMD.SW_HIDE);
        return true;
    }
}
