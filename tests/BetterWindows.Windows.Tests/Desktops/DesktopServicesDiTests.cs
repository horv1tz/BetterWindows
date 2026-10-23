using BetterWindows.Core.Platform;
using BetterWindows.Native;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Native.Tests.Desktops;

/// <summary>
/// Constructors must not do I/O and must resolve from a bare container (no logging, no settings), because the platform registration can be
/// resolved before the Core services exist. Runs on every OS.
/// </summary>
public class DesktopServicesDiTests
{
    [Fact]
    public void Desktop_services_resolve_from_a_bare_container_without_io()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IVirtualDesktopService, VirtualDesktopService>();
        sc.AddSingleton<IWindowCapture, PrintWindowCapture>();
        sc.AddSingleton<ITrayHost, TrayHost>();
        using var sp = sc.BuildServiceProvider();

        sp.GetRequiredService<IVirtualDesktopService>().Should().BeOfType<VirtualDesktopService>();
        sp.GetRequiredService<IWindowCapture>().Should().BeOfType<PrintWindowCapture>();
        sp.GetRequiredService<ITrayHost>().IsSupported.Should().BeFalse();
    }
}
