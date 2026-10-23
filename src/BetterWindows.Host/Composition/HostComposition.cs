using BetterWindows.Core.Hosting;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Services;
using BetterWindows.Host.Bridge;
using BetterWindows.Host.Cli;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Surfaces;
using BetterWindows.Host.Tray;
using BetterWindows.Native;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Composition;

/// <summary>
/// The one DI composition of the Host (docs/ARCHITECTURE.md section 2): Core services and controllers, the Win32 platform ports wrapped by the Host safety decorators
/// (docs/IPC.md section 15), and the Host-only ports (WebView2 bridge, surfaces, lifecycle, tray). Nothing else creates these objects.
/// </summary>
public static class HostComposition
{
    /// <summary>Registers everything. <paramref name="paths"/> and <paramref name="options"/> are instances so the pre-DI startup decisions and DI see the same values.</summary>
    public static IServiceCollection AddBetterWindowsHost(this IServiceCollection services, IAppPaths paths, HostRuntimeOptions options, IUiScheduler ui)
    {
        services.AddSingleton(paths);
        services.AddSingleton(options);
        services.AddSingleton(ui);
        services.AddBetterWindowsCore();
        services.AddBetterWindowsPlatform();

        // Host safety decorators around the platform ports that change the user's shell (docs/IPC.md section 15, rules 5 and 9).
        services.AddSingleton<IProcessProbe, SystemProcessProbe>();
        services.AddSingleton<GuardianLauncher>();
        services.AddSingleton<ShellSafetyGate>();
        Decorate<ITaskbarController>(services, (inner, sp) =>
            new GuardedTaskbarController(inner, sp.GetRequiredService<ShellSafetyGate>(), sp.GetRequiredService<ILogger<GuardedTaskbarController>>()));
        Decorate<IDesktopIconsController>(services, (inner, sp) =>
            new GuardedDesktopIconsController(inner, sp.GetRequiredService<ShellSafetyGate>(), sp.GetRequiredService<ILogger<GuardedDesktopIconsController>>()));
        Decorate<IHotkeyRegistrar>(services, (inner, sp) =>
            new GuardedHotkeyRegistrar(inner, sp.GetRequiredService<HostRuntimeOptions>(), sp.GetRequiredService<ILogger<GuardedHotkeyRegistrar>>()));

        // Lifecycle and bridge.
        services.AddSingleton<ShutdownCoordinator>();
        services.AddSingleton<WebViewBridge>();
        services.AddSingleton<IWebViewBridge>(sp => sp.GetRequiredService<WebViewBridge>());
        services.AddSingleton<IAppLifecycle, AppLifecycle>();
        services.AddSingleton<IClipboard, ClipboardService>();

        // Host-side surfaces (owned by the surfaces stream; constructed through ActivatorUtilities so their constructor may take what it needs).
        services.AddSingleton<IFilePicker, FilePicker>();
        services.AddSingleton<SurfaceManager>(sp => ActivatorUtilities.CreateInstance<SurfaceManager>(sp));
        services.AddSingleton<ISurfaceManager>(sp => sp.GetRequiredService<SurfaceManager>());
        services.AddSingleton<ISurfaceHost>(sp => sp.GetRequiredService<SurfaceManager>());
        services.AddSingleton<TrayIconHost>();
        return services;
    }

    /// <summary>
    /// Replaces the singleton registration of <typeparamref name="TService"/> (registered by implementation type) with a decorator. The inner implementation stays
    /// registered under its concrete type so the decorator can resolve it.
    /// </summary>
    private static void Decorate<TService>(IServiceCollection services, Func<TService, IServiceProvider, TService> wrap) where TService : class
    {
        var original = services.LastOrDefault(d => d.ServiceType == typeof(TService))
            ?? throw new InvalidOperationException($"{typeof(TService).Name} is not registered");
        var impl = original.ImplementationType ?? throw new InvalidOperationException($"{typeof(TService).Name} must be registered by implementation type");
        services.Remove(original);
        services.TryAdd(new ServiceDescriptor(impl, impl, ServiceLifetime.Singleton));
        services.AddSingleton<TService>(sp => wrap((TService)sp.GetRequiredService(impl), sp));
    }
}
