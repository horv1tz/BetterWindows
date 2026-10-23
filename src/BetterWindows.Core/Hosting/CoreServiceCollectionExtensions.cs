using BetterWindows.Core.Controllers;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BetterWindows.Core.Hosting;

public static class CoreServiceCollectionExtensions
{
    /// <summary>
    /// Registers all Core services and controllers. Platform ports (IWindowSystem, ...) and host ports
    /// (ISurfaceManager, IAppLifecycle) must be registered by the caller (AddBetterWindowsPlatform / Host).
    /// Controller order = start order.
    /// </summary>
    public static IServiceCollection AddBetterWindowsCore(this IServiceCollection services)
    {
        services.TryAddSingleton(TimeProvider.System);
        services.TryAddSingleton<IAppPaths, AppPaths>();
        services.AddSingleton<SurfaceRegistry>();
        services.AddSingleton<IEventHub, EventHub>();
        services.AddSingleton<IStatePublisher, StatePublisher>();
        services.AddSingleton<IMessageRouter, MessageRouter>();

        services.AddSingleton<IPathGrants, PathGrants>();
        services.AddSingleton<IHotkeyStatus, HotkeyStatusService>();
        services.AddSingleton<OverlayStateService>();
        services.AddSingleton<IOverlayState>(sp => sp.GetRequiredService<OverlayStateService>());
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAppCatalog, AppCatalog>();
        services.AddSingleton<IAppIdentityResolver, AppIdentityResolver>();
        services.AddSingleton<IWindowTracker, WindowTracker>();
        // Services with a lifecycle are started by ShellRuntime BEFORE the controllers (dependency order = declaration order).
        services.AddSingleton<IShellService>(sp => (IShellService)sp.GetRequiredService<IWindowTracker>());
        services.AddSingleton<IDynamicResourceProvider, DynamicResourceRouter>();

        // Order matters: infrastructure first so initial-state pushes find their dependencies ready.
        services.AddController<SurfaceController>();
        services.AddController<SystemController>();
        services.AddController<AppsController>();
        services.AddController<ActiveAppController>();
        services.AddController<DockController>();
        services.AddController<StatusController>();
        services.AddController<MediaController>();
        services.AddController<TrayController>();
        services.AddController<OverlayController>();
        services.AddController<HotkeyController>();
        services.AddController<HotCornerController>();
        services.AddController<SearchController>();
        services.AddController<MissionControlController>();
        services.AddController<DesktopController>();
        services.AddController<WeatherController>();
        services.AddController<RemindersController>();

        services.AddSingleton<ShellRuntime>();
        return services;
    }

    /// <summary>
    /// Registers a controller whose construction failure (a platform port that throws in its constructor, a missing dependency) must not take the whole app down:
    /// it is replaced by a <see cref="FailedController"/> that ShellRuntime reports and skips.
    /// </summary>
    private static IServiceCollection AddController<T>(this IServiceCollection services) where T : class, IShellController =>
        services.AddSingleton<IShellController>(sp =>
        {
            try { return ActivatorUtilities.CreateInstance<T>(sp); }
            catch (Exception ex) { return new FailedController(typeof(T).Name, ex); }
        });
}
