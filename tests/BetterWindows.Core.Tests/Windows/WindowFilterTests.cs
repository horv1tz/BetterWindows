using BetterWindows.Core.Platform;
using BetterWindows.Core.Tests.Support.Fakes;
using BetterWindows.Core.WindowTracking;

namespace BetterWindows.Core.Tests.WindowTracking;

public class WindowFilterTests
{
    private static RawWindow Ok(Func<RawWindow, RawWindow>? tweak = null)
    {
        var w = FakeWindowSystem.Win(10, "Document - App");
        return tweak is null ? w : tweak(w);
    }

    private static object[] R(string why, WindowRejection expected, Func<RawWindow, RawWindow> tweak) => new object[] { why, expected, tweak };
    private static object[] A(string why, Func<RawWindow, RawWindow> tweak) => new object[] { why, tweak };

    public static IEnumerable<object[]> Rejections() => new[]
    {
        R("handle zero", WindowRejection.InvalidHandle, (w => w with { Handle = 0 })),
        R("own process", WindowRejection.OwnProcess, w => w with { IsOwnProcess = true }),
        R("invisible", WindowRejection.NotVisible, w => w with { IsVisible = false }),
        R("cloaked by app (suspended UWP)", WindowRejection.Cloaked, w => w with { IsCloaked = true, CloakedFlags = 1 }),
        R("cloaked inherited", WindowRejection.Cloaked, w => w with { IsCloaked = true, CloakedFlags = 4 }),
        R("cloaked by shell and app", WindowRejection.Cloaked, w => w with { IsCloaked = true, CloakedFlags = 3 }),
        R("cloaked by shell and inherited", WindowRejection.Cloaked, w => w with { IsCloaked = true, CloakedFlags = 6 }),
        R("only the IsCloaked boolean set", WindowRejection.Cloaked, w => w with { IsCloaked = true }),
        R("zero width", WindowRejection.ZeroSize, w => w with { Bounds = new NativeRect(10, 10, 10, 200) }),
        R("zero height", WindowRejection.ZeroSize, w => w with { Bounds = new NativeRect(10, 10, 200, 10) }),
        R("default bounds", WindowRejection.ZeroSize, w => w with { Bounds = default }),
        R("negative size", WindowRejection.ZeroSize, w => w with { Bounds = new NativeRect(50, 50, 10, 10) }),
        R("visible owner", WindowRejection.Owned, w => w with { OwnerHandle = 5 }),
        R("tool window", WindowRejection.ToolWindow, w => w with { IsToolWindow = true }),
        R("no-activate helper", WindowRejection.NoActivate, w => w with { IsNoActivate = true }),
        R("empty title", WindowRejection.EmptyTitle, w => w with { Title = "" }),
        R("whitespace title", WindowRejection.EmptyTitle, w => w with { Title = "  \t " }),
        R("Progman", WindowRejection.ShellClass, w => w with { ClassName = "Progman" }),
        R("WorkerW", WindowRejection.ShellClass, w => w with { ClassName = "WorkerW" }),
        R("Shell_TrayWnd", WindowRejection.ShellClass, w => w with { ClassName = "Shell_TrayWnd" }),
        R("Shell_SecondaryTrayWnd", WindowRejection.ShellClass, w => w with { ClassName = "Shell_SecondaryTrayWnd" }),
        R("NotifyIconOverflowWindow", WindowRejection.ShellClass, w => w with { ClassName = "NotifyIconOverflowWindow" }),
        R("top-level CoreWindow (Start, Search)", WindowRejection.ShellClass, w => w with { ClassName = "Windows.UI.Core.CoreWindow" }),
        R("XAML island host", WindowRejection.ShellClass, w => w with { ClassName = "XamlExplorerHostIslandWindow" }),
        R("IME window", WindowRejection.ShellClass, w => w with { ClassName = "IME" }),
        R("tooltip", WindowRejection.ShellClass, w => w with { ClassName = "tooltips_class32" }),
        R("popup menu", WindowRejection.ShellClass, w => w with { ClassName = "#32768" }),
        R("shell class is matched case-insensitively", WindowRejection.ShellClass, w => w with { ClassName = "PROGMAN" }),
    };

    [Theory]
    [MemberData(nameof(Rejections))]
    public void Rejects(string why, WindowRejection expected, Func<RawWindow, RawWindow> tweak)
    {
        _ = why;
        WindowFilter.Evaluate(Ok(tweak)).Should().Be(expected);
        WindowFilter.IsEligible(Ok(tweak)).Should().BeFalse();
    }

    public static IEnumerable<object[]> Accepts() => new[]
    {
        A("plain window", (w => w)),
        A("on another virtual desktop (cloaked by the shell only)", w => w with { IsCloaked = true, CloakedFlags = 2, IsCloakedByShell = true }),
        A("shell-cloaked via the boolean only", w => w with { IsCloaked = true, IsCloakedByShell = true }),
        A("minimised with degenerate bounds", w => w with { IsMinimized = true, Bounds = default }),
        A("minimised", w => w with { IsMinimized = true }),
        A("maximised", w => w with { IsMaximized = true }),
        A("elevated (visible, not controllable)", w => w with { IsElevated = true }),
        A("tool window with WS_EX_APPWINDOW", w => w with { IsToolWindow = true, IsAppWindow = true }),
        A("owned window with WS_EX_APPWINDOW", w => w with { OwnerHandle = 5, IsAppWindow = true }),
        A("no-activate window with WS_EX_APPWINDOW", w => w with { IsNoActivate = true, IsAppWindow = true }),
        A("empty title with WS_EX_APPWINDOW", w => w with { Title = "", IsAppWindow = true }),
        A("console window without a title", w => w with { Title = "", ClassName = "ConsoleWindowClass" }),
        A("Windows Terminal without a title", w => w with { Title = "", ClassName = "CASCADIA_HOSTING_WINDOW_CLASS" }),
        A("UWP frame", w => w with { ClassName = "ApplicationFrameWindow", Aumid = "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App" }),
        A("explorer folder window", w => w with { ClassName = "CabinetWClass", ExePath = @"C:\Windows\explorer.exe" }),
    };

    [Theory]
    [MemberData(nameof(Accepts))]
    public void Accepts_(string why, Func<RawWindow, RawWindow> tweak)
    {
        _ = why;
        WindowFilter.Evaluate(Ok(tweak)).Should().Be(WindowRejection.None);
        WindowFilter.IsEligible(Ok(tweak)).Should().BeTrue();
    }

    [Fact]
    public void The_first_matching_rule_names_the_reason_when_several_apply()
    {
        // own process beats everything, shell class beats visibility, visibility beats cloaking, and so on
        WindowFilter.Evaluate(Ok(w => w with { IsOwnProcess = true, ClassName = "Progman", IsVisible = false })).Should().Be(WindowRejection.OwnProcess);
        WindowFilter.Evaluate(Ok(w => w with { ClassName = "Progman", IsVisible = false })).Should().Be(WindowRejection.ShellClass);
        WindowFilter.Evaluate(Ok(w => w with { IsVisible = false, IsCloaked = true, CloakedFlags = 1 })).Should().Be(WindowRejection.NotVisible);
        WindowFilter.Evaluate(Ok(w => w with { IsCloaked = true, CloakedFlags = 1, Bounds = default })).Should().Be(WindowRejection.Cloaked);
        WindowFilter.Evaluate(Ok(w => w with { OwnerHandle = 5, IsToolWindow = true, Title = "" })).Should().Be(WindowRejection.Owned);
        WindowFilter.Evaluate(Ok(w => w with { IsToolWindow = true, IsNoActivate = true, Title = "" })).Should().Be(WindowRejection.ToolWindow);
    }

    [Fact]
    public void An_owned_window_is_eligible_when_the_owner_is_not_visible()
    {
        var owned = Ok(w => w with { OwnerHandle = 5 });
        WindowFilter.IsEligible(owned, h => h != 5).Should().BeTrue("the owner is a hidden helper window (common in .NET / Java / Delphi apps)");
        WindowFilter.IsEligible(owned, h => h == 5).Should().BeFalse();
        WindowFilter.IsEligible(owned, null).Should().BeFalse("without information the owner is assumed visible");
    }

    [Fact]
    public void EffectiveCloakFlags_prefers_the_raw_mask_and_falls_back_to_the_booleans()
    {
        WindowFilter.EffectiveCloakFlags(Ok(w => w with { CloakedFlags = 4, IsCloaked = true })).Should().Be(4u);
        WindowFilter.EffectiveCloakFlags(Ok(w => w with { IsCloaked = true, IsCloakedByShell = true })).Should().Be(2u);
        WindowFilter.EffectiveCloakFlags(Ok(w => w with { IsCloaked = true })).Should().Be(1u);
        WindowFilter.EffectiveCloakFlags(Ok()).Should().Be(0u);
    }

    [Fact]
    public void IsOnOtherDesktop_is_true_only_for_shell_only_cloaking()
    {
        WindowFilter.IsOnOtherDesktop(Ok(w => w with { CloakedFlags = 2 })).Should().BeTrue();
        WindowFilter.IsOnOtherDesktop(Ok(w => w with { CloakedFlags = 3 })).Should().BeFalse();
        WindowFilter.IsOnOtherDesktop(Ok()).Should().BeFalse();
    }

    [Theory]
    [InlineData("Progman", true)]
    [InlineData("Windows.UI.Core.CoreWindow", true)]
    [InlineData("ApplicationFrameWindow", false)]
    [InlineData("Notepad", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void IsShellClass_table(string? cls, bool expected) => WindowFilter.IsShellClass(cls).Should().Be(expected);
}
