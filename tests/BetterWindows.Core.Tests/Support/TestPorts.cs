using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Support;

/// <summary>
/// Registers fake implementations of ALL platform + host ports so the full controller graph can be built in tests.
/// Owner: integration step (each stream adds its own fakes in its own folder and the integrator wires them here).
/// </summary>
public static class TestPorts
{
    public static IServiceCollection AddFakePorts(IServiceCollection sc) => sc;
}
