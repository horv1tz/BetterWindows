using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;

namespace BetterWindows.Native.Interop;

/// <summary>
/// Checked wrappers over the Win32/DWM calls used by the desktops, capture and tray code. Every call reports failure through its return value
/// (never an exception) because hostile or destroyed handles are routine. Handles are passed as <c>long</c> because that is the Core port type.
/// </summary>
internal static class DeskNative
{
    /// <summary>IsWindow: true only for a handle that currently names a window (destroyed windows return false, handle reuse is re-validated by callers).</summary>
    internal static bool IsLiveWindow(nint hwnd) => hwnd != 0 && PInvoke.IsWindow(new HWND(hwnd));

    /// <summary>IsIconic: minimised windows have no meaningful client content for PrintWindow, so capture refuses them and never restores them.</summary>
    internal static bool IsMinimised(nint hwnd) => PInvoke.IsIconic(new HWND(hwnd));

    /// <summary>
    /// DWMWA_CLOAKED mask. DWM sets it for windows on other virtual desktops (SHELL), suspended UWP apps (APP) and inherited cloaks.
    /// Returns null when the attribute cannot be read; callers must treat that as "do not capture".
    /// </summary>
    internal static uint? CloakedFlags(nint hwnd)
    {
        unsafe
        {
            uint value = 0;
            // DwmGetWindowAttribute documents DWMWA_CLOAKED as a DWORD; the buffer size must match exactly.
            HRESULT hr = PInvoke.DwmGetWindowAttribute(new HWND(hwnd), DWMWINDOWATTRIBUTE.DWMWA_CLOAKED, &value, sizeof(uint));
            return hr.Succeeded ? value : null;
        }
    }

    /// <summary>
    /// DWMWA_EXTENDED_FRAME_BOUNDS: the visible frame in physical pixels, without the invisible resize borders that GetWindowRect includes on Windows 10/11.
    /// </summary>
    internal static bool TryGetFrameBounds(nint hwnd, out DeskRect bounds)
    {
        unsafe
        {
            RECT rc = default;
            HRESULT hr = PInvoke.DwmGetWindowAttribute(new HWND(hwnd), DWMWINDOWATTRIBUTE.DWMWA_EXTENDED_FRAME_BOUNDS, &rc, (uint)sizeof(RECT));
            if (hr.Succeeded)
            {
                bounds = new DeskRect(rc.left, rc.top, rc.right, rc.bottom);
                return true;
            }
        }
        bounds = default;
        return false;
    }

    /// <summary>GetWindowRect: the full window rectangle (including invisible borders). PrintWindow renders this rectangle, so capture crops from it.</summary>
    internal static bool TryGetWindowRect(nint hwnd, out DeskRect rect)
    {
        if (PInvoke.GetWindowRect(new HWND(hwnd), out RECT rc))
        {
            rect = new DeskRect(rc.left, rc.top, rc.right, rc.bottom);
            return true;
        }
        rect = default;
        return false;
    }

    internal static unsafe nint GetForeground() => (nint)PInvoke.GetForegroundWindow().Value;

    /// <summary>FindWindow for a top-level window class (window title is ignored). Returns 0 when absent.</summary>
    internal static unsafe nint FindTopLevel(string className)
    {
        fixed (char* cls = className)
        {
            return (nint)PInvoke.FindWindow(new PCWSTR(cls), default).Value;
        }
    }

    /// <summary>FindWindowEx for a child window class under <paramref name="parent"/>. Returns 0 when absent.</summary>
    internal static unsafe nint FindChild(nint parent, string className)
    {
        fixed (char* cls = className)
        {
            return (nint)PInvoke.FindWindowEx(new HWND(parent), default, new PCWSTR(cls), default).Value;
        }
    }

    internal static bool IsVisible(nint hwnd) => PInvoke.IsWindowVisible(new HWND(hwnd));
}

/// <summary>Plain rectangle in physical pixels (avoids leaking the generated Win32 struct type into the pure logic).</summary>
internal readonly record struct DeskRect(int Left, int Top, int Right, int Bottom)
{
    public int Width => Right - Left;
    public int Height => Bottom - Top;
}
