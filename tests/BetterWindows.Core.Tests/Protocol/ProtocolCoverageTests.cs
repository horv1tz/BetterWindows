using BetterWindows.Core.Protocol;
using BetterWindows.Core.Hosting;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;

namespace BetterWindows.Core.Tests.Protocol;

/// <summary>
/// Integration gate: after all controllers are implemented, EVERY call (and UI event) in the catalog must have a handler
/// once ShellRuntime has started. Enabled by the integration step (remove the Skip).
/// </summary>
public class ProtocolCoverageTests
{
    /// <summary>Guards DI: every controller/service can be constructed from the real registrations + fake ports, and start/stop cleanly.</summary>
    [Fact]
    public void Full_graph_resolves_starts_and_stops()
    {
        using var h = new CoreHarness(addAllControllers: true);
        var runtime = h.Services.GetRequiredService<ShellRuntime>();
        var expected = h.Services.GetServices<IShellController>().Count();
        runtime.Start();
        // A controller that throws in Start (e.g. a duplicate handler registration) is skipped silently by the runtime: this catches it.
        runtime.Started.Should().HaveCount(expected);
        runtime.Stop();
    }

    [Fact(Skip = "Enabled during integration: controllers are still stubs.")]
    public void Every_call_and_ui_event_has_a_handler()
    {
        using var h = new CoreHarness(addAllControllers: true);
        h.Services.GetRequiredService<ShellRuntime>().Start();

        var registered = h.Router.RegisteredTypes.ToHashSet();
        var missing = MessageCatalog.All
            .Where(m => m.Kind is MessageKind.Call or MessageKind.UiEvent)
            .Select(m => m.Type)
            .Where(t => !registered.Contains(t))
            .OrderBy(t => t)
            .ToArray();

        missing.Should().BeEmpty("every protocol call must be handled by a controller");
    }
}
