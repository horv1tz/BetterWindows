using BetterWindows.Core.Platform;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Native;

public static class PlatformServiceCollectionExtensions
{
    /// <summary>Registers every Win32/WinRT implementation of the Core platform ports (singletons).</summary>
    public static IServiceCollection AddBetterWindowsPlatform(this IServiceCollection services)
    {
        // windows stream
        services.AddSingleton<IWindowSystem, Win32WindowSystem>();
        services.AddSingleton<IMonitorProvider, Win32MonitorProvider>();
        services.AddSingleton<ICursorProvider, Win32CursorProvider>();
        services.AddSingleton<ITaskbarController, Win32TaskbarController>();
        services.AddSingleton<IAppBarFactory, AppBarFactory>();
        services.AddSingleton<IHotkeyRegistrar, Win32HotkeyRegistrar>();
        services.AddSingleton<IInputSimulator, Win32InputSimulator>();
        // apps stream
        services.AddSingleton<IAppCatalogSource, ShellAppCatalogSource>();
        services.AddSingleton<IIconSource, ShellIconSource>();
        services.AddSingleton<IAppLauncher, ShellAppLauncher>();
        services.AddSingleton<IFileSearch, WindowsSearchFileSearch>();
        services.AddSingleton<IAutostartService, RegistryAutostartService>();
        services.AddSingleton<IWallpaperService, DesktopWallpaperService>();
        services.AddSingleton<IDesktopIconsController, Win32DesktopIconsController>();
        services.AddSingleton<IDesktopFolderSource, ShellDesktopFolderSource>();
        // status stream
        services.AddSingleton<IBatterySource, PowerBatterySource>();
        services.AddSingleton<INetworkSource, NetworkInfoSource>();
        services.AddSingleton<IBluetoothSource, BluetoothSource>();
        services.AddSingleton<IAudioEndpoint, CoreAudioEndpoint>();
        services.AddSingleton<IBrightnessControl, WmiBrightnessControl>();
        services.AddSingleton<IMediaSource, SmtcMediaSource>();
        services.AddSingleton<IThemeSource, RegistryThemeSource>();
        services.AddSingleton<ISystemPower, Win32SystemPower>();
        // desktops stream
        services.AddSingleton<IVirtualDesktopService, VirtualDesktopService>();
        services.AddSingleton<IWindowCapture, PrintWindowCapture>();
        services.AddSingleton<ITrayHost, TrayHost>();
        return services;
    }
}
