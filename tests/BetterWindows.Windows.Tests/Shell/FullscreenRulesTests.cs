using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>The normative full-screen rules as a pure function; runs everywhere.</summary>
public class FullscreenRulesTests
{
    private static readonly NativeRect Monitor = new(0, 0, 1920, 1080);

    private static FullscreenFacts Game(Func<FullscreenFacts, FullscreenFacts>? tweak = null)
    {
        var f = new FullscreenFacts(
            ClassName: "UnityWndClass",
            IsOwnProcess: false,
            IsVisible: true,
            IsCloaked: false,
            IsMinimized: false,
            IsMaximized: false,
            Style: Win32Consts.WS_POPUP,
            ExStyle: 0,
            FrameBounds: Monitor);
        return tweak == null ? f : tweak(f);
    }

    [Fact]
    public void Borderless_window_covering_the_monitor_is_fullscreen() =>
        FullscreenRules.Evaluate(Game(), Monitor, FullscreenHint.None).Should().BeTrue();

    [Fact]
    public void One_pixel_of_slack_is_accepted_two_are_not()
    {
        FullscreenRules.Evaluate(Game(f => f with { FrameBounds = new NativeRect(-1, -1, 1921, 1081) }), Monitor, FullscreenHint.None).Should().BeTrue();
        FullscreenRules.Evaluate(Game(f => f with { FrameBounds = new NativeRect(2, 0, 1920, 1080) }), Monitor, FullscreenHint.None).Should().BeFalse();
    }

    [Fact]
    public void Ordinary_maximised_captioned_window_is_not_fullscreen()
    {
        // Maximised window with taskbar + appbars hidden: frame == monitor, but it has a caption and the maximised state.
        var maximised = Game(f => f with
        {
            ClassName = "Chrome_WidgetWin_1",
            IsMaximized = true,
            Style = Win32Consts.WS_CAPTION | Win32Consts.WS_THICKFRAME | Win32Consts.WS_MAXIMIZE,
        });
        FullscreenRules.Evaluate(maximised, Monitor, FullscreenHint.None).Should().BeFalse();
    }

    [Fact]
    public void Maximised_sizable_window_without_caption_is_still_ordinary()
    {
        var frameless = Game(f => f with { IsMaximized = true, Style = Win32Consts.WS_THICKFRAME | Win32Consts.WS_MAXIMIZE });
        FullscreenRules.Evaluate(frameless, Monitor, FullscreenHint.None).Should().BeFalse();
    }

    [Fact]
    public void F11_style_fullscreen_without_caption_or_sizing_frame_counts()
    {
        var f11 = Game(f => f with { ClassName = "MozillaWindowClass", IsMaximized = false, Style = Win32Consts.WS_POPUP });
        FullscreenRules.Evaluate(f11, Monitor, FullscreenHint.None).Should().BeTrue();
    }

    [Theory]
    [InlineData("Progman")]
    [InlineData("WorkerW")]
    [InlineData("Shell_TrayWnd")]
    [InlineData("Shell_SecondaryTrayWnd")]
    [InlineData("XamlExplorerHostIslandWindow")]
    [InlineData("MultitaskingViewFrame")]
    [InlineData("Windows.UI.Core.CoreWindow")]
    [InlineData("progman")]
    public void Shell_classes_are_never_fullscreen_apps(string cls) =>
        FullscreenRules.Evaluate(Game(f => f with { ClassName = cls }), Monitor, FullscreenHint.None).Should().BeFalse();

    [Fact]
    public void Own_process_invisible_cloaked_and_minimised_windows_are_excluded()
    {
        FullscreenRules.Evaluate(Game(f => f with { IsOwnProcess = true }), Monitor, FullscreenHint.None).Should().BeFalse();
        FullscreenRules.Evaluate(Game(f => f with { IsVisible = false }), Monitor, FullscreenHint.None).Should().BeFalse();
        FullscreenRules.Evaluate(Game(f => f with { IsCloaked = true }), Monitor, FullscreenHint.None).Should().BeFalse();
        FullscreenRules.Evaluate(Game(f => f with { IsMinimized = true }), Monitor, FullscreenHint.None).Should().BeFalse();
    }

    [Fact]
    public void Tool_windows_are_excluded_unless_they_ask_for_a_taskbar_button()
    {
        FullscreenRules.Evaluate(Game(f => f with { ExStyle = Win32Consts.WS_EX_TOOLWINDOW }), Monitor, FullscreenHint.None).Should().BeFalse();
        FullscreenRules.Evaluate(Game(f => f with { ExStyle = Win32Consts.WS_EX_TOOLWINDOW | Win32Consts.WS_EX_APPWINDOW }), Monitor, FullscreenHint.None).Should().BeTrue();
    }

    [Fact]
    public void Captioned_maximised_frame_counts_only_with_a_positive_shell_hint()
    {
        var uwp = Game(f => f with
        {
            ClassName = "ApplicationFrameWindow",
            IsMaximized = true,
            Style = Win32Consts.WS_CAPTION | Win32Consts.WS_THICKFRAME | Win32Consts.WS_MAXIMIZE,
        });
        FullscreenRules.Evaluate(uwp, Monitor, FullscreenHint.None).Should().BeFalse();
        FullscreenRules.HintCouldChangeVerdict(uwp, Monitor).Should().BeTrue();
        FullscreenRules.Evaluate(uwp, Monitor, FullscreenHint.FullscreenApp).Should().BeTrue();
    }

    [Fact]
    public void A_clearly_fullscreen_window_does_not_need_the_hint() =>
        FullscreenRules.HintCouldChangeVerdict(Game(), Monitor).Should().BeFalse();

    [Fact]
    public void The_hint_never_overrides_the_exclusions()
    {
        FullscreenRules.Evaluate(Game(f => f with { ClassName = "Progman" }), Monitor, FullscreenHint.FullscreenApp).Should().BeFalse();
        FullscreenRules.Evaluate(Game(f => f with { IsOwnProcess = true }), Monitor, FullscreenHint.FullscreenApp).Should().BeFalse();
        FullscreenRules.Evaluate(Game(f => f with { IsMinimized = true }), Monitor, FullscreenHint.FullscreenApp).Should().BeFalse();
    }

    [Fact]
    public void Window_covering_only_the_work_area_is_not_fullscreen()
    {
        var workArea = new NativeRect(0, 28, 1920, 1080);
        FullscreenRules.Evaluate(Game(f => f with { FrameBounds = workArea }), Monitor, FullscreenHint.None).Should().BeFalse();
    }

    [Fact]
    public void Window_on_another_monitor_does_not_count_for_this_one()
    {
        var other = new NativeRect(1920, 0, 3840, 1080);
        FullscreenRules.Evaluate(Game(f => f with { FrameBounds = other }), Monitor, FullscreenHint.None).Should().BeFalse();
        FullscreenRules.Evaluate(Game(f => f with { FrameBounds = other }), other, FullscreenHint.None).Should().BeTrue();
    }

    [Fact]
    public void Shell_hint_relaxes_the_tolerance_for_mode_switch_rounding()
    {
        var slightlyOff = Game(f => f with { FrameBounds = new NativeRect(0, 0, 1916, 1078) });
        FullscreenRules.Evaluate(slightlyOff, Monitor, FullscreenHint.None).Should().BeFalse();
        FullscreenRules.HintCouldChangeVerdict(slightlyOff, Monitor).Should().BeTrue();
        FullscreenRules.Evaluate(slightlyOff, Monitor, FullscreenHint.FullscreenApp).Should().BeTrue();
    }

    [Fact]
    public void Hint_never_turns_a_small_window_into_a_fullscreen_one()
    {
        var small = Game(f => f with { FrameBounds = new NativeRect(100, 100, 900, 700) });
        FullscreenRules.HintCouldChangeVerdict(small, Monitor).Should().BeFalse();
        FullscreenRules.Evaluate(small, Monitor, FullscreenHint.FullscreenApp).Should().BeFalse();
    }
}
