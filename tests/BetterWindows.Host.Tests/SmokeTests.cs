namespace BetterWindows.Host.Tests;

public class SmokeTests
{
    [Fact]
    public void Assembly_loads() => typeof(BetterWindows.Host.Bridge.SurfaceFailure).Assembly.GetName().Name.Should().Be("BetterWindows");
}
