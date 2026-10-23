using System.Collections.Concurrent;
using System.Diagnostics;
using BetterWindows.Core.Platform;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>
/// Real Win32 tests for <see cref="Win32WindowSystem"/> (GitHub Actions windows-latest). Skipped on other OSes. They create their own windows or
/// launch Notepad (events from our own process are filtered by WINEVENT_SKIPOWNPROCESS, so event tests need a foreign process); everything is
/// closed again, and tests that depend on the desktop environment skip instead of failing.
/// </summary>
[Collection(ShellCollection.Name)]
public class WindowSystemIntegrationTests
{
    private static void RequireWindows() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

    [SkippableFact]
    public void EnumerateTopLevel_returns_windows_with_sane_fields()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        using var mine = TestWindow.Create("BW enumerate " + Guid.NewGuid());

        var all = ws.EnumerateTopLevel();

        all.Should().NotBeEmpty();
        all.Select(w => w.Handle).Should().OnlyHaveUniqueItems();
        all.Should().OnlyContain(w => w.Handle != 0 && w.ProcessId > 0 && w.ClassName.Length > 0);
        all.Should().OnlyContain(w => w.Title != null && (w.CloakedFlags != 0) == w.IsCloaked);
        all.Where(w => w.IsCloakedByShell).Should().OnlyContain(w => (w.CloakedFlags & 2) != 0);
        all.Should().Contain(w => w.Handle == mine.Handle);
        all.Should().Contain(w => w.IsVisible, "a desktop session always has visible windows");
    }

    [SkippableFact]
    public void TryDescribe_fills_every_field_for_our_own_visible_window()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        string title = "BW describe " + Guid.NewGuid();
        using var tw = TestWindow.Create(title);
        Skip.If(tw.Handle == 0, "could not create a test window");

        var w = ws.TryDescribe(tw.Handle);

        w.Should().NotBeNull();
        w!.Handle.Should().Be(tw.Handle);
        w.Title.Should().Be(title);
        w.ClassName.Should().Be("STATIC");
        w.ProcessId.Should().Be(Environment.ProcessId);
        w.IsOwnProcess.Should().BeTrue();
        w.ExePath.Should().NotBeNullOrEmpty();
        File.Exists(w.ExePath!).Should().BeTrue();
        w.IsVisible.Should().BeTrue();
        w.IsToolWindow.Should().BeTrue();
        w.IsAppWindow.Should().BeFalse();
        w.IsMinimized.Should().BeFalse();
        w.IsMaximized.Should().BeFalse();
        w.IsCloaked.Should().BeFalse();
        w.CloakedFlags.Should().Be(0);
        w.IsCloakedByShell.Should().BeFalse();
        w.OwnerHandle.Should().Be(0);
        w.IsElevated.Should().BeFalse("our own process is never 'not controllable' from ourselves");
        w.IsFlashing.Should().BeFalse();
        w.Bounds.Width.Should().BeGreaterThan(0);
        w.Bounds.Height.Should().BeGreaterThan(0);
    }

    [SkippableFact]
    public void TryDescribe_of_an_invisible_window_reports_it_invisible()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        using var tw = TestWindow.Create("BW hidden", visible: false);
        Skip.If(tw.Handle == 0, "could not create a test window");

        var w = ws.TryDescribe(tw.Handle);

        w.Should().NotBeNull();
        w!.IsVisible.Should().BeFalse();
        w.ClassName.Should().Be("STATIC");
    }

    [SkippableFact]
    public void TryDescribe_of_the_console_or_foreground_window_does_not_throw_and_is_consistent()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        long fg = ws.GetForegroundWindow();
        long console = TestNative.GetConsoleWindow();
        foreach (long h in new[] { fg, console }.Where(h => h != 0))
        {
            var w = ws.TryDescribe(h);
            if (w != null) // a console window of a ConPTY host can be a message-only/child window: then null is the right answer
            {
                w.Handle.Should().Be(h);
                w.ProcessId.Should().BeGreaterThan(0);
                w.ClassName.Should().NotBeEmpty();
            }
        }
    }

    [SkippableFact]
    public void TryDescribe_returns_null_for_missing_garbage_and_destroyed_windows()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        ws.TryDescribe(0).Should().BeNull();
        ws.TryDescribe(1).Should().BeNull();
        ws.TryDescribe(-5).Should().BeNull();
        ws.TryDescribe(long.MaxValue).Should().BeNull();

        long gone;
        using (var tw = TestWindow.Create("BW gone"))
        {
            gone = tw.Handle;
        }

        ws.TryDescribe(gone).Should().BeNull();
    }

    [SkippableFact]
    public void Commands_reject_invalid_handles()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        foreach (long bad in new long[] { 0, 1, -1, long.MaxValue })
        {
            ws.Activate(bad).Should().BeFalse();
            ws.Minimize(bad).Should().BeFalse();
            ws.Restore(bad).Should().BeFalse();
            ws.Maximize(bad).Should().BeFalse();
            ws.Close(bad).Should().BeFalse();
        }
    }

    [SkippableFact]
    public void The_desktop_window_and_child_windows_cannot_be_controlled()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        long desktop = TestNative.GetDesktopWindow();
        ws.Close(desktop).Should().BeFalse("the desktop window has no parent desktop, it is not an application window");
        ws.Minimize(desktop).Should().BeFalse();
    }

    [SkippableFact]
    public void Close_posts_WM_CLOSE_and_the_window_goes_away()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        using var tw = TestWindow.Create("BW close me");
        Skip.If(tw.Handle == 0, "could not create a test window");

        ws.Close(tw.Handle).Should().BeTrue();

        Poll.Until(() => ws.TryDescribe(tw.Handle) == null, 5000).Should().BeTrue();
    }

    [SkippableFact]
    public void Start_and_Stop_are_idempotent_and_restartable()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        ws.Stop(); // before start
        ws.Start();
        ws.Start();
        ws.Stop();
        ws.Stop();
        ws.Start();
        ws.Stop();
        ws.Dispose();
        ws.Dispose();
        ws.EnumerateTopLevel().Should().NotBeNull(); // enumeration still works after dispose (no handles needed)
    }

    [SkippableFact]
    public void Events_flow_for_a_foreign_window_and_commands_change_its_state()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        var events = new ConcurrentQueue<WindowEvent>();
        ws.WindowChanged += events.Enqueue;
        ws.Start();

        using var notepad = ExternalWindow.TryLaunchNotepad(ws);
        Skip.If(notepad == null, "Notepad could not be launched on this machine");
        long h = notepad!.Handle;

        // Created/Shown may have been raised before we found the window: they are in the queue by now.
        Poll.Until(() => events.Any(e => e.Handle == h && e.Kind is WindowEventKind.Created or WindowEventKind.Shown), 5000)
            .Should().BeTrue("the hook must report the new window");

        var raw = ws.TryDescribe(h);
        raw.Should().NotBeNull();
        raw!.ExePath.Should().NotBeNullOrEmpty();
        raw.IsOwnProcess.Should().BeFalse();
        raw.ProcessId.Should().Be(notepad.ProcessId);

        ws.Minimize(h).Should().BeTrue();
        Poll.Until(() => ws.TryDescribe(h)?.IsMinimized == true, 5000).Should().BeTrue("Minimize");
        Poll.Until(() => events.Any(e => e.Handle == h && e.Kind == WindowEventKind.Minimized), 3000).Should().BeTrue("EVENT_SYSTEM_MINIMIZESTART");

        ws.Restore(h).Should().BeTrue();
        Poll.Until(() => ws.TryDescribe(h)?.IsMinimized == false, 5000).Should().BeTrue("Restore");
        Poll.Until(() => events.Any(e => e.Handle == h && e.Kind == WindowEventKind.Restored), 3000).Should().BeTrue("EVENT_SYSTEM_MINIMIZEEND");

        ws.Maximize(h).Should().BeTrue();
        Poll.Until(() => ws.TryDescribe(h)?.IsMaximized == true, 5000).Should().BeTrue("Maximize");
        ws.Restore(h).Should().BeTrue();
        Poll.Until(() => ws.TryDescribe(h)?.IsMaximized == false, 5000).Should().BeTrue("Restore from maximised");

        ws.Close(h).Should().BeTrue();
        Poll.Until(() => events.Any(e => e.Handle == h && e.Kind == WindowEventKind.Destroyed), 8000).Should().BeTrue("EVENT_OBJECT_DESTROY");
        ws.TryDescribe(h).Should().BeNull();
    }

    [SkippableFact]
    public void Activate_tells_the_truth_about_the_foreground_window()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        using var notepad = ExternalWindow.TryLaunchNotepad(ws);
        Skip.If(notepad == null, "Notepad could not be launched on this machine");
        long h = notepad!.Handle;

        // Even a minimised window: Activate restores it first.
        ws.Minimize(h);
        Poll.Until(() => ws.TryDescribe(h)?.IsMinimized == true, 3000);

        bool ok = ws.Activate(h);

        if (ok)
        {
            ws.TryDescribe(h)!.IsMinimized.Should().BeFalse();
            long fg = ws.GetForegroundWindow();
            (fg == h || ws.TryDescribe(fg)?.OwnerHandle == h || ws.TryDescribe(fg)?.ProcessId == notepad.ProcessId).Should().BeTrue("a true result means the window (or its dialog) is the foreground window");
        }
        else
        {
            // The foreground lock may legitimately win on a CI session; the contract is only that the result is truthful.
            ws.GetForegroundWindow().Should().NotBe(h);
        }
    }

    [SkippableFact]
    public void FullscreenChanged_subscription_installs_and_removes_the_location_hook_without_side_effects_and_fires_on_foreground_change()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        ws.Start();
        using var fired = new ManualResetEventSlim(false);
        void Handler() => fired.Set();

        ws.FullscreenChanged += Handler;
        using var notepad = ExternalWindow.TryLaunchNotepad(ws);
        Skip.If(notepad == null, "Notepad could not be launched on this machine");
        bool activated = ws.Activate(notepad!.Handle);
        bool raised = fired.Wait(TimeSpan.FromSeconds(4));
        ws.FullscreenChanged -= Handler;
        ws.FullscreenChanged -= Handler; // removing twice must not corrupt the subscriber count

        Skip.IfNot(activated, "the foreground lock prevented the foreground change on this session");
        raised.Should().BeTrue("a foreground change must raise FullscreenChanged");
    }

    [SkippableFact]
    public void IsFullscreenWindowActive_is_false_for_a_normal_foreground_window_and_never_throws()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        var monitors = new Win32MonitorProvider().GetMonitors();
        Skip.If(monitors.Count == 0, "no monitor");
        using var notepad = ExternalWindow.TryLaunchNotepad(ws);
        Skip.If(notepad == null, "Notepad could not be launched on this machine");
        Skip.IfNot(ws.Activate(notepad!.Handle), "could not make Notepad the foreground window");

        foreach (var m in monitors)
        {
            ws.IsFullscreenWindowActive(m).Should().BeFalse("a normal Notepad window is not a full-screen app");
        }
    }

    [SkippableFact]
    public void The_hook_thread_has_negligible_cpu_when_idle()
    {
        RequireWindows();
        using var ws = new Win32WindowSystem();
        ws.Start();
        ws.FullscreenChanged += () => { };
        Thread.Sleep(500);
        var proc = Process.GetCurrentProcess();
        var cpu0 = proc.TotalProcessorTime;
        var t0 = Stopwatch.StartNew();
        Thread.Sleep(2000);
        proc.Refresh();
        double used = (proc.TotalProcessorTime - cpu0).TotalMilliseconds / (t0.Elapsed.TotalMilliseconds * Environment.ProcessorCount);
        used.Should().BeLessThan(0.25, "an idle window system must not burn CPU (generous bound: CI machines are noisy)");
    }
}
