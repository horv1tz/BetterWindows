namespace BetterWindows.Native.Interop;

/// <summary>
/// Numeric Win32 constants used by the shell layer. Kept as plain literals (documented in the Windows SDK headers) so the
/// call sites read like the MSDN pages and no CsWin32 constant needs to be generated for them.
/// </summary>
internal static class Win32Consts
{
    // ---- window messages ----
    public const uint WM_DESTROY = 0x0002;
    public const uint WM_ACTIVATE = 0x0006;
    public const uint WM_CLOSE = 0x0010;
    public const uint WM_GETTEXT = 0x000D;
    public const uint WM_SETTINGCHANGE = 0x001A;
    public const uint WM_WINDOWPOSCHANGED = 0x0047;
    public const uint WM_DISPLAYCHANGE = 0x007E;
    public const uint WM_NCCREATE = 0x0081;
    public const uint WM_NCDESTROY = 0x0082;
    public const uint WM_SYSCOMMAND = 0x0112;
    public const uint WM_TIMER = 0x0113;
    public const uint WM_HOTKEY = 0x0312;
    public const uint WM_DPICHANGED = 0x02E0;
    public const uint WM_APP = 0x8000;

    // ---- SystemParametersInfo actions seen in WM_SETTINGCHANGE.wParam ----
    public const uint SPI_SETWORKAREA = 0x002F;
    public const uint SPI_SETLOGICALDPIOVERRIDE = 0x009F;

    // ---- WinEvent ids (winuser.h) ----
    public const uint EVENT_SYSTEM_FOREGROUND = 0x0003;
    public const uint EVENT_SYSTEM_MINIMIZESTART = 0x0016;
    public const uint EVENT_SYSTEM_MINIMIZEEND = 0x0017;
    public const uint EVENT_OBJECT_CREATE = 0x8000;
    public const uint EVENT_OBJECT_DESTROY = 0x8001;
    public const uint EVENT_OBJECT_SHOW = 0x8002;
    public const uint EVENT_OBJECT_HIDE = 0x8003;
    public const uint EVENT_OBJECT_LOCATIONCHANGE = 0x800B;
    public const uint EVENT_OBJECT_NAMECHANGE = 0x800C;
    public const uint EVENT_OBJECT_CLOAKED = 0x8017;
    public const uint EVENT_OBJECT_UNCLOAKED = 0x8018;
    public const int OBJID_WINDOW = 0;
    public const int CHILDID_SELF = 0;
    public const uint WINEVENT_OUTOFCONTEXT = 0x0000;
    public const uint WINEVENT_SKIPOWNPROCESS = 0x0002;

    // ---- shell hook (RegisterShellHookWindow) wParam codes ----
    public const int HSHELL_WINDOWACTIVATED = 4;
    public const int HSHELL_REDRAW = 6;
    public const int HSHELL_HIGHBIT = 0x8000;
    public const int HSHELL_RUDEAPPACTIVATED = HSHELL_WINDOWACTIVATED | HSHELL_HIGHBIT;
    public const int HSHELL_FLASH = HSHELL_REDRAW | HSHELL_HIGHBIT;

    // ---- window styles ----
    public const uint WS_MINIMIZE = 0x20000000;
    public const uint WS_MAXIMIZE = 0x01000000;
    public const uint WS_VISIBLE = 0x10000000;
    public const uint WS_CAPTION = 0x00C00000;
    public const uint WS_THICKFRAME = 0x00040000;
    public const uint WS_POPUP = 0x80000000;
    public const uint WS_EX_TOOLWINDOW = 0x00000080;
    public const uint WS_EX_APPWINDOW = 0x00040000;
    public const uint WS_EX_NOACTIVATE = 0x08000000;

    // ---- DWM ----
    public const uint DWM_CLOAKED_APP = 1;
    public const uint DWM_CLOAKED_SHELL = 2;
    public const uint DWM_CLOAKED_INHERITED = 4;

    // ---- SHAppBarMessage ----
    public const uint ABM_NEW = 0, ABM_REMOVE = 1, ABM_QUERYPOS = 2, ABM_SETPOS = 3, ABM_GETSTATE = 4, ABM_GETTASKBARPOS = 5,
        ABM_ACTIVATE = 6, ABM_GETAUTOHIDEBAR = 7, ABM_SETAUTOHIDEBAR = 8, ABM_WINDOWPOSCHANGED = 9, ABM_SETSTATE = 10;
    public const uint ABE_LEFT = 0, ABE_TOP = 1, ABE_RIGHT = 2, ABE_BOTTOM = 3;
    public const uint ABS_AUTOHIDE = 1, ABS_ALWAYSONTOP = 2;
    public const int ABN_STATECHANGE = 0, ABN_POSCHANGED = 1, ABN_FULLSCREENAPP = 2, ABN_WINDOWARRANGE = 3;

    // ---- hotkeys ----
    public const uint MOD_NOREPEAT = 0x4000;

    // ---- error codes ----
    public const int ERROR_ACCESS_DENIED = 5;
    public const int ERROR_INSUFFICIENT_BUFFER = 122;
    public const int ERROR_HOTKEY_ALREADY_REGISTERED = 1409;
    public const int ERROR_CLASS_ALREADY_EXISTS = 1410;
    public const int APPMODEL_ERROR_NO_PACKAGE = 15700;
    public const int APPMODEL_ERROR_NO_APPLICATION = 15703;

    // ---- virtual keys ----
    public const ushort VK_LBUTTON = 0x01, VK_RBUTTON = 0x02, VK_MBUTTON = 0x04, VK_XBUTTON1 = 0x05, VK_XBUTTON2 = 0x06;
    public const ushort VK_TAB = 0x09, VK_SHIFT = 0x10, VK_CONTROL = 0x11, VK_MENU = 0x12;
    public const ushort VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    public const ushort VK_D = 0x44;
    public const ushort VK_LWIN = 0x5B, VK_RWIN = 0x5C;
    public const ushort VK_LSHIFT = 0xA0, VK_RSHIFT = 0xA1, VK_LCONTROL = 0xA2, VK_RCONTROL = 0xA3, VK_LMENU = 0xA4, VK_RMENU = 0xA5;
    /// <summary>Unassigned virtual key (0xE8), used to "mask" an Alt tap so the foreground app does not activate its menu bar.</summary>
    public const ushort VK_UNASSIGNED_MASK = 0xE8;

    public const int SM_SWAPBUTTON = 23;
}
