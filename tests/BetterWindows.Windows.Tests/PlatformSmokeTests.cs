using BetterWindows.Core.Platform;
using BetterWindows.Native;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Native.Tests;

/// <summary>
/// Runs on real Windows only (GitHub Actions windows-latest). On other OSes every test is skipped.
/// Rules for tests in this project: tolerate missing hardware (no battery, no Bluetooth, no media session),
/// never change user-visible state permanently, always restore what you touched.
/// </summary>
public class PlatformSmokeTests
{
    [SkippableFact]
    public void All_platform_ports_resolve_from_DI()
    {
        Skip.IfNot(OperatingSystem.IsWindows(), "Windows only");
        var sc = new ServiceCollection();
        sc.AddBetterWindowsPlatform();
        using var sp = sc.BuildServiceProvider();
        foreach (var d in sc) sp.GetRequiredService(d.ServiceType).Should().NotBeNull();
    }
}
