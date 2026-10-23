using BetterWindows.Core.Platform;

// NOTE: the namespace is deliberately NOT "...Windows": a namespace segment called Windows shadows Windows.Win32 / WinRT projections (see AGENTS.md).
namespace BetterWindows.Core.WindowTracking;

/// <summary>Why <see cref="WindowFilter"/> refused a window (None = the window gets a dock button / Mission Control tile).</summary>
public enum WindowRejection
{
    None,
    /// <summary>Handle 0.</summary>
    InvalidHandle,
    /// <summary>A BetterWindows window: never shown, never acted upon.</summary>
    OwnProcess,
    /// <summary>Shell/system infrastructure class (Progman, Shell_TrayWnd, IME, tooltips, ...).</summary>
    ShellClass,
    NotVisible,
    /// <summary>Cloaked by the app (suspended UWP, DWM_CLOAKED_APP) or inherited from a cloaked owner: a ghost window.</summary>
    Cloaked,
    ZeroSize,
    /// <summary>Has a visible owner and no WS_EX_APPWINDOW: the owner's button represents it.</summary>
    Owned,
    /// <summary>WS_EX_TOOLWINDOW without WS_EX_APPWINDOW.</summary>
    ToolWindow,
    /// <summary>WS_EX_NOACTIVATE without WS_EX_APPWINDOW (tooltips, overlays, on-screen helpers).</summary>
    NoActivate,
    EmptyTitle,
}

/// <summary>
/// Decides whether a <see cref="RawWindow"/> appears in the dock and in Mission Control "like a Windows taskbar button". Pure and table-driven.
/// The rules mirror the empirical taskbar rules (docs/ARCHITECTURE.md section 4.1):
/// visible, not ours, not a shell/system class, not a ghost (cloaked by the app) but windows cloaked ONLY by the shell are on another virtual desktop and
/// stay eligible, non-zero size, no visible owner (unless WS_EX_APPWINDOW), no WS_EX_TOOLWINDOW / WS_EX_NOACTIVATE helpers (unless WS_EX_APPWINDOW),
/// and a title (unless WS_EX_APPWINDOW or a class known to have none).
/// </summary>
public static class WindowFilter
{
    public const uint CloakedByApp = 1, CloakedByShell = 2, CloakedInherited = 4;

    /// <summary>Window classes of the shell and of system helper windows: none of them ever gets a taskbar button.</summary>
    private static readonly HashSet<string> ShellClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",                              // desktop (Program Manager)
        "WorkerW",                              // desktop wallpaper host
        "Shell_TrayWnd",                        // primary taskbar
        "Shell_SecondaryTrayWnd",               // taskbar on other monitors
        "TrayNotifyWnd", "TrayShowDesktopButtonWClass", "NotifyIconOverflowWindow", "TopLevelWindowForOverflowXamlIsland",
        "TaskListThumbnailWnd", "TaskListOverlayWnd", "Shell_LightDismissOverlay", "Shell_InputSwitchTopLevelWindow",
        "XamlExplorerHostIslandWindow",         // Windows 11 shell XAML islands (taskbar flyouts, snap layouts, Task View host)
        "MultitaskingViewFrame",                // Task View (Windows 10)
        "ForegroundStaging", "ApplicationManager_DesktopShellWindow", "ApplicationManager_ImmersiveShellWindow",
        "EdgeUiInputTopWndClass", "EdgeUiInputWndClass", "ImmersiveLauncher", "DV2ControlHost",
        "Windows.UI.Core.CoreWindow",           // top-level CoreWindows are shell experiences (Start, Search, lock app); real UWP apps appear as ApplicationFrameWindow
        "Windows.UI.Input.InputSite.WindowClass", "Windows.UI.Composition.DesktopWindowContentBridge",
        "IME", "MSCTFIME UI", "CiceroUIWndFrame", "SysShadow",
        "tooltips_class32", "#32768",           // tooltips and popup menus
        "BroadcastListenerWindow", "DummyDWMListenerWindow", "MediaContextNotificationWindow", "SystemResourceNotifyWindow", "GDI+ Hook Window Class",
        "PseudoConsoleWindow",                  // ConPTY helper
    };

    /// <summary>Classes whose windows legitimately have an empty title (consoles, games) and still deserve a button.</summary>
    private static readonly HashSet<string> EmptyTitleClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "ConsoleWindowClass", "CASCADIA_HOSTING_WINDOW_CLASS", "UnityWndClass", "UnrealWindow", "SDL_app", "GLFW30",
    };

    public static bool IsShellClass(string? className) => !string.IsNullOrEmpty(className) && ShellClasses.Contains(className);

    /// <summary>
    /// The cloak mask to reason about: <see cref="RawWindow.CloakedFlags"/>, or - for a producer that only filled the booleans - IsCloaked / IsCloakedByShell
    /// (IsCloakedByShell alone maps to the shell bit, a plain IsCloaked to the app bit).
    /// </summary>
    public static uint EffectiveCloakFlags(RawWindow w)
    {
        if (w.CloakedFlags != 0) return w.CloakedFlags;
        if (w.IsCloakedByShell) return CloakedByShell;
        return w.IsCloaked ? CloakedByApp : 0;
    }

    /// <summary>True for a window that lives on another virtual desktop: cloaked ONLY by the shell bit.</summary>
    public static bool IsOnOtherDesktop(RawWindow w) => EffectiveCloakFlags(w) == CloakedByShell;

    public static bool IsEligible(RawWindow w, Func<long, bool>? isVisibleWindow = null) => Evaluate(w, isVisibleWindow) == WindowRejection.None;

    /// <param name="w">The window.</param>
    /// <param name="isVisibleWindow">
    /// Tells whether a window handle (the owner) currently exists and is visible. null = "assume the owner is visible", which is the conservative answer: an
    /// owned window is then never shown. The tracker passes a lookup over the same enumeration so that windows owned by a hidden helper window (very common in
    /// .NET/Java/Delphi apps) still get their button.
    /// </param>
    public static WindowRejection Evaluate(RawWindow w, Func<long, bool>? isVisibleWindow = null)
    {
        if (w.Handle == 0) return WindowRejection.InvalidHandle;
        if (w.IsOwnProcess) return WindowRejection.OwnProcess;
        if (IsShellClass(w.ClassName)) return WindowRejection.ShellClass;
        if (!w.IsVisible) return WindowRejection.NotVisible;

        var cloak = EffectiveCloakFlags(w);
        if (cloak != 0 && cloak != CloakedByShell) return WindowRejection.Cloaked;

        // Minimised windows can report degenerate frame bounds; windows on other desktops keep their real bounds.
        if (!w.IsMinimized && (w.Bounds.Width <= 0 || w.Bounds.Height <= 0)) return WindowRejection.ZeroSize;

        if (!w.IsAppWindow)
        {
            if (w.OwnerHandle != 0 && (isVisibleWindow is null || isVisibleWindow(w.OwnerHandle))) return WindowRejection.Owned;
            if (w.IsToolWindow) return WindowRejection.ToolWindow;
            if (w.IsNoActivate) return WindowRejection.NoActivate;
        }

        if (string.IsNullOrWhiteSpace(w.Title) && !w.IsAppWindow && !EmptyTitleClasses.Contains(w.ClassName ?? "")) return WindowRejection.EmptyTitle;
        return WindowRejection.None;
    }
}
