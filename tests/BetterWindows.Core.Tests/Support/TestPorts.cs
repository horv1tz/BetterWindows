using BetterWindows.Core.Platform;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace BetterWindows.Core.Tests.Support;

/// <summary>
/// Registers a fake for EVERY platform + host port, so the full controller graph can be constructed in tests.
/// Each fake is registered under both its port interface and its own concrete type (so tests can fetch <c>FakeWindowSystem</c>).
/// </summary>
public static class TestPorts
{
    public static IServiceCollection AddFakePorts(IServiceCollection sc)
    {
        Add<IWindowSystem, FakeWindowSystem>(sc);
        Add<IMonitorProvider, FakeMonitorProvider>(sc);
        Add<ICursorProvider, FakeCursorProvider>(sc);
        Add<ITaskbarController, FakeTaskbarController>(sc);
        Add<IAppBarFactory, FakeAppBarFactory>(sc);
        Add<IHotkeyRegistrar, FakeHotkeyRegistrar>(sc);
        Add<IInputSimulator, FakeInputSimulator>(sc);
        Add<IVirtualDesktopService, FakeVirtualDesktopService>(sc);
        Add<IWindowCapture, FakeWindowCapture>(sc);
        Add<ITrayHost, FakeTrayHost>(sc);
        Add<IAppCatalogSource, FakeAppCatalogSource>(sc);
        Add<IIconSource, FakeIconSource>(sc);
        Add<IAppLauncher, FakeAppLauncher>(sc);
        Add<IFileSearch, FakeFileSearch>(sc);
        Add<IAutostartService, FakeAutostartService>(sc);
        Add<IWallpaperService, FakeWallpaperService>(sc);
        Add<IDesktopIconsController, FakeDesktopIconsController>(sc);
        Add<IDesktopFolderSource, FakeDesktopFolderSource>(sc);
        Add<IBatterySource, FakeBatterySource>(sc);
        Add<INetworkSource, FakeNetworkSource>(sc);
        Add<IBluetoothSource, FakeBluetoothSource>(sc);
        Add<IAudioEndpoint, FakeAudioEndpoint>(sc);
        Add<IBrightnessControl, FakeBrightnessControl>(sc);
        Add<IMediaSource, FakeMediaSource>(sc);
        Add<IThemeSource, FakeThemeSource>(sc);
        Add<ISystemPower, FakeSystemPower>(sc);
        Add<ISurfaceManager, FakeSurfaceManager>(sc);
        Add<IAppLifecycle, FakeAppLifecycle>(sc);
        Add<IClipboard, FakeClipboard>(sc);
        Add<IFilePicker, FakeFilePicker>(sc);
        return sc;
    }

    private static void Add<TPort, TFake>(IServiceCollection sc) where TPort : class where TFake : class, TPort, new()
    {
        sc.RemoveAll<TPort>();
        sc.AddSingleton<TFake>();
        sc.AddSingleton<TPort>(sp => sp.GetRequiredService<TFake>());
    }
}
