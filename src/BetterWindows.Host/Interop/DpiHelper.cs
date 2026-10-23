using BetterWindows.Core.Platform;
using BetterWindows.Host.Surfaces;
using Windows.Win32;
using Windows.Win32.Graphics.Gdi;
using Windows.Win32.UI.HiDpi;

namespace BetterWindows.Host.Interop;

/// <summary>
/// Per-monitor DPI queries. The process is PerMonitorV2 (app.manifest), so GetDpiForWindow returns the DPI of the monitor the window is currently on and changes (with a
/// WM_DPICHANGED) as the window moves. Surfaces express sizes in DIPs, so the scale in effect for the WINDOW (not just the target monitor) decides what the page renders at.
/// </summary>
internal static class DpiHelper
{
    /// <summary>Scale factor of a window (GetDpiForWindow / 96); <paramref name="fallback"/> when the window is gone or the call fails.</summary>
    public static double GetScaleForWindow(nint hwnd, double fallback = 1.0)
    {
        if (hwnd == 0) return fallback;
        var dpi = PInvoke.GetDpiForWindow(hwnd.ToHwnd());
        return dpi == 0 ? fallback : DpiMath.ScaleFromDpi(dpi);
    }

    /// <summary>Effective DPI scale of the monitor a window is on, via MonitorFromWindow + GetDpiForMonitor(MDT_EFFECTIVE_DPI).</summary>
    public static double GetScaleForMonitorOfWindow(nint hwnd, double fallback = 1.0)
    {
        if (hwnd == 0) return fallback;
        var mon = PInvoke.MonitorFromWindow(hwnd.ToHwnd(), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        return GetScaleForMonitor(mon, fallback);
    }

    public static unsafe double GetScaleForMonitor(HMONITOR monitor, double fallback = 1.0)
    {
        if (monitor.Value == null) return fallback;
        var hr = PInvoke.GetDpiForMonitor(monitor, MONITOR_DPI_TYPE.MDT_EFFECTIVE_DPI, out var dpiX, out _);
        return hr.Failed || dpiX == 0 ? fallback : DpiMath.ScaleFromDpi(dpiX);
    }

    /// <summary>Bounds and work area of the monitor nearest to a window (virtual-screen px); false when the query fails.</summary>
    public static bool TryGetMonitorRects(nint hwnd, out NativeRect bounds, out NativeRect workArea)
    {
        bounds = workArea = default;
        if (hwnd == 0) return false;
        var mon = PInvoke.MonitorFromWindow(hwnd.ToHwnd(), MONITOR_FROM_FLAGS.MONITOR_DEFAULTTONEAREST);
        var info = new MONITORINFO { cbSize = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MONITORINFO>() };
        if (!PInvoke.GetMonitorInfo(mon, ref info)) return false;
        bounds = info.rcMonitor.ToNativeRect();
        workArea = info.rcWork.ToNativeRect();
        return true;
    }
}
