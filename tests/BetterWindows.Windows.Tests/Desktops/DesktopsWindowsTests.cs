using BetterWindows.Native;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Desktops;

/// <summary>
/// Real-Windows checks for the desktops, capture and tray code. They never change user-visible state: no desktop is created, switched or removed,
/// no window is moved, minimised or restored. On other systems every test is skipped.
/// </summary>
public class DesktopsWindowsTests
{
    private static bool IsCanonicalId(string text) => DesktopRegistry.TryParseId(text, out _);

    [SkippableFact]
    public void GetDesktops_lists_desktops_with_exactly_one_current_when_present()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var service = new VirtualDesktopService();
        var desktops = service.GetDesktops();
        Skip.If(desktops.Count == 0, "no VirtualDesktopIDs value on this machine");

        desktops.Select(d => d.Index).Should().Equal(Enumerable.Range(0, desktops.Count));
        desktops.Count(d => d.Current).Should().Be(1);
        desktops.Should().OnlyContain(d => IsCanonicalId(d.Id) && d.Name.Length > 0);
        service.IsSupported.Should().BeTrue();
    }

    [SkippableFact]
    public void Switch_to_the_current_desktop_is_a_no_op_success()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var service = new VirtualDesktopService();
        var current = service.GetDesktops().FirstOrDefault(d => d.Current);
        Skip.If(current is null, "no desktop list on this machine");
        service.Switch(current!.Id).Should().BeTrue();
    }

    [SkippableFact]
    public void Switch_and_move_reject_unknown_or_malformed_ids()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var service = new VirtualDesktopService();
        service.Switch("00000000-0000-0000-0000-000000000001").Should().BeFalse();
        service.Switch("not-a-guid").Should().BeFalse();
        service.MoveWindow(0, "00000000-0000-0000-0000-000000000001").Should().BeFalse();
        service.Remove("00000000-0000-0000-0000-000000000001").Should().BeFalse();
    }

    [SkippableFact]
    public void Window_queries_for_the_foreground_window_do_not_throw()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var service = new VirtualDesktopService();
        long foreground = DeskNative.GetForeground();
        Skip.If(foreground == 0, "no foreground window on this session");

        // The foreground window is on the current desktop by definition, when the public API can answer at all.
        if (service.GetWindowDesktopId(foreground) is string id)
        {
            IsCanonicalId(id).Should().BeTrue();
            service.IsWindowOnCurrentDesktop(foreground).Should().BeTrue();
        }
        service.GetWindowDesktopId(0).Should().BeNull();
        service.IsWindowOnCurrentDesktop(0).Should().BeFalse();
        service.IsWindowOnCurrentDesktop(0x7FFFFFF1).Should().BeFalse();
    }

    [SkippableFact]
    public void Changed_subscription_starts_and_stops_the_registry_watcher_cleanly()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var service = new VirtualDesktopService();
        int raised = 0;
        Action handler = () => Interlocked.Increment(ref raised);
        service.Changed += handler;
        service.Changed -= handler;
        // Nothing changed in the registry during the test, so no notification is expected.
        Thread.Sleep(150);
        raised.Should().Be(0);
    }

    [SkippableFact]
    public async Task Capture_of_invalid_or_hostile_handles_returns_null_without_throwing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        var capture = new PrintWindowCapture();
        (await capture.CaptureAsync(0, 256)).Should().BeNull();
        (await capture.CaptureAsync(-1, 256)).Should().BeNull();
        (await capture.CaptureAsync(0x7FFFFFF1, 256)).Should().BeNull();
        (await capture.CaptureAsync(0x100000000, 256)).Should().BeNull();
    }

    [SkippableFact]
    public async Task Capture_of_the_foreground_window_is_a_valid_png_or_null()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        long foreground = DeskNative.GetForeground();
        Skip.If(foreground == 0, "no foreground window on this session");

        var capture = new PrintWindowCapture();
        var png = await capture.CaptureAsync(foreground, 256);
        if (png is null) return; // minimised, cloaked or protected: a legitimate null
        png.Take(8).Should().Equal(0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A);
        png.Length.Should().BeGreaterThan(64);
    }

    [SkippableFact]
    public async Task Capture_honours_a_cancelled_token()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var capture = new PrintWindowCapture();
        Func<Task> act = () => capture.CaptureAsync(0x10, 256, cts.Token);
        await act.Should().ThrowAsync<OperationCanceledException>();
    }

    [SkippableFact]
    public void Tray_diagnostics_describe_the_taskbar_without_throwing()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        var text = TrayDiagnostics.Describe();
        text.Should().NotBeNullOrWhiteSpace();
        text.Should().Contain("Shell_NotifyIcon hosting: unsupported");
    }

    [SkippableFact]
    public void Registry_watcher_starts_on_the_desktop_key_and_stops()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        using var watcher = new DeskRegistryWatcher(VirtualDesktopService.RegistryRoot, () => { });
        var started = watcher.Start();
        Skip.IfNot(started, "the virtual desktop key is not present on this machine");
        watcher.Stop();
    }
}
