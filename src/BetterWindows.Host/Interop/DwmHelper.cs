using Windows.Win32;
using Windows.Win32.Foundation;
using Windows.Win32.Graphics.Dwm;
using Windows.Win32.UI.Controls;

namespace BetterWindows.Host.Interop;

/// <summary>The system backdrop kinds BetterWindows uses (DWM_SYSTEMBACKDROP_TYPE).</summary>
internal enum SystemBackdrop
{
    /// <summary>DWMSBT_NONE: no backdrop.</summary>
    None = 1,
    /// <summary>DWMSBT_MAINWINDOW (Mica): the settings window. Mica falls back to a flat colour while the window is inactive.</summary>
    Mica = 2,
    /// <summary>DWMSBT_TRANSIENTWINDOW (acrylic): the dock and menu bar. They are never activated (WS_EX_NOACTIVATE), and only acrylic keeps its blur on inactive windows.</summary>
    Acrylic = 3,
}

/// <summary>
/// DWM attribute setters with HRESULT checks. Each method returns the HRESULT-derived success flag and the failing HRESULT (as an int) for logging; nothing throws.
/// WHY these exist at all: WPF has no API for system backdrops, immersive dark mode or corner preferences, and DwmEnableBlurBehindWindow (the old blur API) produces NO blur
/// since Windows 8 (docs/IPC.md section 5), so the documented Windows 11 attribute DWMWA_SYSTEMBACKDROP_TYPE is the only supported way to get a real blurred backdrop.
/// </summary>
internal static class DwmHelper
{
    /// <summary>Windows build number as reported by the OS (Environment.OSVersion is accurate on .NET 8; no manifest lie).</summary>
    public static int OsBuild { get; } = Environment.OSVersion.Version.Build;

    /// <summary>True on Windows 11 22H2 and newer: DWMWA_SYSTEMBACKDROP_TYPE exists. Earlier builds return E_INVALIDARG for the attribute.</summary>
    public static bool IsSystemBackdropSupported => OsBuild >= Win32Const.BuildSystemBackdrop;

    public static bool IsWindows11 => OsBuild >= Win32Const.BuildWindows11;

    public static unsafe bool TrySetInt(nint hwnd, DWMWINDOWATTRIBUTE attribute, int value, out int hresult)
    {
        hresult = 0;
        if (hwnd == 0) { hresult = unchecked((int)0x80070578); return false; } // ERROR_INVALID_WINDOW_HANDLE
        var v = value;
        var hr = HostPInvoke.DwmSetWindowAttribute(hwnd.ToHwnd(), attribute, &v, sizeof(int));
        hresult = hr.Value;
        return hr.Succeeded;
    }

    /// <summary>DWMWA_SYSTEMBACKDROP_TYPE. No-op (false, E_NOTIMPL-like) on builds before 22621.</summary>
    public static bool TrySetBackdrop(nint hwnd, SystemBackdrop type, out int hresult)
    {
        if (!IsSystemBackdropSupported) { hresult = unchecked((int)0x80004001); return false; }
        return TrySetInt(hwnd, DWMWINDOWATTRIBUTE.DWMWA_SYSTEMBACKDROP_TYPE, (int)type, out hresult);
    }

    /// <summary>
    /// DWMWA_USE_IMMERSIVE_DARK_MODE: selects the dark title bar for the settings window and the dark tint of the Mica/acrylic backdrop. Attribute 20 exists from
    /// Windows 10 20H1 (build 18985+); attribute 19 was the pre-release value and is tried as a fallback.
    /// </summary>
    public static bool TrySetDarkMode(nint hwnd, bool dark, out int hresult)
    {
        if (TrySetInt(hwnd, DWMWINDOWATTRIBUTE.DWMWA_USE_IMMERSIVE_DARK_MODE, dark ? 1 : 0, out hresult)) return true;
        return TrySetInt(hwnd, (DWMWINDOWATTRIBUTE)Win32Const.DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1, dark ? 1 : 0, out hresult);
    }

    /// <summary>
    /// DWMWA_WINDOW_CORNER_PREFERENCE = DONOTROUND for our region-shaped windows: DWM's own rounding (8 px) would otherwise clip a rectangular full-width menu bar or the
    /// overlay at the corners. Windows 11 only; failure is harmless.
    /// </summary>
    public static bool TrySetCorners(nint hwnd, DWM_WINDOW_CORNER_PREFERENCE preference, out int hresult)
    {
        if (!IsWindows11) { hresult = unchecked((int)0x80004001); return false; }
        return TrySetInt(hwnd, DWMWINDOWATTRIBUTE.DWMWA_WINDOW_CORNER_PREFERENCE, (int)preference, out hresult);
    }

    /// <summary>DWMWA_BORDER_COLOR = DWMWA_COLOR_NONE: remove the 1 px accent border Windows 11 draws around some top-level windows.</summary>
    public static bool TrySetNoBorder(nint hwnd, out int hresult)
    {
        if (!IsWindows11) { hresult = unchecked((int)0x80004001); return false; }
        return TrySetInt(hwnd, DWMWINDOWATTRIBUTE.DWMWA_BORDER_COLOR, unchecked((int)Win32Const.DWMWA_COLOR_NONE), out hresult);
    }

    /// <summary>
    /// DwmExtendFrameIntoClientArea(-1, -1, -1, -1): the "sheet of glass". WHY: a system backdrop is only composed behind the parts of the window that belong to the DWM
    /// frame, and fully transparent (alpha 0) client pixels show the backdrop only when the frame is extended into them. -1 margins extend it over the whole client area.
    /// <paramref name="enable"/> false restores zero margins (no glass: a plain opaque window).
    /// </summary>
    public static bool TryExtendFrame(nint hwnd, bool enable, out int hresult)
    {
        hresult = 0;
        if (hwnd == 0) { hresult = unchecked((int)0x80070578); return false; }
        var m = enable ? new MARGINS { cxLeftWidth = -1, cxRightWidth = -1, cyTopHeight = -1, cyBottomHeight = -1 } : default;
        var hr = HostPInvoke.DwmExtendFrameIntoClientArea(hwnd.ToHwnd(), in m);
        hresult = hr.Value;
        return hr.Succeeded;
    }
}
