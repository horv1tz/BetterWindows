using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;
using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.UI.WindowsAndMessaging;

namespace BetterWindows.Native;

public sealed unsafe partial class Win32WindowSystem
{
    /// <inheritdoc />
    public bool Activate(long handle)
    {
        var hwnd = ValidateTopLevel(handle);
        return !hwnd.IsNull && ForegroundActivator.Activate(hwnd, _log);
    }

    /// <inheritdoc />
    public bool Minimize(long handle) => ShowAsync(handle, SHOW_WINDOW_CMD.SW_MINIMIZE);

    /// <inheritdoc />
    public bool Restore(long handle) => ShowAsync(handle, SHOW_WINDOW_CMD.SW_RESTORE);

    /// <inheritdoc />
    public bool Maximize(long handle) => ShowAsync(handle, SHOW_WINDOW_CMD.SW_MAXIMIZE);

    /// <inheritdoc />
    public bool Close(long handle)
    {
        var hwnd = ValidateTopLevel(handle);
        if (hwnd.IsNull)
        {
            return false;
        }

        // PostMessage, not SendMessage: a graceful close request must not block on an app that shows a "save changes?" dialog.
        if (!PInvoke.PostMessage(hwnd, Win32Consts.WM_CLOSE, 0, 0))
        {
            // ERROR_ACCESS_DENIED here is UIPI: the target is elevated and we are not.
            _log.LogDebug("PostMessage(WM_CLOSE) to {Handle} failed (error {Error})", handle, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
            return false;
        }

        return true;
    }

    /// <summary>
    /// ShowWindowAsync posts the command to the window's own thread instead of sending it, so a hung window cannot block the caller.
    /// The return value only says that the request was queued.
    /// </summary>
    private bool ShowAsync(long handle, SHOW_WINDOW_CMD command)
    {
        var hwnd = ValidateTopLevel(handle);
        if (hwnd.IsNull)
        {
            return false;
        }

        if (!PInvoke.ShowWindowAsync(hwnd, command))
        {
            _log.LogDebug("ShowWindowAsync({Handle}, {Cmd}) failed (error {Error})", handle, command, System.Runtime.InteropServices.Marshal.GetLastPInvokeError());
            return false;
        }

        return true;
    }

    /// <summary>The HWND if the value names an existing top-level window, otherwise a null HWND. Child windows and message-only windows are refused.</summary>
    private static HWND ValidateTopLevel(long handle)
    {
        if (handle == 0)
        {
            return default;
        }

        HWND hwnd = handle.ToHwnd();
        if (!PInvoke.IsWindow(hwnd) || !WindowNative.IsTopLevel(hwnd, PInvoke.GetDesktopWindow()))
        {
            return default;
        }

        return hwnd;
    }
}
