using BetterWindows.Core.Hosting;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Services;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Cli;
using BetterWindows.Host.Composition;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Tests.Composition;

public class CompositionTests
{
    private static ServiceProvider Build(TempDir dir, string[]? args = null)
    {
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddBetterWindowsHost(new AppPaths(dir.Path), new HostRuntimeOptions(CommandLineParser.Parse(args ?? new[] { "--smoke" })), new ManualUiScheduler());
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }

    [SkippableFact]
    public void Every_registration_resolves_at_build_time()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "resolves native Win32 ports");
        // ValidateOnBuild walks the whole graph (Core services, controllers, Host ports, decorators) without starting anything.
        using var dir = new TempDir();
        var act = () => Build(dir).Dispose();
        act.Should().NotThrow();
    }

    [SkippableFact]
    public void Shell_safety_decorators_wrap_the_platform_ports()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "resolves native Win32 ports");
        using var dir = new TempDir();
        using var provider = Build(dir, new[] { "--smoke" });
        provider.GetRequiredService<ITaskbarController>().Should().BeOfType<GuardedTaskbarController>();
        provider.GetRequiredService<IDesktopIconsController>().Should().BeOfType<GuardedDesktopIconsController>();
        provider.GetRequiredService<IHotkeyRegistrar>().Should().BeOfType<GuardedHotkeyRegistrar>();
    }

    [SkippableFact]
    public void Lifecycle_and_bridge_are_single_instances()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "resolves native Win32 ports");
        using var dir = new TempDir();
        using var provider = Build(dir);
        var bridge = provider.GetRequiredService<WebViewBridge>();
        provider.GetRequiredService<IWebViewBridge>().Should().BeSameAs(bridge);
        provider.GetRequiredService<ShutdownCoordinator>().Should().BeSameAs(provider.GetRequiredService<ShutdownCoordinator>());
        provider.GetRequiredService<IAppLifecycle>().Should().BeOfType<AppLifecycle>();
        provider.GetRequiredService<IAppPaths>().Root.Should().Be(dir.Path);
    }

    [SkippableFact]
    public void Smoke_composition_keeps_the_native_taskbar_visible()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "resolves native Win32 ports");
        using var dir = new TempDir();
        using var provider = Build(dir, new[] { "--smoke" });
        provider.GetRequiredService<ShellSafetyGate>().HidingBlocked.Should().BeTrue();
    }
}
