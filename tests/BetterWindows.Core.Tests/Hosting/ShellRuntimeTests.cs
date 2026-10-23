using BetterWindows.Core.Hosting;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Tests.Hosting;

public class ShellRuntimeTests
{
    private sealed class Probe : IShellController, IShellService
    {
        public Probe(string name, bool failStart = false, List<string>? log = null) { Name = name; _fail = failStart; _log = log ?? new(); }
        private readonly bool _fail; private readonly List<string> _log;
        public string Name { get; }
        public void Start() { _log.Add("start:" + Name); if (_fail) throw new InvalidOperationException("boom"); }
        public void Stop() => _log.Add("stop:" + Name);
    }

    [Fact]
    public void Services_start_before_controllers_and_stop_in_reverse()
    {
        var log = new List<string>();
        var rt = new ShellRuntime(new IShellService[] { new Probe("s1", log: log), new Probe("s2", log: log) }, new IShellController[] { new Probe("c1", log: log), new Probe("c2", log: log) }, NullLogger<ShellRuntime>.Instance);
        rt.Start(); rt.Stop();
        log.Should().Equal("start:s1", "start:s2", "start:c1", "start:c2", "stop:c2", "stop:c1", "stop:s2", "stop:s1");
    }

    [Fact]
    public void A_controller_that_fails_to_start_is_cleaned_up_and_skipped_without_affecting_others()
    {
        var log = new List<string>();
        var rt = new ShellRuntime(Array.Empty<IShellService>(), new IShellController[] { new Probe("bad", true, log), new Probe("good", log: log) }, NullLogger<ShellRuntime>.Instance);
        rt.Start();
        rt.Started.Select(c => c.Name).Should().Equal("good");
        log.Should().Contain("stop:bad"); // partial registrations of the failed controller are released
    }

    private sealed class ThrowingCtorController : IShellController
    {
        public ThrowingCtorController(IDisposable missing) { }
        public string Name => "ThrowingCtor";
        public void Start() { }
        public void Stop() { }
    }

    [Fact]
    public void A_controller_that_cannot_be_constructed_becomes_a_FailedController_instead_of_crashing_the_app()
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<IShellController>(sp =>
        {
            try { return ActivatorUtilities.CreateInstance<ThrowingCtorController>(sp); }
            catch (Exception ex) { return new FailedController(nameof(ThrowingCtorController), ex); }
        });
        sc.AddSingleton<ShellRuntime>();
        sc.AddSingleton(typeof(Microsoft.Extensions.Logging.ILogger<>), typeof(NullLogger<>));
        sc.AddSingleton<IEnumerable<IShellService>>(Array.Empty<IShellService>());
        using var sp = sc.BuildServiceProvider();
        var rt = sp.GetRequiredService<ShellRuntime>();
        rt.Start();
        rt.Started.Should().BeEmpty();
    }

    [Fact]
    public void Full_graph_starts_the_window_tracker_as_a_service()
    {
        using var h = new CoreHarness(addAllControllers: true);
        h.Services.GetServices<IShellService>().Select(s => s.Name).Should().Contain("WindowTracker");
    }
}
