using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Windows.Win32;
using Windows.Win32.System.StationsAndDesktops;

namespace BetterWindows.Native;

/// <summary>
/// Win32 cursor/mouse state: GetCursorPos in physical pixels and mouse-button state via GetAsyncKeyState. Polled by hot corners at ~20 Hz,
/// so the fast path is a single GetCursorPos; the "is this still our input desktop?" check is rate limited to four times a second.
/// </summary>
public sealed unsafe class Win32CursorProvider : ICursorProvider
{
    private const int DesktopCheckIntervalMs = 250;

    private long _nextDesktopCheckMs;
    private volatile bool _desktopOk = true;

    /// <inheritdoc />
    public bool TryGetCursorPosition(out NativePoint position)
    {
        position = default;
        try
        {
            if (!InputDesktopIsOurs())
            {
                return false;
            }

            // PerMonitorV2 so the position is physical even if the host process is DPI-unaware.
            using var dpi = DpiAwarenessScope.PerMonitorV2();
            System.Drawing.Point p;
            // GetCursorPos fails (ERROR_ACCESS_DENIED) while the secure desktop (UAC prompt, lock screen) is the input desktop.
            if (!PInvoke.GetCursorPos(&p))
            {
                return false;
            }

            position = new NativePoint(p.X, p.Y);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <inheritdoc />
    public bool IsAnyMouseButtonDown()
    {
        try
        {
            return KeyboardInput.IsDown(Win32Consts.VK_LBUTTON)
                || KeyboardInput.IsDown(Win32Consts.VK_RBUTTON)
                || KeyboardInput.IsDown(Win32Consts.VK_MBUTTON)
                || KeyboardInput.IsDown(Win32Consts.VK_XBUTTON1)
                || KeyboardInput.IsDown(Win32Consts.VK_XBUTTON2);
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Whether the PRIMARY (logical left) button is down. GetAsyncKeyState(VK_LBUTTON) reports the PHYSICAL left button even when the user
    /// swapped the buttons (SM_SWAPBUTTON), so the logical button is looked up through the swap flag.
    /// </summary>
    public bool IsPrimaryButtonDown()
    {
        bool swapped = PInvoke.GetSystemMetrics(Windows.Win32.UI.WindowsAndMessaging.SYSTEM_METRICS_INDEX.SM_SWAPBUTTON) != 0;
        return KeyboardInput.IsDown(swapped ? Win32Consts.VK_RBUTTON : Win32Consts.VK_LBUTTON);
    }

    /// <summary>
    /// False while another desktop (Winlogon / UAC secure desktop) is the input desktop: OpenInputDesktop then fails with ACCESS_DENIED.
    /// Any other failure is ignored (fail open) so an unusual environment cannot disable hot corners for good.
    /// </summary>
    private bool InputDesktopIsOurs()
    {
        long now = Environment.TickCount64;
        if (now < _nextDesktopCheckMs)
        {
            return _desktopOk;
        }

        _nextDesktopCheckMs = now + DesktopCheckIntervalMs;
        var desktop = PInvoke.OpenInputDesktop(0, false, DESKTOP_ACCESS_FLAGS.DESKTOP_READOBJECTS);
        if (desktop.IsNull)
        {
            _desktopOk = System.Runtime.InteropServices.Marshal.GetLastPInvokeError() != Win32Consts.ERROR_ACCESS_DENIED;
            return _desktopOk;
        }

        PInvoke.CloseDesktop(desktop);
        _desktopOk = true;
        return true;
    }
}
