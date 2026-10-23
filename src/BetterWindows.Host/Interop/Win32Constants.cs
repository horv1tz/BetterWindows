namespace BetterWindows.Host.Interop;

/// <summary>
/// Win32 numeric constants used by the surfaces. They are declared here (instead of generated) because they are plain documented values and we want them as ints/nints
/// for GetWindowLongPtr arithmetic and for WndProc message switches.
/// </summary>
internal static class Win32Const
{
    // Extended window styles (WS_EX_*)
    public const nint WS_EX_TOPMOST = 0x00000008;
    public const nint WS_EX_TRANSPARENT = 0x00000020;
    public const nint WS_EX_TOOLWINDOW = 0x00000080;
    public const nint WS_EX_APPWINDOW = 0x00040000;
    public const nint WS_EX_LAYERED = 0x00080000;
    public const nint WS_EX_NOACTIVATE = 0x08000000;

    // Window styles (WS_*)
    public const long WS_POPUP = 0x80000000L;
    public const long WS_CHILD = 0x40000000L;
    public const long WS_SYSMENU = 0x00080000L;

    // Messages
    public const int WM_SHOWWINDOW = 0x0018;
    public const int WM_ACTIVATE = 0x0006;
    public const int WM_MOUSEACTIVATE = 0x0021;
    public const int WM_WINDOWPOSCHANGED = 0x0047;
    public const int WM_DISPLAYCHANGE = 0x007E;
    public const int WM_NCACTIVATE = 0x0086;
    public const int WM_SYSCOMMAND = 0x0112;
    public const int WM_DPICHANGED = 0x02E0;
    public const int WM_DWMCOMPOSITIONCHANGED = 0x031E;

    // WM_MOUSEACTIVATE results
    public const int MA_ACTIVATE = 1;
    public const int MA_NOACTIVATE = 3;
    public const int MA_NOACTIVATEANDEAT = 4;

    // WM_ACTIVATE wParam low word
    public const int WA_INACTIVE = 0;

    // DWM
    public const int DWMWA_USE_IMMERSIVE_DARK_MODE_PRE20H1 = 19;
    public const uint DWMWA_COLOR_NONE = 0xFFFFFFFE;

    /// <summary>First Windows build with DWMWA_SYSTEMBACKDROP_TYPE (Windows 11 22H2).</summary>
    public const int BuildSystemBackdrop = 22621;
    /// <summary>First Windows 11 build (rounded corner attributes).</summary>
    public const int BuildWindows11 = 22000;
}
