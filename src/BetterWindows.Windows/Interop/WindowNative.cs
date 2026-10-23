using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using BetterWindows.Core.Platform;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Thin, defensive wrappers around the per-window Win32 queries shared by the window system, the taskbar controller and the
/// full-screen detection. Every wrapper returns a safe default instead of throwing; none of them blocks on another process
/// (the title query uses SendMessageTimeout with SMTO_ABORTIFHUNG).
/// </summary>
internal static unsafe class WindowNative
{
    /// <summary>Window class name ("" when the window is gone).</summary>
    public static string GetClassName(HWND hwnd)
    {
        char* buf = stackalloc char[256];
        int n = PInvoke.GetClassName(hwnd, new PWSTR(buf), 256);
        return n > 0 ? new string(buf, 0, n) : "";
    }

    /// <summary>Owning process id; 0 when the window is gone. <paramref name="threadId"/> is the creating thread (0 when gone).</summary>
    public static int GetProcessId(HWND hwnd, out uint threadId)
    {
        uint pid = 0;
        threadId = PInvoke.GetWindowThreadProcessId(hwnd, &pid);
        return threadId == 0 ? 0 : (int)pid;
    }

    /// <summary>Raw GWL_STYLE bits.</summary>
    public static uint GetStyle(HWND hwnd) => unchecked((uint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_STYLE));

    /// <summary>Raw GWL_EXSTYLE bits.</summary>
    public static uint GetExStyle(HWND hwnd) => unchecked((uint)PInvoke.GetWindowLongPtr(hwnd, WINDOW_LONG_PTR_INDEX.GWL_EXSTYLE));

    /// <summary>True when the window's parent is the desktop (a real top-level window; message-only and child windows are not).</summary>
    public static bool IsTopLevel(HWND hwnd, HWND desktop) => PInvoke.GetAncestor(hwnd, GET_ANCESTOR_FLAGS.GA_PARENT) == desktop;

    /// <summary>
    /// Raw DWMWA_CLOAKED mask (DWM_CLOAKED_APP=1, SHELL=2, INHERITED=4), 0 when not cloaked or when DWM is unavailable.
    /// </summary>
    public static uint GetCloakedFlags(HWND hwnd)
    {
        uint cloaked = 0;
        var hr = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &cloaked, sizeof(uint));
        return hr.Succeeded ? cloaked : 0;
    }

    /// <summary>
    /// DWMWA_EXTENDED_FRAME_BOUNDS (the visible frame without the invisible resize border) in physical pixels; falls back to GetWindowRect.
    /// False if neither works (window gone).
    /// </summary>
    public static bool TryGetFrameBounds(HWND hwnd, out NativeRect bounds)
    {
        RECT r;
        var hr = PInvoke.DwmGetWindowAttribute(hwnd, DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &r, (uint)sizeof(RECT));
        if (hr.Succeeded)
        {
            bounds = new NativeRect(r.left, r.top, r.right, r.bottom);
            return true;
        }

        if (PInvoke.GetWindowRect(hwnd, &r))
        {
            bounds = new NativeRect(r.left, r.top, r.right, r.bottom);
            return true;
        }

        bounds = default;
        return false;
    }

    /// <summary>Restored ("normal") rectangle of a window from GetWindowPlacement; used for minimised windows whose live frame is parked off-screen.</summary>
    public static bool TryGetNormalRect(HWND hwnd, out NativeRect rect)
    {
        var wp = new WINDOWPLACEMENT { length = (uint)sizeof(WINDOWPLACEMENT) };
        if (PInvoke.GetWindowPlacement(hwnd, &wp))
        {
            rect = new NativeRect(wp.rcNormalPosition.left, wp.rcNormalPosition.top, wp.rcNormalPosition.right, wp.rcNormalPosition.bottom);
            return true;
        }

        rect = default;
        return false;
    }

    /// <summary>
    /// Window title through SendMessageTimeout(WM_GETTEXT, SMTO_ABORTIFHUNG): GetWindowText can block on an unresponsive window of the SAME
    /// process and gives no timeout control. Returns null when the window did not answer within <paramref name="timeoutMs"/>
    /// (hung, busy or gone) so the caller can fall back to a cached value.
    /// </summary>
    public static string? GetTitle(HWND hwnd, int timeoutMs)
    {
        const int capacity = 1024;
        char* buf = stackalloc char[capacity];
        buf[0] = '\0';
        nuint copied = 0;
        var r = PInvoke.SendMessageTimeout(
            hwnd,
            Win32Consts.WM_GETTEXT,
            capacity,
            (nint)buf,
            SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ABORTIFHUNG | SEND_MESSAGE_TIMEOUT_FLAGS.SMTO_ERRORONEXIT,
            (uint)timeoutMs,
            &copied);
        if (r.Value == 0)
        {
            return null;
        }

        int len = (int)Math.Min(copied, capacity - 1);
        buf[len] = '\0'; // never trust the receiver to terminate
        return new string(buf, 0, len);
    }

    /// <summary>Handles of all top-level windows (EnumWindows), in Z order from the top.</summary>
    public static List<nint> EnumerateTopLevel()
    {
        var list = new List<nint>(512);
        var gc = GCHandle.Alloc(list);
        try
        {
            PInvoke.EnumWindows(&EnumProc, (nint)GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        return list;
    }

    /// <summary>Handles of all descendants of <paramref name="parent"/> (EnumChildWindows).</summary>
    public static List<nint> EnumerateChildren(HWND parent)
    {
        var list = new List<nint>(16);
        var gc = GCHandle.Alloc(list);
        try
        {
            PInvoke.EnumChildWindows(parent, &EnumProc, (nint)GCHandle.ToIntPtr(gc));
        }
        finally
        {
            gc.Free();
        }

        return list;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvStdcall) })]
    private static BOOL EnumProc(HWND hwnd, LPARAM lParam)
    {
        try
        {
            if (GCHandle.FromIntPtr(lParam.Value).Target is List<nint> list)
            {
                list.Add(hwnd.ToNint());
            }

            return true;
        }
        catch
        {
            return false; // stop enumerating; exceptions must not cross the native boundary
        }
    }

    /// <summary>Finds every top-level window of the given class (FindWindowEx loop; stable against windows appearing meanwhile).</summary>
    public static List<nint> FindAllByClass(string className)
    {
        var result = new List<nint>(2);
        HWND prev = default;
        for (int guard = 0; guard < 64; guard++)
        {
            HWND next = PInvoke.FindWindowEx(default, prev, className, null!);
            if (next.IsNull)
            {
                break;
            }

            result.Add(next.ToNint());
            prev = next;
        }

        return result;
    }
}
