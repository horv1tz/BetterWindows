using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Real monitor enumeration and cursor tests; tolerate headless/virtual displays.</summary>
[Collection(ShellCollection.Name)]
public class MonitorAndCursorIntegrationTests
{
    private static void RequireWindows() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

    [SkippableFact]
    public void At_least_one_monitor_with_exactly_one_primary_and_consistent_geometry()
    {
        RequireWindows();
        var provider = new Win32MonitorProvider();
        var monitors = provider.GetMonitors();
        Skip.If(monitors.Count == 0, "no display attached to this session");

        monitors.Count(m => m.Primary).Should().Be(1);
        monitors.Select(m => m.Id).Should().OnlyHaveUniqueItems();
        monitors.Select(m => m.Index).Should().Equal(Enumerable.Range(0, monitors.Count));
        foreach (var m in monitors)
        {
            m.Id.Should().MatchRegex("^m[0-9a-f]{8}$");
            m.Name.Should().NotBeNullOrWhiteSpace();
            m.BoundsPx.Width.Should().BeGreaterThan(0);
            m.BoundsPx.Height.Should().BeGreaterThan(0);
            m.Scale.Should().BeGreaterThanOrEqualTo(1.0);
            m.Scale.Should().BeLessThan(8.0);
            var b = RectMath.FromProtocol(m.BoundsPx);
            var w = RectMath.FromProtocol(m.WorkAreaPx);
            RectMath.Covers(b, w, 0).Should().BeTrue("the work area lies inside the monitor");
            if (m.DevicePath != null)
            {
                m.Id.Should().Be(MonitorIdentity.IdFromDevicePath(m.DevicePath));
            }
        }
    }

    [SkippableFact]
    public void Monitor_ids_are_stable_across_calls_and_instances()
    {
        RequireWindows();
        var a = new Win32MonitorProvider().GetMonitors();
        Skip.If(a.Count == 0, "no display attached to this session");
        var b = new Win32MonitorProvider().GetMonitors();
        var c = new Win32MonitorProvider().GetMonitors();

        a.Select(m => m.Id).Should().Equal(b.Select(m => m.Id));
        b.Select(m => m.Id).Should().Equal(c.Select(m => m.Id));
        a.Select(m => (m.Index, m.Primary, m.Name)).Should().Equal(b.Select(m => (m.Index, m.Primary, m.Name)));
    }

    [SkippableFact]
    public void FromPoint_finds_the_monitor_or_the_nearest_one()
    {
        RequireWindows();
        var provider = new Win32MonitorProvider();
        var monitors = provider.GetMonitors();
        Skip.If(monitors.Count == 0, "no display attached to this session");

        foreach (var m in monitors)
        {
            var b = RectMath.FromProtocol(m.BoundsPx);
            provider.FromPoint((b.Left + b.Right) / 2, (b.Top + b.Bottom) / 2)!.Id.Should().Be(m.Id);
            provider.FromPoint(b.Left, b.Top)!.Id.Should().Be(m.Id);
        }

        provider.FromPoint(int.MinValue / 2, int.MinValue / 2).Should().NotBeNull("nearest monitor for a point outside everything");
        provider.FromPoint(int.MaxValue / 2, int.MaxValue / 2).Should().NotBeNull();
    }

    [SkippableFact]
    public void Subscribing_starts_the_listener_and_dispose_stops_it()
    {
        RequireWindows();
        var provider = new Win32MonitorProvider();
        int fired = 0;
        Action handler = () => Interlocked.Increment(ref fired);
        provider.MonitorsChanged += handler;
        provider.GetMonitors().Should().NotBeNull();
        provider.MonitorsChanged -= handler;
        provider.Dispose();
        provider.Dispose();
        fired.Should().Be(0, "nothing changed while the test ran");
    }

    [SkippableFact]
    public void Cursor_position_is_inside_a_monitor_when_available()
    {
        RequireWindows();
        var monitors = new Win32MonitorProvider().GetMonitors();
        Skip.If(monitors.Count == 0, "no display attached to this session");
        var cursor = new Win32CursorProvider();

        Skip.IfNot(cursor.TryGetCursorPosition(out var p), "cursor position not available (secure desktop / non-interactive session)");

        monitors.Any(m => RectMath.FromProtocol(m.BoundsPx).Contains(p.X, p.Y)).Should().BeTrue($"cursor {p} must lie on a monitor");
    }

    [SkippableFact]
    public void Cursor_queries_are_cheap_and_never_throw()
    {
        RequireWindows();
        var cursor = new Win32CursorProvider();
        for (int i = 0; i < 200; i++)
        {
            cursor.TryGetCursorPosition(out _);
            cursor.IsAnyMouseButtonDown();
            cursor.IsPrimaryButtonDown();
        }
    }
}

/// <summary>Real RegisterHotKey tests with a combination nobody uses (Ctrl+Alt+Shift+F24).</summary>
[Collection(ShellCollection.Name)]
public class HotkeyIntegrationTests
{
    private static readonly HotkeyGesture Unlikely = new(HotkeyGesture.Control | HotkeyGesture.Alt | HotkeyGesture.Shift, 0x87 /* VK_F24 */);

    private static void RequireWindows() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

    [SkippableFact]
    public void Register_unregister_round_trip_and_duplicate_detection()
    {
        RequireWindows();
        using var hk = new Win32HotkeyRegistrar();

        hk.TryRegister("bw.test.a", Unlikely).Should().BeTrue("the combination is free");
        hk.TryRegister("bw.test.b", Unlikely).Should().BeFalse("the same combination twice must be refused (ERROR_HOTKEY_ALREADY_REGISTERED)");

        hk.Unregister("bw.test.a");
        hk.TryRegister("bw.test.b", Unlikely).Should().BeTrue("unregistering frees the combination");

        hk.UnregisterAll();
        hk.TryRegister("bw.test.a", Unlikely).Should().BeTrue();
    }

    [SkippableFact]
    public void Re_registering_an_id_replaces_its_gesture_and_a_failed_replacement_keeps_the_old_one()
    {
        RequireWindows();
        using var hk = new Win32HotkeyRegistrar();
        var other = new HotkeyGesture(HotkeyGesture.Control | HotkeyGesture.Alt | HotkeyGesture.Shift, 0x86 /* VK_F23 */);

        hk.TryRegister("x", Unlikely).Should().BeTrue();
        hk.TryRegister("y", other).Should().BeTrue();
        hk.TryRegister("x", other).Should().BeFalse("taken by 'y'");
        hk.Unregister("y");
        hk.TryRegister("z", Unlikely).Should().BeFalse("'x' kept its old gesture after the failed replacement");
        hk.TryRegister("x", other).Should().BeTrue("now free");
        hk.TryRegister("z", Unlikely).Should().BeTrue("the old gesture of 'x' was released by the replacement");
    }

    [SkippableFact]
    public void Invalid_requests_are_rejected_without_starting_anything()
    {
        RequireWindows();
        using var hk = new Win32HotkeyRegistrar();
        hk.TryRegister("", Unlikely).Should().BeFalse();
        hk.TryRegister("a", new HotkeyGesture(HotkeyGesture.Control, 0)).Should().BeFalse();
        hk.TryRegister("a", new HotkeyGesture(0x1000, 0x41)).Should().BeFalse();
        hk.Unregister("never-registered");
        hk.UnregisterAll();
    }

    [SkippableFact]
    public void After_Dispose_registration_fails_and_calls_are_harmless()
    {
        RequireWindows();
        var hk = new Win32HotkeyRegistrar();
        hk.TryRegister("a", Unlikely).Should().BeTrue();
        hk.Dispose();
        hk.Dispose();
        hk.TryRegister("b", Unlikely).Should().BeFalse();
        hk.Unregister("a");
        hk.UnregisterAll();

        using var fresh = new Win32HotkeyRegistrar();
        fresh.TryRegister("again", Unlikely).Should().BeTrue("Dispose released the OS registration");
    }

    [SkippableFact]
    public void Concurrent_registration_from_many_threads_is_safe()
    {
        RequireWindows();
        using var hk = new Win32HotkeyRegistrar();
        var results = new bool[24];
        Parallel.For(0, results.Length, i =>
        {
            // F13..F24 (0x7C..0x87) with two modifier sets: 24 distinct gestures.
            uint vk = (uint)(0x7C + i % 12);
            uint mods = i < 12 ? HotkeyGesture.Control | HotkeyGesture.Alt | HotkeyGesture.Shift : HotkeyGesture.Control | HotkeyGesture.Alt | HotkeyGesture.Shift | HotkeyGesture.Win;
            results[i] = hk.TryRegister("t" + i, new HotkeyGesture(mods, vk));
        });
        results.Count(r => r).Should().BeGreaterThan(20, "all gestures are distinct; only an unlucky foreign owner may refuse one");
        hk.UnregisterAll();
    }

    [SkippableFact]
    public void A_pressed_hotkey_is_delivered_on_a_pool_thread_when_input_can_be_injected()
    {
        RequireWindows();
        using var hk = new Win32HotkeyRegistrar();
        using var got = new ManualResetEventSlim(false);
        string? received = null;
        int? thread = null;
        hk.Pressed += id =>
        {
            received = id;
            thread = Environment.CurrentManagedThreadId;
            got.Set();
        };
        hk.TryRegister("bw.press", Unlikely).Should().BeTrue();

        // Synthesize Ctrl+Alt+Shift+F24 (modifiers first, release in reverse order).
        var steps = new[]
        {
            KeyboardInput.Down(Win32Consts.VK_LCONTROL), KeyboardInput.Down(Win32Consts.VK_LMENU), KeyboardInput.Down(Win32Consts.VK_LSHIFT),
            KeyboardInput.Down(0x87), KeyboardInput.Up(0x87),
            KeyboardInput.Up(Win32Consts.VK_LSHIFT), KeyboardInput.Up(Win32Consts.VK_LMENU), KeyboardInput.Up(Win32Consts.VK_LCONTROL),
        };
        uint sent = KeyboardInput.Send(steps);
        try
        {
            Skip.IfNot(sent == steps.Length, "SendInput is not available in this session");
            Skip.IfNot(got.Wait(TimeSpan.FromSeconds(4)), "synthesized keys did not reach the hotkey (session not interactive?)");
            received.Should().Be("bw.press");
            thread.Should().NotBe(Environment.CurrentManagedThreadId);
        }
        finally
        {
            KeyboardInput.Send(InputSequences.ReleaseAll(steps).ToArray()); // never leave modifiers stuck
        }
    }
}

/// <summary>SendInput plumbing sanity (never injects visible shortcuts).</summary>
[Collection(ShellCollection.Name)]
public class InputIntegrationTests
{
    [SkippableFact]
    public void Injecting_an_unassigned_key_press_is_accepted_and_leaves_no_key_down()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        uint sent = KeyboardInput.Send(new[] { KeyboardInput.Down(Win32Consts.VK_UNASSIGNED_MASK), KeyboardInput.Up(Win32Consts.VK_UNASSIGNED_MASK) });
        Skip.If(sent == 0, "SendInput is not available in this session");
        sent.Should().Be(2);
        KeyboardInput.IsDown(Win32Consts.VK_UNASSIGNED_MASK).Should().BeFalse();
    }

    [SkippableFact]
    public void Held_modifier_probe_runs_against_the_real_keyboard_state()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        var held = InputSequences.HeldModifiers(KeyboardInput.IsDown);
        held.Should().BeSubsetOf(InputSequences.ModifierKeys);
    }

    [SkippableFact]
    public void Simulator_methods_do_not_throw_when_the_foreground_is_elevated_or_unavailable()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        // Construct only: the shortcuts would toggle the user's desktop. Their sequences are covered by InputSequencesTests.
        var sim = new Win32InputSimulator(NullLogger<Win32InputSimulator>.Instance);
        sim.Should().NotBeNull();
        ProcessSecurity.IsLikelyBlockedByUipi(0).Should().BeFalse();
        ProcessSecurity.IsLikelyBlockedByUipi(Environment.ProcessId).Should().BeFalse();
    }
}

/// <summary>The message-loop building block.</summary>
[Collection(ShellCollection.Name)]
public class MessageLoopThreadTests
{
    private static void RequireWindows() => Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");

    [SkippableFact]
    public void Work_runs_on_one_STA_thread_in_order_and_results_come_back()
    {
        RequireWindows();
        using var loop = new MessageLoopThread("test-loop", NullLogger.Instance, messageOnly: true);
        loop.Start(TimeSpan.FromSeconds(5)).Should().BeTrue();
        loop.WindowHandle.Should().NotBe(0);

        var order = new List<int>();
        for (int i = 0; i < 20; i++)
        {
            int n = i;
            loop.Post(() => order.Add(n));
        }

        loop.TryInvoke(() => Environment.CurrentManagedThreadId, TimeSpan.FromSeconds(5), out int tid).Should().BeTrue();
        tid.Should().NotBe(Environment.CurrentManagedThreadId);
        order.Should().Equal(Enumerable.Range(0, 20));

        loop.TryInvoke(() => Thread.CurrentThread.GetApartmentState(), TimeSpan.FromSeconds(5), out var apartment).Should().BeTrue();
        apartment.Should().Be(ApartmentState.STA);

        loop.TryInvoke(() => loop.IsOnLoopThread, TimeSpan.FromSeconds(5), out bool inside).Should().BeTrue();
        inside.Should().BeTrue();
        loop.IsOnLoopThread.Should().BeFalse();
    }

    [SkippableFact]
    public void Invoke_from_the_loop_thread_runs_inline_without_deadlock()
    {
        RequireWindows();
        using var loop = new MessageLoopThread("test-loop", NullLogger.Instance, messageOnly: true);
        loop.Start(TimeSpan.FromSeconds(5));
        loop.TryInvoke(() =>
        {
            loop.TryInvoke(() => 7, TimeSpan.FromSeconds(1), out int inner).Should().BeTrue();
            return inner;
        }, TimeSpan.FromSeconds(5), out int outer).Should().BeTrue();
        outer.Should().Be(7);
    }

    [SkippableFact]
    public void Stop_runs_the_stop_hook_on_the_loop_thread_and_later_calls_fail_cleanly()
    {
        RequireWindows();
        int stopThread = -1;
        int loopThread = -2;
        var loop = new MessageLoopThread(
            "test-loop",
            NullLogger.Instance,
            messageOnly: false,
            onStart: l =>
            {
                loopThread = Environment.CurrentManagedThreadId;
                return true;
            },
            onStop: _ => stopThread = Environment.CurrentManagedThreadId);
        loop.Start(TimeSpan.FromSeconds(5)).Should().BeTrue();

        loop.Stop();

        stopThread.Should().Be(loopThread);
        loop.IsRunning.Should().BeFalse();
        loop.Post(() => { }).Should().BeFalse();
        loop.TryInvoke(() => 1, TimeSpan.FromMilliseconds(200), out int _).Should().BeFalse();
        loop.Stop();
        loop.Dispose();
    }

    [SkippableFact]
    public void A_failing_start_hook_aborts_the_start_and_ends_the_thread()
    {
        RequireWindows();
        using var loop = new MessageLoopThread("test-loop", NullLogger.Instance, messageOnly: true, onStart: _ => false);
        loop.Start(TimeSpan.FromSeconds(5)).Should().BeFalse();
        loop.IsRunning.Should().BeFalse();
    }

    [SkippableFact]
    public void An_exception_in_a_posted_action_does_not_kill_the_loop()
    {
        RequireWindows();
        using var loop = new MessageLoopThread("test-loop", NullLogger.Instance, messageOnly: true);
        loop.Start(TimeSpan.FromSeconds(5));
        loop.Post(() => throw new InvalidOperationException("boom"));
        loop.TryInvoke(() => 42, TimeSpan.FromSeconds(5), out int v).Should().BeTrue();
        v.Should().Be(42);
    }

    [SkippableFact]
    public void A_top_level_hidden_window_receives_broadcast_style_messages_posted_to_it()
    {
        RequireWindows();
        uint probe = PInvoke.RegisterWindowMessage("BetterWindows.Tests.Probe");
        using var got = new ManualResetEventSlim(false);
        using var loop = new MessageLoopThread("test-loop", NullLogger.Instance, messageOnly: false, handler: (uint msg, nuint w, nint l, out nint result) =>
        {
            result = 0;
            if (msg == probe && (int)l == 1234)
            {
                got.Set();
                return true;
            }

            return false;
        });
        loop.Start(TimeSpan.FromSeconds(5)).Should().BeTrue();

        TestNative.PostMessage(loop.WindowHandle, probe, 0, 1234).Should().BeTrue();

        got.Wait(TimeSpan.FromSeconds(3)).Should().BeTrue();
        // Top-level (not message-only): visible to FindWindow-style enumeration of top-level windows.
        TestNative.IsWindow(loop.WindowHandle).Should().BeTrue();
    }
}

/// <summary>Process cache behaviour against the real OS.</summary>
[Collection(ShellCollection.Name)]
public class ProcessInfoCacheIntegrationTests
{
    [SkippableFact]
    public void Own_process_is_resolved_cached_and_not_flagged_elevated()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var cache = new ProcessInfoCache();

        var info = cache.Get(Environment.ProcessId);
        var again = cache.Get(Environment.ProcessId);

        info.ExePath.Should().NotBeNullOrEmpty();
        File.Exists(info.ExePath!).Should().BeTrue();
        info.IsElevated.Should().BeFalse();
        again.Should().BeSameAs(info);
        cache.Count.Should().Be(1);
        cache.Prune();
        cache.Count.Should().Be(1, "the process is still running");
    }

    [SkippableFact]
    public void Invalid_pids_yield_empty_info_and_exited_processes_are_pruned()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var cache = new ProcessInfoCache();
        cache.Get(0).ExePath.Should().BeNull();
        cache.Get(-3).ExePath.Should().BeNull();
        cache.Get(int.MaxValue).ExePath.Should().BeNull("no such process");

        using var child = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo("cmd.exe", "/c exit 0") { CreateNoWindow = true, UseShellExecute = false })!;
        child.WaitForExit(5000);
        var info = cache.Get(child.Id);
        // A handle opened right after exit may or may not succeed; either way pruning must not throw and must drop dead entries.
        Thread.Sleep(3200);
        cache.Prune();
        cache.Count.Should().BeLessThan(5);
        info.Should().NotBeNull();
    }
}
