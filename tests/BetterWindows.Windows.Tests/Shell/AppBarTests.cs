using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>
/// A fake shell implementing the AppBar protocol the way Explorer does: ABM_QUERYPOS subtracts the bands of OTHER appbars on the same edge
/// (and, for left/right bars, the top/bottom bands), ABM_NEW refuses a window that is already registered.
/// </summary>
internal sealed class FakeAppBarShell : IAppBarApi
{
    public readonly List<string> Calls = new();
    public readonly Dictionary<nint, (AppBarEdge Edge, NativeRect Rect)> Bars = new();
    private readonly HashSet<nint> _registered = new();
    public NativeRect Monitor = new(0, 0, 1920, 1080);
    public bool RefuseNew;
    public bool ReturnZeroMessage;
    public Queue<NativeRect> SetPosOverrides = new();

    public uint RegisterCallbackMessage(string name)
    {
        Calls.Add("msg:" + name);
        return ReturnZeroMessage ? 0u : 0xC123u;
    }

    public bool New(nint hwnd, uint callbackMessage)
    {
        Calls.Add("new");
        if (RefuseNew || !_registered.Add(hwnd))
        {
            return false;
        }

        return true;
    }

    public void Remove(nint hwnd)
    {
        Calls.Add("remove");
        _registered.Remove(hwnd);
        Bars.Remove(hwnd);
    }

    public NativeRect QueryPos(nint hwnd, AppBarEdge edge, NativeRect proposed)
    {
        Calls.Add("query");
        var r = proposed;
        foreach (var (other, bar) in Bars)
        {
            if (other == hwnd)
            {
                continue;
            }

            switch (edge)
            {
                case AppBarEdge.Top when bar.Edge == AppBarEdge.Top: r = r with { Top = Math.Max(r.Top, bar.Rect.Bottom) }; break;
                case AppBarEdge.Bottom when bar.Edge == AppBarEdge.Bottom: r = r with { Bottom = Math.Min(r.Bottom, bar.Rect.Top) }; break;
                case AppBarEdge.Left when bar.Edge == AppBarEdge.Left: r = r with { Left = Math.Max(r.Left, bar.Rect.Right) }; break;
                case AppBarEdge.Right when bar.Edge == AppBarEdge.Right: r = r with { Right = Math.Min(r.Right, bar.Rect.Left) }; break;
                case AppBarEdge.Left or AppBarEdge.Right when bar.Edge == AppBarEdge.Top: r = r with { Top = Math.Max(r.Top, bar.Rect.Bottom) }; break;
                case AppBarEdge.Left or AppBarEdge.Right when bar.Edge == AppBarEdge.Bottom: r = r with { Bottom = Math.Min(r.Bottom, bar.Rect.Top) }; break;
            }
        }

        return r;
    }

    public NativeRect SetPos(nint hwnd, AppBarEdge edge, NativeRect proposed)
    {
        Calls.Add("setpos");
        var r = SetPosOverrides.Count > 0 ? SetPosOverrides.Dequeue() : proposed;
        Bars[hwnd] = (edge, r);
        return r;
    }

    public void WindowPosChanged(nint hwnd) => Calls.Add("windowposchanged");

    public void Activate(nint hwnd) => Calls.Add("activate");
}

public class AppBarTests
{
    private const nint Hwnd = 0x1001;
    private static readonly NativeRect Monitor = new(0, 0, 1920, 1080);

    private static AppBar Create(FakeAppBarShell shell, nint hwnd = Hwnd) => new(hwnd, shell, NullLogger.Instance);

    [Fact]
    public void Dock_registers_then_queries_then_sets_and_returns_the_band_on_the_edge()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);

        var rect = bar.Dock(AppBarEdge.Top, Monitor, 28);

        rect.Should().Be(new NativeRect(0, 0, 1920, 28));
        bar.IsRegistered.Should().BeTrue();
        shell.Calls.Where(c => !c.StartsWith("msg:")).Should().Equal("new", "query", "setpos");
        shell.Calls.Should().Contain("msg:BetterWindows.AppBar.1001");
    }

    [Theory]
    [InlineData(AppBarEdge.Left, 0, 0, 60, 1080)]
    [InlineData(AppBarEdge.Right, 1860, 0, 1920, 1080)]
    [InlineData(AppBarEdge.Top, 0, 0, 1920, 60)]
    [InlineData(AppBarEdge.Bottom, 0, 1020, 1920, 1080)]
    public void Every_edge_gets_the_requested_thickness(AppBarEdge edge, int l, int t, int r, int b)
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(edge, Monitor, 60).Should().Be(new NativeRect(l, t, r, b));
    }

    [Fact]
    public void Existing_appbar_on_the_same_edge_pushes_the_new_band_inwards_keeping_its_thickness()
    {
        var shell = new FakeAppBarShell();
        using var menubar = Create(shell, 0x2002);
        menubar.Dock(AppBarEdge.Top, Monitor, 28);
        using var other = Create(shell, 0x3003);

        var rect = other.Dock(AppBarEdge.Top, Monitor, 40);

        rect.Should().Be(new NativeRect(0, 28, 1920, 68)); // starts below the 28 px menu bar, still 40 px thick
    }

    [Fact]
    public void Side_bar_stays_between_the_top_and_bottom_bars()
    {
        var shell = new FakeAppBarShell();
        using var top = Create(shell, 0x2002);
        using var bottom = Create(shell, 0x3003);
        using var left = Create(shell, 0x4004);
        top.Dock(AppBarEdge.Top, Monitor, 28);
        bottom.Dock(AppBarEdge.Bottom, Monitor, 80);

        left.Dock(AppBarEdge.Left, Monitor, 50).Should().Be(new NativeRect(0, 28, 50, 1000));
    }

    [Fact]
    public void Work_area_that_already_excludes_other_bars_is_respected_on_a_second_monitor()
    {
        var shell = new FakeAppBarShell();
        var second = new NativeRect(1920, 0, 3840, 1080);
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Bottom, second, 90).Should().Be(new NativeRect(1920, 990, 3840, 1080));
    }

    [Fact]
    public void Docking_again_updates_in_place_without_a_second_ABM_NEW()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        var first = bar.Dock(AppBarEdge.Bottom, Monitor, 80);
        var again = bar.Dock(AppBarEdge.Bottom, Monitor, 80);
        var thicker = bar.Dock(AppBarEdge.Bottom, Monitor, 120);

        again.Should().Be(first);
        thicker.Should().Be(new NativeRect(0, 960, 1920, 1080));
        shell.Calls.Count(c => c == "new").Should().Be(1);
    }

    [Fact]
    public void Moving_to_another_edge_re_commits_with_the_new_edge()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Bottom, Monitor, 80);
        bar.Dock(AppBarEdge.Left, Monitor, 80).Should().Be(new NativeRect(0, 0, 80, 1080));
        shell.Bars[Hwnd].Edge.Should().Be(AppBarEdge.Left);
    }

    [Fact]
    public void When_the_shell_changes_the_rectangle_on_SETPOS_dock_goes_around_again()
    {
        var shell = new FakeAppBarShell();
        shell.SetPosOverrides.Enqueue(new NativeRect(0, 10, 1920, 38)); // shell moved us 10 px down
        using var bar = Create(shell);

        var rect = bar.Dock(AppBarEdge.Top, Monitor, 28);

        // Second pass starts from the shell's answer and is stable: the shell's position wins.
        shell.Calls.Count(c => c == "setpos").Should().Be(2);
        rect.Should().Be(new NativeRect(0, 10, 1920, 38));
    }

    [Fact]
    public void Dock_gives_up_after_three_passes_and_returns_the_last_answer()
    {
        var shell = new FakeAppBarShell();
        for (int i = 0; i < 5; i++)
        {
            shell.SetPosOverrides.Enqueue(new NativeRect(0, i + 1, 1920, i + 29));
        }

        using var bar = Create(shell);
        var rect = bar.Dock(AppBarEdge.Top, Monitor, 28);

        shell.Calls.Count(c => c == "setpos").Should().Be(3);
        rect.Should().Be(new NativeRect(0, 3, 1920, 31));
    }

    [Fact]
    public void Thickness_is_clamped_to_the_monitor_and_to_at_least_one_pixel()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 0).Height.Should().Be(1);
        bar.Dock(AppBarEdge.Top, Monitor, 100_000).Height.Should().Be(1080);
        bar.Dock(AppBarEdge.Left, Monitor, -5).Width.Should().Be(1);
    }

    [Fact]
    public void A_leaked_registration_is_cleared_and_ABM_NEW_retried()
    {
        var shell = new FakeAppBarShell();
        shell.New(Hwnd, 1); // someone (a previous AppBar object) already registered this window
        using var bar = Create(shell);

        bar.Dock(AppBarEdge.Top, Monitor, 28);

        bar.IsRegistered.Should().BeTrue();
        shell.Calls.Should().ContainInOrder("new", "new", "remove", "new");
    }

    [Fact]
    public void When_the_shell_refuses_the_appbar_dock_degrades_to_the_proposed_rectangle_without_throwing()
    {
        var shell = new FakeAppBarShell { RefuseNew = true };
        using var bar = Create(shell);

        var rect = bar.Dock(AppBarEdge.Top, Monitor, 28);

        rect.Should().Be(new NativeRect(0, 0, 1920, 28));
        bar.IsRegistered.Should().BeFalse();
        shell.Calls.Should().NotContain("setpos");
    }

    [Fact]
    public void When_the_callback_message_cannot_be_registered_dock_degrades()
    {
        var shell = new FakeAppBarShell { ReturnZeroMessage = true };
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28).Should().Be(new NativeRect(0, 0, 1920, 28));
        bar.IsRegistered.Should().BeFalse();
        shell.Calls.Should().NotContain("new");
    }

    [Fact]
    public void Undock_removes_the_registration_and_Dock_can_register_again()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28);

        bar.Undock();

        bar.IsRegistered.Should().BeFalse();
        shell.Bars.Should().BeEmpty();
        bar.Undock(); // idempotent
        shell.Calls.Count(c => c == "remove").Should().Be(1);

        bar.Dock(AppBarEdge.Top, Monitor, 28);
        bar.IsRegistered.Should().BeTrue();
        shell.Calls.Count(c => c == "new").Should().Be(2);
    }

    [Fact]
    public void Dispose_removes_the_registration_once_and_blocks_further_docking()
    {
        var shell = new FakeAppBarShell();
        var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28);

        bar.Dispose();
        bar.Dispose();

        shell.Calls.Count(c => c == "remove").Should().Be(1);
        var act = () => bar.Dock(AppBarEdge.Top, Monitor, 28);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Callback_notifications_are_mapped_and_consumed()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28);
        var seen = new List<AppBarNotification>();
        bar.Notification += seen.Add;

        bar.ProcessWindowMessage(0xC123, 1, 0).Should().BeTrue();   // ABN_POSCHANGED
        bar.ProcessWindowMessage(0xC123, 2, 1).Should().BeTrue();   // ABN_FULLSCREENAPP entered
        bar.ProcessWindowMessage(0xC123, 2, 0).Should().BeTrue();   // ABN_FULLSCREENAPP left
        bar.ProcessWindowMessage(0xC123, 3, 1).Should().BeTrue();   // ABN_WINDOWARRANGE
        bar.ProcessWindowMessage(0xC123, 0, 0).Should().BeTrue();   // ABN_STATECHANGE: consumed, no event
        bar.ProcessWindowMessage(0xC123, 99, 0).Should().BeTrue();  // unknown: consumed

        seen.Should().Equal(
            AppBarNotification.PosChanged,
            AppBarNotification.FullScreenAppEntered,
            AppBarNotification.FullScreenAppLeft,
            AppBarNotification.WindowArrange);
    }

    [Fact]
    public void Other_messages_are_not_consumed_but_drive_ABM_ACTIVATE_and_ABM_WINDOWPOSCHANGED()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28);
        shell.Calls.Clear();

        bar.ProcessWindowMessage(0x0006, 1, 0).Should().BeFalse();   // WM_ACTIVATE
        bar.ProcessWindowMessage(0x0047, 0, 0).Should().BeFalse();   // WM_WINDOWPOSCHANGED
        bar.ProcessWindowMessage(0x0200, 0, 0).Should().BeFalse();   // WM_MOUSEMOVE

        shell.Calls.Should().Equal("activate", "windowposchanged");
    }

    [Theory]
    [InlineData(0x007Eu)] // WM_DISPLAYCHANGE
    [InlineData(0x02E0u)] // WM_DPICHANGED
    public void Display_and_dpi_changes_ask_the_host_to_dock_again(uint msg)
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28);
        var seen = new List<AppBarNotification>();
        bar.Notification += seen.Add;

        bar.ProcessWindowMessage(msg, 0, 0).Should().BeFalse();

        seen.Should().Equal(AppBarNotification.PosChanged);
    }

    [Fact]
    public void Messages_are_ignored_while_not_registered()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        var seen = new List<AppBarNotification>();
        bar.Notification += seen.Add;

        bar.ProcessWindowMessage(0x007E, 0, 0).Should().BeFalse();
        bar.ProcessWindowMessage(0xC123, 1, 0).Should().BeFalse();

        seen.Should().BeEmpty();
        shell.Calls.Should().BeEmpty();
    }

    [Fact]
    public void A_throwing_subscriber_neither_escapes_into_the_wndproc_nor_starves_the_others()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28);
        var seen = new List<AppBarNotification>();
        bar.Notification += _ => throw new InvalidOperationException("boom");
        bar.Notification += seen.Add;

        var act = () => bar.ProcessWindowMessage(0xC123, 1, 0);

        act.Should().NotThrow();
        seen.Should().Equal(AppBarNotification.PosChanged);
    }

    [Fact]
    public void Re_dock_from_inside_a_notification_is_safe()
    {
        var shell = new FakeAppBarShell();
        using var bar = Create(shell);
        bar.Dock(AppBarEdge.Top, Monitor, 28);
        NativeRect redocked = default;
        bar.Notification += n =>
        {
            if (n == AppBarNotification.PosChanged)
            {
                redocked = bar.Dock(AppBarEdge.Top, Monitor, 28);
            }
        };

        bar.ProcessWindowMessage(0xC123, 1, 0);

        redocked.Should().Be(new NativeRect(0, 0, 1920, 28));
    }
}

public class AppBarMathTests
{
    private static readonly NativeRect M = new(100, 50, 2020, 1130);

    [Fact]
    public void Propose_covers_the_whole_edge()
    {
        AppBarMath.Propose(M, AppBarEdge.Left, 30).Should().Be(new NativeRect(100, 50, 130, 1130));
        AppBarMath.Propose(M, AppBarEdge.Right, 30).Should().Be(new NativeRect(1990, 50, 2020, 1130));
        AppBarMath.Propose(M, AppBarEdge.Top, 30).Should().Be(new NativeRect(100, 50, 2020, 80));
        AppBarMath.Propose(M, AppBarEdge.Bottom, 30).Should().Be(new NativeRect(100, 1100, 2020, 1130));
    }

    [Fact]
    public void AdjustToThickness_keeps_the_moved_edge_and_restores_the_depth()
    {
        AppBarMath.AdjustToThickness(new NativeRect(0, 28, 1920, 30), AppBarEdge.Top, 40).Should().Be(new NativeRect(0, 28, 1920, 68));
        AppBarMath.AdjustToThickness(new NativeRect(0, 900, 1920, 1000), AppBarEdge.Bottom, 40).Should().Be(new NativeRect(0, 960, 1920, 1000));
        AppBarMath.AdjustToThickness(new NativeRect(60, 0, 80, 1080), AppBarEdge.Left, 50).Should().Be(new NativeRect(60, 0, 110, 1080));
        AppBarMath.AdjustToThickness(new NativeRect(1800, 0, 1860, 1080), AppBarEdge.Right, 50).Should().Be(new NativeRect(1810, 0, 1860, 1080));
    }

    [Theory]
    [InlineData(AppBarEdge.Left, 0u)]
    [InlineData(AppBarEdge.Top, 1u)]
    [InlineData(AppBarEdge.Right, 2u)]
    [InlineData(AppBarEdge.Bottom, 3u)]
    public void Edge_values_match_ABE(AppBarEdge edge, uint abe) => AppBarMath.ToAbe(edge).Should().Be(abe);

    [Fact]
    public void Thickness_clamp_uses_the_dimension_across_the_edge()
    {
        AppBarMath.ClampThickness(AppBarEdge.Left, M, 5000).Should().Be(1920);
        AppBarMath.ClampThickness(AppBarEdge.Top, M, 5000).Should().Be(1080);
        AppBarMath.ClampThickness(AppBarEdge.Top, M, 0).Should().Be(1);
    }

    [Fact]
    public void Notification_mapping()
    {
        AppBarMath.MapNotification(0, 0).Should().BeNull();
        AppBarMath.MapNotification(1, 0).Should().Be(AppBarNotification.PosChanged);
        AppBarMath.MapNotification(2, 1).Should().Be(AppBarNotification.FullScreenAppEntered);
        AppBarMath.MapNotification(2, 0).Should().Be(AppBarNotification.FullScreenAppLeft);
        AppBarMath.MapNotification(3, 0).Should().Be(AppBarNotification.WindowArrange);
        AppBarMath.MapNotification(7, 0).Should().BeNull();
    }
}
