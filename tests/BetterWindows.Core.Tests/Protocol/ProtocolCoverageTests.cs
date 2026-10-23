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
    [Fact(Skip = "Enabled during integration: controllers are still stubs.")]
    public void Every_call_and_ui_event_has_a_handler()
    {
        using var h = new CoreHarness(addAllControllers: true, configure: sc => TestPorts.AddFakePorts(sc));
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
