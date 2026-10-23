using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Host.Interop;

/// <summary>
/// Fallback z-order of the desktop layer when IDesktopIconsController.TryAttachDesktopLayer reports an unrecognised shell layout. The layer must sit ABOVE the wallpaper
/// (Progman, which paints it) but BELOW every normal window. A plain HWND_BOTTOM would put the window BELOW Progman (an ordinary top-level window), i.e. hide it behind the
/// wallpaper, so the window is inserted directly above Progman instead: SetWindowPos(hWndInsertAfter = the window that is just above Progman in the z-order). When Progman
/// cannot be found, HWND_BOTTOM is used. NOACTIVATE everywhere: this never activates anything. Re-asserted by the caller after foreground changes.
/// This file deliberately contains no WorkerW/SHELLDLL_DefView logic: that structure is owned by IDesktopIconsController (docs/DECISIONS.md #12).
/// </summary>
internal static class DesktopZOrder
{
    /// <summary>Places <paramref name="hwnd"/> just above the desktop (Progman). Returns false if SetWindowPos failed.</summary>
    public static unsafe bool PinAboveWallpaper(nint hwnd)
    {
        if (hwnd == 0) return false;
        var progman = HostPInvoke.FindWindow("Progman", null);
        nint insertAfter = WindowStyling.HwndBottom;
        if (progman.Value != null)
        {
            var above = HostPInvoke.GetWindow(progman, GET_WINDOW_CMD.GW_HWNDPREV);
            if (above.Value == null)
            {
                // Progman is already at the very top of the z-order: nothing normal exists above it, so staying at the top keeps us right above it as well.
                insertAfter = WindowStyling.HwndTop;
            }
            else if ((nint)above.Value == hwnd)
            {
                return true; // we already sit directly above Progman
            }
            else
            {
                insertAfter = (nint)above.Value;
            }
        }
        return WindowStyling.SetZOrder(hwnd, insertAfter);
    }
}
