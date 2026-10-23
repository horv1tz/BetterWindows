using BetterWindows.Core.Platform;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>
/// The shell classes are constructed by DI in the Host; constructing them must be cheap, free of side effects (no threads, no hooks, no
/// taskbar changes) and work with or without a logging setup. Runs on every OS (nothing here calls Win32).
/// </summary>
public class ShellCompositionTests
{
    private static ServiceProvider Build(bool withLogging)
    {
        var sc = new ServiceCollection();
        if (withLogging)
        {
            sc.AddSingleton<ILoggerFactory, NullLoggerFactory>();
            sc.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        }

        sc.AddBetterWindowsPlatform();
        return sc.BuildServiceProvider();
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void The_shell_ports_resolve_to_the_Win32_implementations(bool withLogging)
    {
        using var sp = Build(withLogging);

        sp.GetRequiredService<IWindowSystem>().Should().BeOfType<Win32WindowSystem>();
        sp.GetRequiredService<IMonitorProvider>().Should().BeOfType<Win32MonitorProvider>();
        sp.GetRequiredService<ICursorProvider>().Should().BeOfType<Win32CursorProvider>();
        sp.GetRequiredService<IAppBarFactory>().Should().BeOfType<AppBarFactory>();
        sp.GetRequiredService<IHotkeyRegistrar>().Should().BeOfType<Win32HotkeyRegistrar>();
        sp.GetRequiredService<IInputSimulator>().Should().BeOfType<Win32InputSimulator>();
        sp.GetRequiredService<ITaskbarController>().Should().BeOfType<Win32TaskbarController>();
    }

    [Fact]
    public void Controller_starts_in_Visible_mode_and_does_not_touch_anything_on_construction()
    {
        var surface = new FakeTaskbarSurface(new List<string>());
        var store = new FakeSnapshotStore(new List<string>());
        using var controller = new Win32TaskbarController(NullLogger.Instance, surface, store);

        controller.Current.Should().Be(BetterWindows.Core.Protocol.TaskbarMode.Visible);
        store.Exists.Should().BeFalse();
        surface.Log.Should().BeEmpty();
    }

    [Fact]
    public void AppBar_stub_signature_is_kept_public_constructor_takes_only_the_window_handle()
    {
        var bar = new AppBar(0x1234);
        bar.IsRegistered.Should().BeFalse();
        bar.ProcessWindowMessage(0x0006, 0, 0).Should().BeFalse("not registered: every message passes through");
        bar.Undock();
        bar.Dispose();
    }

    [Fact]
    public void Restorer_is_static_and_parameterless()
    {
        typeof(TaskbarRestorer).IsAbstract.Should().BeTrue();
        typeof(TaskbarRestorer).IsSealed.Should().BeTrue();
        typeof(TaskbarRestorer).GetMethod(nameof(TaskbarRestorer.RestoreNow), Type.EmptyTypes).Should().NotBeNull();
    }
}
