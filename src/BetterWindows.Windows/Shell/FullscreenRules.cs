using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native;

/// <summary>Everything the full-screen decision needs to know about the foreground window (pure data, gathered by the caller).</summary>
internal readonly record struct FullscreenFacts(
    string ClassName,
    bool IsOwnProcess,
    bool IsVisible,
    bool IsCloaked,
    bool IsMinimized,
    bool IsMaximized,
    uint Style,
    uint ExStyle,
    NativeRect FrameBounds);

/// <summary>Positive hint from SHQueryUserNotificationState.</summary>
internal enum FullscreenHint
{
    /// <summary>No hint / state says nothing special.</summary>
    None,
    /// <summary>QUNS_BUSY, QUNS_RUNNING_D3D_FULL_SCREEN, QUNS_PRESENTATION_MODE or QUNS_APP: the shell believes a full-screen app is running.</summary>
    FullscreenApp,
}

/// <summary>
/// The NORMATIVE full-screen rules of <c>IWindowSystem.IsFullscreenWindowActive</c> as a pure function: the foreground window of ANOTHER
/// process, visible, not minimised, not cloaked, not a shell window class, not a tool window, whose DWMWA_EXTENDED_FRAME_BOUNDS cover the
/// monitor within 1 px - and which is not an ordinary maximised window with a caption / sizing frame (maximised windows cover the work area,
/// and only touch the monitor edges when no appbar is reserved; they must never hide the dock and menu bar). A positive shell hint
/// (SHQueryUserNotificationState) widens the tolerance to 8 px and also admits captioned frames (UWP full screen).
/// </summary>
internal static class FullscreenRules
{
    /// <summary>Slack in pixels per side for "covers the monitor".</summary>
    public const int ToleranceStrictPx = 1;

    /// <summary>Slack granted when the shell positively reports a full-screen app (mode switches round the frame by a few pixels).</summary>
    public const int ToleranceHintPx = 8;

    private static readonly HashSet<string> ExcludedClasses = new(StringComparer.OrdinalIgnoreCase)
    {
        "Progman",
        "WorkerW",
        "Shell_TrayWnd",
        "Shell_SecondaryTrayWnd",
        "XamlExplorerHostIslandWindow",
        "MultitaskingViewFrame",
        "Windows.UI.Core.CoreWindow",
        "ForegroundStaging",
        "TaskSwitcherWnd",
        "TaskSwitcherOverlayWnd",
        "Shell_LightDismissOverlay",
        "NotifyIconOverflowWindow",
        "TopLevelWindowForOverflowXamlIsland",
        "ApplicationManager_ImmersiveShellWindow",
    };

    /// <summary>True for shell / desktop window classes that can never be a full-screen app.</summary>
    public static bool IsExcludedClass(string className) => ExcludedClasses.Contains(className);

    /// <summary>The window is maximised in the ordinary way (captioned or sizable), as opposed to a borderless popup / F11 window.</summary>
    public static bool IsOrdinaryMaximized(in FullscreenFacts f) =>
        f.IsMaximized && ((f.Style & Win32Consts.WS_CAPTION) == Win32Consts.WS_CAPTION || (f.Style & Win32Consts.WS_THICKFRAME) != 0);

    /// <summary>Applies the rules for one monitor.</summary>
    public static bool Evaluate(in FullscreenFacts f, NativeRect monitorBounds, FullscreenHint hint)
    {
        if (f.IsOwnProcess || !f.IsVisible || f.IsCloaked || f.IsMinimized)
        {
            return false;
        }

        if (IsExcludedClass(f.ClassName))
        {
            return false;
        }

        // Tool windows (palettes, floating toolbars) are never "the" full-screen app, unless the app explicitly asks for a taskbar button.
        if ((f.ExStyle & Win32Consts.WS_EX_TOOLWINDOW) != 0 && (f.ExStyle & Win32Consts.WS_EX_APPWINDOW) == 0)
        {
            return false;
        }

        int tolerance = hint == FullscreenHint.FullscreenApp ? ToleranceHintPx : ToleranceStrictPx;
        if (!RectMath.Covers(f.FrameBounds, monitorBounds, tolerance))
        {
            return false;
        }

        // The shell itself says a full-screen app is running (UWP full-screen, borderless game, presentation mode) and this window covers the
        // monitor: trust it even if the window keeps a caption / sizing frame style (UWP frames do).
        if (hint == FullscreenHint.FullscreenApp)
        {
            return true;
        }

        return !IsOrdinaryMaximized(f);
    }

    /// <summary>
    /// True when asking the shell for a hint could change the verdict (the rules say "no" but would say "yes" with a positive hint).
    /// Lets the caller skip the SHQueryUserNotificationState call in the common case.
    /// </summary>
    public static bool HintCouldChangeVerdict(in FullscreenFacts f, NativeRect monitorBounds) =>
        !Evaluate(f, monitorBounds, FullscreenHint.None) && Evaluate(f, monitorBounds, FullscreenHint.FullscreenApp);
}
