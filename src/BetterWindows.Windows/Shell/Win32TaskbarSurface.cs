using System.Runtime.InteropServices;
using BetterWindows.Native.Interop;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.Shell;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native;

/// <summary>
/// The real taskbar: Shell_TrayWnd + Shell_SecondaryTrayWnd windows, ABM_GETSTATE / ABM_SETSTATE and ShowWindowAsync.
/// Stateless and dependency-free, so TaskbarRestorer can use it from a fresh process.
/// </summary>
internal sealed unsafe class Win32TaskbarSurface : ITaskbarSurface
{
    internal const string PrimaryClass = "Shell_TrayWnd";
    internal const string SecondaryClass = "Shell_SecondaryTrayWnd";

    /// <inheritdoc />
    public IReadOnlyList<TaskbarWindowInfo> GetWindows()
    {
        var result = new List<TaskbarWindowInfo>(2);
        foreach (nint h in WindowNative.FindAllByClass(PrimaryClass))
        {
            result.Add(new TaskbarWindowInfo(h, true, PInvoke.IsWindowVisible(h.ToHwnd())));
        }

        foreach (nint h in WindowNative.FindAllByClass(SecondaryClass))
        {
            result.Add(new TaskbarWindowInfo(h, false, PInvoke.IsWindowVisible(h.ToHwnd())));
        }

        return result;
    }

    /// <inheritdoc />
    public bool TryGetState(out uint state)
    {
        state = 0;
        var primary = WindowNative.FindAllByClass(PrimaryClass);
        if (primary.Count == 0)
        {
            return false;
        }

        var abd = new APPBARDATA { cbSize = (uint)sizeof(APPBARDATA), hWnd = primary[0].ToHwnd() };
        // ABM_GETSTATE returns the state bits directly (not a pointer): ABS_AUTOHIDE | ABS_ALWAYSONTOP.
        state = (uint)PInvoke.SHAppBarMessage(Win32Consts.ABM_GETSTATE, ref abd) & (Win32Consts.ABS_AUTOHIDE | Win32Consts.ABS_ALWAYSONTOP);
        return true;
    }

    /// <inheritdoc />
    public bool TrySetState(uint state)
    {
        var primary = WindowNative.FindAllByClass(PrimaryClass);
        if (primary.Count == 0)
        {
            return false;
        }

        // ABM_SETSTATE: lParam carries the new ABS_* bits; hWnd names the taskbar. It applies to every taskbar (the setting is global).
        var abd = new APPBARDATA
        {
            cbSize = (uint)sizeof(APPBARDATA),
            hWnd = primary[0].ToHwnd(),
            lParam = (nint)state,
        };
        PInvoke.SHAppBarMessage(Win32Consts.ABM_SETSTATE, ref abd);

        // The call has no result: verify by reading back (the shell applies it synchronously, but be tolerant for a moment).
        for (int i = 0; i < 10; i++)
        {
            if (TryGetState(out uint now) && now == state)
            {
                return true;
            }

            Thread.Sleep(30);
        }

        return false;
    }

    /// <inheritdoc />
    public bool SetVisible(nint handle, bool visible)
    {
        var hwnd = handle.ToHwnd();
        if (!PInvoke.IsWindow(hwnd))
        {
            return false;
        }

        // ShowWindowAsync posts the request to Explorer's thread: a hung Explorer cannot block us. SW_SHOWNA shows without activating.
        var cmd = visible ? SHOW_WINDOW_CMD.SW_SHOWNA : SHOW_WINDOW_CMD.SW_HIDE;
        PInvoke.ShowWindowAsync(hwnd, cmd);
        for (int i = 0; i < 30; i++)
        {
            if (PInvoke.IsWindowVisible(hwnd) == visible)
            {
                return true;
            }

            if (i == 12)
            {
                PInvoke.ShowWindowAsync(hwnd, cmd); // the first post can be swallowed while Explorer is busy re-creating its UI
            }

            Thread.Sleep(20);
        }

        return PInvoke.IsWindowVisible(hwnd) == visible;
    }
}
