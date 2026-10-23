using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>
/// Real AppBar tests: the shell must reserve and release work area. Each test restores the work area (Undock/Dispose) and verifies it.
/// Skipped when there is no display or the shell refuses appbars (Explorer not running).
/// </summary>
[Collection(ShellCollection.Name)]
public class AppBarIntegrationTests
{
    private static MonitorInfo Primary() => new Win32MonitorProvider().GetMonitors().First(m => m.Primary);

    private static NativeRect Bounds(MonitorInfo m) => RectMath.FromProtocol(m.BoundsPx);

    private static void RequireDesktop()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.If(new Win32MonitorProvider().GetMonitors().Count == 0, "no display attached to this session");
        Skip.If(TestNative.FindWindow("Shell_TrayWnd", null) == 0, "Explorer's taskbar is not running: the shell does not manage appbars");
    }

    private static bool WorkAreaIs(Rect expected) => Primary().WorkAreaPx == expected;

    [SkippableFact]
    public void Dock_reserves_work_area_and_Undock_gives_it_back_for_every_edge()
    {
        RequireDesktop();
        var before = Primary();
        foreach (var edge in new[] { AppBarEdge.Top, AppBarEdge.Bottom, AppBarEdge.Left, AppBarEdge.Right })
        {
            using var tw = TestWindow.Create("BW appbar " + edge);
            Skip.If(tw.Handle == 0, "could not create a test window");
            using var bar = new AppBarFactory().Create(tw.Handle);

            var rect = bar.Dock(edge, Bounds(before), 40);
            Skip.IfNot(bar.IsRegistered, "the shell refused the appbar registration");

            // The returned rectangle lies on the requested edge of the monitor and has the requested thickness.
            var b = Bounds(before);
            switch (edge)
            {
                case AppBarEdge.Top: rect.Height.Should().Be(40); rect.Top.Should().BeGreaterThanOrEqualTo(b.Top); break;
                case AppBarEdge.Bottom: rect.Height.Should().Be(40); rect.Bottom.Should().BeLessThanOrEqualTo(b.Bottom); break;
                case AppBarEdge.Left: rect.Width.Should().Be(40); rect.Left.Should().BeGreaterThanOrEqualTo(b.Left); break;
                case AppBarEdge.Right: rect.Width.Should().Be(40); rect.Right.Should().BeLessThanOrEqualTo(b.Right); break;
            }

            Poll.Until(() => !WorkAreaIs(before.WorkAreaPx), 5000).Should().BeTrue($"{edge}: the work area must shrink after ABM_SETPOS");
            var during = Primary().WorkAreaPx;
            (during.Width * during.Height).Should().BeLessThan(before.WorkAreaPx.Width * before.WorkAreaPx.Height);

            bar.Undock();
            bar.IsRegistered.Should().BeFalse();
            Poll.Until(() => WorkAreaIs(before.WorkAreaPx), 5000).Should().BeTrue($"{edge}: the work area must be unchanged after Undock");
        }
    }

    [SkippableFact]
    public void Dispose_without_Undock_releases_the_reservation()
    {
        RequireDesktop();
        var before = Primary();
        using var tw = TestWindow.Create("BW appbar dispose");
        Skip.If(tw.Handle == 0, "could not create a test window");
        var bar = new AppBarFactory().Create(tw.Handle);
        bar.Dock(AppBarEdge.Top, Bounds(before), 28);
        Skip.IfNot(bar.IsRegistered, "the shell refused the appbar registration");
        Poll.Until(() => !WorkAreaIs(before.WorkAreaPx), 5000).Should().BeTrue();

        bar.Dispose();

        Poll.Until(() => WorkAreaIs(before.WorkAreaPx), 5000).Should().BeTrue();
    }

    [SkippableFact]
    public void Two_appbars_on_different_edges_stack_and_release_independently()
    {
        RequireDesktop();
        var before = Primary();
        using var twA = TestWindow.Create("BW appbar A");
        using var twB = TestWindow.Create("BW appbar B");
        Skip.If(twA.Handle == 0 || twB.Handle == 0, "could not create test windows");
        using var top = new AppBarFactory().Create(twA.Handle);
        using var bottom = new AppBarFactory().Create(twB.Handle);

        top.Dock(AppBarEdge.Top, Bounds(before), 28);
        bottom.Dock(AppBarEdge.Bottom, Bounds(before), 60);
        Skip.IfNot(top.IsRegistered && bottom.IsRegistered, "the shell refused the appbar registration");

        Poll.Until(() => Primary().WorkAreaPx.Height <= before.WorkAreaPx.Height - 28 - 60 + 1, 5000).Should().BeTrue();
        top.Undock();
        Poll.Until(() => Primary().WorkAreaPx.Height > before.WorkAreaPx.Height - 28 - 60 + 1, 5000).Should().BeTrue();
        bottom.Undock();
        Poll.Until(() => WorkAreaIs(before.WorkAreaPx), 5000).Should().BeTrue();
    }

    [SkippableFact]
    public void Docking_the_same_band_twice_is_stable()
    {
        RequireDesktop();
        var before = Primary();
        using var tw = TestWindow.Create("BW appbar stable");
        Skip.If(tw.Handle == 0, "could not create a test window");
        using var bar = new AppBarFactory().Create(tw.Handle);

        var first = bar.Dock(AppBarEdge.Top, Bounds(before), 28);
        Skip.IfNot(bar.IsRegistered, "the shell refused the appbar registration");
        var second = bar.Dock(AppBarEdge.Top, Bounds(before), 28);
        var thicker = bar.Dock(AppBarEdge.Top, Bounds(before), 50);

        second.Should().Be(first, "ABM_QUERYPOS ignores our own previous reservation");
        thicker.Height.Should().Be(50);
        bar.Undock();
        Poll.Until(() => WorkAreaIs(before.WorkAreaPx), 5000).Should().BeTrue();
    }

    [SkippableFact]
    public void The_registered_callback_message_is_consumed_and_mapped()
    {
        RequireDesktop();
        var before = Primary();
        using var tw = TestWindow.Create("BW appbar callback");
        Skip.If(tw.Handle == 0, "could not create a test window");
        using var bar = new AppBarFactory().Create(tw.Handle);
        bar.Dock(AppBarEdge.Top, Bounds(before), 28);
        Skip.IfNot(bar.IsRegistered, "the shell refused the appbar registration");
        var seen = new List<AppBarNotification>();
        bar.Notification += seen.Add;
        uint callback = PInvoke.RegisterWindowMessage($"BetterWindows.AppBar.{tw.Handle:X}");

        bar.ProcessWindowMessage(callback, 1, 0).Should().BeTrue();
        bar.ProcessWindowMessage(callback, 2, 1).Should().BeTrue();
        bar.ProcessWindowMessage(callback, 2, 0).Should().BeTrue();
        bar.ProcessWindowMessage(0x0200 /* WM_MOUSEMOVE */, 0, 0).Should().BeFalse();

        seen.Should().Equal(AppBarNotification.PosChanged, AppBarNotification.FullScreenAppEntered, AppBarNotification.FullScreenAppLeft);
        bar.Undock();
    }

    [SkippableFact]
    public void Registering_the_same_window_through_two_AppBar_objects_recovers_instead_of_failing()
    {
        RequireDesktop();
        var before = Primary();
        using var tw = TestWindow.Create("BW appbar twice");
        Skip.If(tw.Handle == 0, "could not create a test window");
        var first = new AppBarFactory().Create(tw.Handle);
        first.Dock(AppBarEdge.Top, Bounds(before), 28);
        Skip.IfNot(first.IsRegistered, "the shell refused the appbar registration");

        using var second = new AppBarFactory().Create(tw.Handle); // e.g. the Host re-created its object after an error
        second.Dock(AppBarEdge.Top, Bounds(before), 28);

        second.IsRegistered.Should().BeTrue("ABM_NEW is retried after ABM_REMOVE when the window is already registered");
        second.Undock();
        first.Dispose();
        Poll.Until(() => WorkAreaIs(before.WorkAreaPx), 5000).Should().BeTrue();
    }
}

/// <summary>
/// Real taskbar tests against Explorer's Shell_TrayWnd with a TEMPORARY snapshot file (never the real marker). Every test restores the
/// original state in a finally block. Skipped when the runner has no taskbar.
/// </summary>
[Collection(ShellCollection.Name)]
public class TaskbarIntegrationTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-taskbar-it-" + Guid.NewGuid().ToString("N"));
    private readonly Win32TaskbarSurface _surface = new();

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    private FileTaskbarSnapshotStore NewStore() => new(Path.Combine(_dir, "taskbar.json"));

    private void RequireTaskbar()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        Skip.If(_surface.GetWindows().Count == 0, "no Shell_TrayWnd on this machine");
        Skip.IfNot(_surface.TryGetState(out _), "taskbar state not readable");
    }

    private (IReadOnlyList<TaskbarWindowInfo> Windows, uint State) Capture()
    {
        _surface.TryGetState(out uint state);
        return (_surface.GetWindows(), state);
    }

    private bool AllVisibleLike(IReadOnlyList<TaskbarWindowInfo> expected) =>
        expected.All(e => _surface.GetWindows().Any(w => w.Handle == e.Handle && w.IsVisible == e.IsVisible));

    [SkippableFact]
    public void Hidden_then_Visible_leaves_the_taskbar_exactly_as_it_was()
    {
        RequireTaskbar();
        var (windows, state) = Capture();
        var store = NewStore();
        var controller = new Win32TaskbarController(NullLogger.Instance, _surface, store);
        try
        {
            controller.Apply(TaskbarMode.Hidden);

            store.Exists.Should().BeTrue("the snapshot is persisted before the first mutation");
            Poll.Until(() => _surface.GetWindows().All(w => !w.IsVisible), 5000).Should().BeTrue("every taskbar window is hidden");
            _surface.TryGetState(out uint hiddenState).Should().BeTrue();
            (hiddenState & 1).Should().Be(1, "ABS_AUTOHIDE is set so the shell stops reserving its band");
            (hiddenState & 2).Should().Be(state & 2, "always-on-top is kept as it was");
            controller.Current.Should().Be(TaskbarMode.Hidden);

            controller.Apply(TaskbarMode.Visible);

            Poll.Until(() => AllVisibleLike(windows), 5000).Should().BeTrue("visibility restored per taskbar");
            _surface.TryGetState(out uint restored).Should().BeTrue();
            restored.Should().Be(state);
            store.Exists.Should().BeFalse();
            controller.Current.Should().Be(TaskbarMode.Visible);
        }
        finally
        {
            controller.Restore();
            controller.Dispose();
            TaskbarRestorer.RestoreNow(_surface, store, NullLogger.Instance);
        }
    }

    [SkippableFact]
    public void The_watcher_hides_the_taskbar_again_when_the_shell_shows_it()
    {
        RequireTaskbar();
        var (windows, state) = Capture();
        Skip.IfNot(windows.All(w => w.IsVisible), "the taskbar is not visible to begin with");
        var store = NewStore();
        var controller = new Win32TaskbarController(NullLogger.Instance, _surface, store);
        try
        {
            controller.Apply(TaskbarMode.Hidden);
            Skip.IfNot(Poll.Until(() => _surface.GetWindows().All(w => !w.IsVisible), 5000), "could not hide the taskbar on this machine");
            var primary = _surface.GetWindows().First(w => w.IsPrimary);

            _surface.SetVisible(primary.Handle, true); // what Explorer does when the Win key is pressed

            Poll.Until(() => !_surface.GetWindows().First(w => w.IsPrimary).IsVisible, 5000)
                .Should().BeTrue("EVENT_OBJECT_SHOW on the tray window schedules a re-hide");
        }
        finally
        {
            controller.Restore();
            controller.Dispose();
            TaskbarRestorer.RestoreNow(_surface, store, NullLogger.Instance);
            _surface.TrySetState(state);
        }
    }

    [SkippableFact]
    public void AutoHide_mode_sets_only_the_auto_hide_flag_and_keeps_the_windows_visible()
    {
        RequireTaskbar();
        var (windows, state) = Capture();
        var store = NewStore();
        var controller = new Win32TaskbarController(NullLogger.Instance, _surface, store);
        try
        {
            controller.Apply(TaskbarMode.AutoHide);

            _surface.TryGetState(out uint now).Should().BeTrue();
            (now & 1).Should().Be(1);
            AllVisibleLike(windows).Should().BeTrue("auto-hide leaves the windows alone");

            controller.Apply(TaskbarMode.Visible);
            _surface.TryGetState(out uint back).Should().BeTrue();
            back.Should().Be(state);
        }
        finally
        {
            controller.Restore();
            controller.Dispose();
            TaskbarRestorer.RestoreNow(_surface, store, NullLogger.Instance);
        }
    }

    [SkippableFact]
    public void A_fresh_process_can_restore_from_the_marker_after_a_crash()
    {
        RequireTaskbar();
        var (windows, state) = Capture();
        Skip.IfNot(windows.All(w => w.IsVisible), "the taskbar is not visible to begin with");
        var store = NewStore();
        var controller = new Win32TaskbarController(NullLogger.Instance, _surface, store);
        try
        {
            controller.Apply(TaskbarMode.Hidden);
            Skip.IfNot(Poll.Until(() => _surface.GetWindows().All(w => !w.IsVisible), 5000), "could not hide the taskbar on this machine");
            store.Exists.Should().BeTrue();

            // "Crash": no Restore() on the controller; a brand-new restorer (as the Host does at the next start) undoes everything.
            TaskbarRestorer.RestoreNow(_surface, store, NullLogger.Instance);

            Poll.Until(() => AllVisibleLike(windows), 5000).Should().BeTrue();
            _surface.TryGetState(out uint restored).Should().BeTrue();
            restored.Should().Be(state);
            store.Exists.Should().BeFalse();

            // Idempotent.
            TaskbarRestorer.RestoreNow(_surface, store, NullLogger.Instance);
            TaskbarRestorer.RestoreNow(_surface, store, NullLogger.Instance);
            AllVisibleLike(windows).Should().BeTrue();
        }
        finally
        {
            controller.Restore();
            controller.Dispose();
            TaskbarRestorer.RestoreNow(_surface, store, NullLogger.Instance);
        }
    }

    [SkippableFact]
    public void Without_a_marker_a_taskbar_hidden_by_someone_is_made_visible_but_a_visible_one_is_untouched()
    {
        RequireTaskbar();
        var (windows, state) = Capture();
        Skip.IfNot(windows.All(w => w.IsVisible), "the taskbar is not visible to begin with");
        var emptyStore = NewStore();

        // Healthy taskbar: nothing changes.
        TaskbarRestorer.RestoreNow(_surface, emptyStore, NullLogger.Instance);
        AllVisibleLike(windows).Should().BeTrue();
        _surface.TryGetState(out uint untouched).Should().BeTrue();
        untouched.Should().Be(state);

        try
        {
            foreach (var w in windows)
            {
                _surface.SetVisible(w.Handle, false);
            }

            Skip.IfNot(Poll.Until(() => _surface.GetWindows().All(w => !w.IsVisible), 5000), "could not hide the taskbar on this machine");

            TaskbarRestorer.RestoreNow(_surface, emptyStore, NullLogger.Instance);

            Poll.Until(() => AllVisibleLike(windows), 5000).Should().BeTrue();
        }
        finally
        {
            foreach (var w in windows)
            {
                _surface.SetVisible(w.Handle, w.IsVisible);
            }

            _surface.TrySetState(state);
        }
    }

    [SkippableFact]
    public void The_default_RestoreNow_never_throws_and_is_idempotent()
    {
        RequireTaskbar();
        // The default marker belongs to a possibly running BetterWindows: never run the real restore over it on a developer machine.
        Skip.If(File.Exists(FileTaskbarSnapshotStore.DefaultPath()), "a real BetterWindows taskbar marker exists on this machine");
        var (windows, state) = Capture();
        Skip.IfNot(windows.All(w => w.IsVisible), "the taskbar is not visible to begin with");

        TaskbarRestorer.RestoreNow();
        TaskbarRestorer.RestoreNow();

        AllVisibleLike(windows).Should().BeTrue();
        _surface.TryGetState(out uint now).Should().BeTrue();
        now.Should().Be(state);
    }
}
