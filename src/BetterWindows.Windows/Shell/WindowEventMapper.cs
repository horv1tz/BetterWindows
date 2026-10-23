using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native;

/// <summary>One narrow SetWinEventHook registration (event id range).</summary>
/// <param name="Min">First event id.</param>
/// <param name="Max">Last event id (inclusive).</param>
/// <param name="Name">Diagnostic name.</param>
internal readonly record struct WinEventRange(uint Min, uint Max, string Name);

/// <summary>What a shell-hook (HSHELL_*) message means for the window system.</summary>
internal enum ShellHookAction
{
    /// <summary>Not interesting.</summary>
    None,
    /// <summary>HSHELL_FLASH: the taskbar button of the window flashes (needs attention).</summary>
    Flash,
    /// <summary>HSHELL_WINDOWACTIVATED / HSHELL_RUDEAPPACTIVATED: a top-level window was activated.</summary>
    Activated,
    /// <summary>HSHELL_REDRAW: the taskbar button was redrawn (title / state).</summary>
    Redraw,
}

/// <summary>
/// Pure tables: which WinEvents we subscribe to (several NARROW hooks, never EVENT_MIN..EVENT_MAX), how a WinEvent maps to a
/// <see cref="WindowEventKind"/>, which kinds are rate limited, and how shell-hook codes are interpreted. Cross-platform, unit-tested.
/// </summary>
internal static class WindowEventMapper
{
    /// <summary>Hook registrations installed at Start (the location hook is separate, see <see cref="LocationRange"/>).</summary>
    public static readonly IReadOnlyList<WinEventRange> HookRanges = new[]
    {
        new WinEventRange(Win32Consts.EVENT_SYSTEM_FOREGROUND, Win32Consts.EVENT_SYSTEM_FOREGROUND, "foreground"),
        new WinEventRange(Win32Consts.EVENT_SYSTEM_MINIMIZESTART, Win32Consts.EVENT_SYSTEM_MINIMIZEEND, "minimize"),
        new WinEventRange(Win32Consts.EVENT_OBJECT_CREATE, Win32Consts.EVENT_OBJECT_HIDE, "create-destroy-show-hide"),
        new WinEventRange(Win32Consts.EVENT_OBJECT_CLOAKED, Win32Consts.EVENT_OBJECT_UNCLOAKED, "cloak"),
        new WinEventRange(Win32Consts.EVENT_OBJECT_NAMECHANGE, Win32Consts.EVENT_OBJECT_NAMECHANGE, "name"),
    };

    /// <summary>EVENT_OBJECT_LOCATIONCHANGE: installed lazily, only while someone listens for full-screen changes (high volume).</summary>
    public static readonly WinEventRange LocationRange =
        new(Win32Consts.EVENT_OBJECT_LOCATIONCHANGE, Win32Consts.EVENT_OBJECT_LOCATIONCHANGE, "location");

    /// <summary>Maps a WinEvent id to the window event kind; false for ids we do not handle.</summary>
    public static bool TryMap(uint winEvent, out WindowEventKind kind)
    {
        switch (winEvent)
        {
            case Win32Consts.EVENT_SYSTEM_FOREGROUND: kind = WindowEventKind.Foreground; return true;
            case Win32Consts.EVENT_SYSTEM_MINIMIZESTART: kind = WindowEventKind.Minimized; return true;
            case Win32Consts.EVENT_SYSTEM_MINIMIZEEND: kind = WindowEventKind.Restored; return true;
            case Win32Consts.EVENT_OBJECT_CREATE: kind = WindowEventKind.Created; return true;
            case Win32Consts.EVENT_OBJECT_DESTROY: kind = WindowEventKind.Destroyed; return true;
            case Win32Consts.EVENT_OBJECT_SHOW: kind = WindowEventKind.Shown; return true;
            case Win32Consts.EVENT_OBJECT_HIDE: kind = WindowEventKind.Hidden; return true;
            case Win32Consts.EVENT_OBJECT_NAMECHANGE: kind = WindowEventKind.TitleChanged; return true;
            case Win32Consts.EVENT_OBJECT_CLOAKED: kind = WindowEventKind.Cloaked; return true;
            case Win32Consts.EVENT_OBJECT_UNCLOAKED: kind = WindowEventKind.Uncloaked; return true;
            case Win32Consts.EVENT_OBJECT_LOCATIONCHANGE: kind = WindowEventKind.Moved; return true;
            default: kind = default; return false;
        }
    }

    /// <summary>Kinds that can burst and are limited to one delivery per window per 50 ms (leading + trailing edge).</summary>
    public static bool IsThrottled(WindowEventKind kind) => kind is WindowEventKind.TitleChanged or WindowEventKind.Moved or WindowEventKind.Redraw;

    /// <summary>Kinds that may change the full-screen verdict when they concern the foreground window.</summary>
    public static bool AffectsFullscreen(WindowEventKind kind) =>
        kind is WindowEventKind.Foreground or WindowEventKind.Moved or WindowEventKind.Minimized or WindowEventKind.Restored
            or WindowEventKind.Shown or WindowEventKind.Hidden or WindowEventKind.Cloaked or WindowEventKind.Uncloaked;

    /// <summary>Classifies the wParam of a SHELLHOOK message.</summary>
    public static ShellHookAction ClassifyShellHook(int code) => code switch
    {
        Win32Consts.HSHELL_FLASH => ShellHookAction.Flash,
        Win32Consts.HSHELL_WINDOWACTIVATED or Win32Consts.HSHELL_RUDEAPPACTIVATED => ShellHookAction.Activated,
        Win32Consts.HSHELL_REDRAW => ShellHookAction.Redraw,
        _ => ShellHookAction.None,
    };
}

/// <summary>Pure helpers around the raw DWMWA_CLOAKED mask.</summary>
internal static class CloakInfo
{
    /// <summary>Any cloak reason.</summary>
    public static bool IsCloaked(uint flags) => flags != 0;

    /// <summary>
    /// The shell cloaked the window because it lives on another virtual desktop (DWM_CLOAKED_SHELL). Such windows are real, running windows.
    /// Windows cloaked only by their app (suspended UWP, DWM_CLOAKED_APP) or by inheritance are not.
    /// </summary>
    public static bool IsCloakedByShell(uint flags) => (flags & Win32Consts.DWM_CLOAKED_SHELL) != 0;
}
