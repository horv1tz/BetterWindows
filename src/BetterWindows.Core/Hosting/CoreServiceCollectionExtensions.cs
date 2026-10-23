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
        services.AddSingleton<SurfaceRegistry>();
        services.AddSingleton<IEventHub, EventHub>();
        services.AddSingleton<IMessageRouter, MessageRouter>();

        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IThemeService, ThemeService>();
        services.AddSingleton<IAppCatalog, AppCatalog>();
        services.AddSingleton<IAppIdentityResolver, AppIdentityResolver>();
        services.AddSingleton<IWindowTracker, WindowTracker>();
        services.AddSingleton<IDynamicResourceProvider, DynamicResourceRouter>();

        // Order matters: infrastructure first so initial-state pushes find their dependencies ready.
        services.AddSingleton<IShellController, SurfaceController>();
        services.AddSingleton<IShellController, SystemController>();
        services.AddSingleton<IShellController, AppsController>();
        services.AddSingleton<IShellController, ActiveAppController>();
        services.AddSingleton<IShellController, DockController>();
        services.AddSingleton<IShellController, StatusController>();
        services.AddSingleton<IShellController, MediaController>();
        services.AddSingleton<IShellController, TrayController>();
        services.AddSingleton<IShellController, OverlayController>();
        services.AddSingleton<IShellController, HotkeyController>();
        services.AddSingleton<IShellController, HotCornerController>();
        services.AddSingleton<IShellController, SearchController>();
        services.AddSingleton<IShellController, MissionControlController>();
        services.AddSingleton<IShellController, DesktopController>();
        services.AddSingleton<IShellController, WeatherController>();
        services.AddSingleton<IShellController, RemindersController>();

        services.AddSingleton<ShellRuntime>();
        return services;
    }
}
