using System.Text.Json;
using System.Text.Json.Nodes;
using BetterWindows.Core.Hosting;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Tests.Support;

/// <summary>
/// Spins up the real Core DI graph with fake platform ports, a manual clock, a temp data directory and fake surfaces, so a test can drive a controller exactly like the UI would:
/// <c>await h.CallAsync&lt;DockPinParams&gt;(surface, Msg.DockPin, ...)</c>. Disposing it fails the test if any surface received a protocol violation.
/// </summary>
public sealed class CoreHarness : IDisposable
{
    private int _callId;
    private readonly List<FakeSurface> _surfaces = new();
    public ServiceProvider Services { get; }
    public SurfaceRegistry Registry => Services.GetRequiredService<SurfaceRegistry>();
    public IMessageRouter Router => Services.GetRequiredService<IMessageRouter>();
    public IEventHub Hub => Services.GetRequiredService<IEventHub>();
    public IStatePublisher State => Services.GetRequiredService<IStatePublisher>();
    public ManualTimeProvider Time { get; } = new();
    /// <summary>Isolated data directory (deleted on Dispose): config.json, state\, data\ ...</summary>
    public IAppPaths Paths { get; }
    /// <summary>The registered fake (or any service) by type, e.g. <c>h.Get&lt;FakeWindowSystem&gt;()</c>.</summary>
    public T Get<T>() where T : notnull => Services.GetRequiredService<T>();

    /// <param name="configure">Replace/add services AFTER the defaults (fake ports are registered first, so replacements win).</param>
    /// <param name="addAllControllers">false: no controllers are registered; add only the ones under test in <paramref name="configure"/>.</param>
    public CoreHarness(Action<IServiceCollection>? configure = null, bool addAllControllers = false)
    {
        var root = Path.Combine(Path.GetTempPath(), "bw-tests-" + Guid.NewGuid().ToString("N"));
        Paths = new AppPaths(root);
        var sc = new ServiceCollection();
        sc.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        sc.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        sc.AddBetterWindowsCore();
        sc.AddSingleton<TimeProvider>(Time);
        sc.AddSingleton(Paths);
        TestPorts.AddFakePorts(sc);
        if (!addAllControllers)
        {
            foreach (var d in sc.Where(d => d.ServiceType == typeof(IShellController)).ToArray()) sc.Remove(d);
        }
        configure?.Invoke(sc);
        Services = sc.BuildServiceProvider();
    }

    /// <summary>Registers a fake surface. ready:true (default) marks it ready as if surface.ready had completed; ready:false leaves the handshake to the test.</summary>
    public FakeSurface AddSurface(SurfaceKind kind, string? monitorId = null, bool ready = true)
    {
        var s = new FakeSurface(kind, monitorId);
        Registry.Register(s);
        _surfaces.Add(s);
        if (ready) Registry.MarkReadyForTests(s);
        return s;
    }

    /// <summary>Do the real handshake: send surface.ready and wait for the reply (the router then pushes initial state). Throws if no surface.ready handler is registered.</summary>
    public async Task<SurfaceInit?> ReadyAsync(FakeSurface surface)
    {
        return await CallAsync<SurfaceInit>(surface, Msg.SurfaceReady, new SurfaceReadyParams { Surface = surface.Id.Kind, MonitorId = surface.Id.MonitorId });
    }

    /// <summary>Send a call as the given surface and return the full result envelope.</summary>
    public async Task<JsonObject> CallRawAsync(FakeSurface from, string type, object? payload = null)
    {
        var id = "c" + Interlocked.Increment(ref _callId);
        var env = new Envelope { V = ProtocolInfo.Version, Kind = EnvelopeKind.Call, Id = id, Type = type, Payload = payload is null ? null : ProtocolJson.ToNode(payload) };
        await Router.DispatchAsync(from, env.ToJson());
        return from.ResultFor(id) ?? throw new InvalidOperationException($"no result posted for call {type}");
    }

    /// <summary>Call and deserialise the result payload; throws if the call failed.</summary>
    public async Task<TResult?> CallAsync<TResult>(FakeSurface from, string type, object? payload = null)
    {
        var r = await CallRawAsync(from, type, payload);
        if (r["ok"]?.GetValue<bool>() != true) throw new InvalidOperationException($"call {type} failed: {r["error"]}");
        return ProtocolJson.Deserialize<TResult>(r["payload"]);
    }

    /// <summary>Send a UI -> host event.</summary>
    public Task SendEventAsync(FakeSurface from, string type, object? payload = null)
    {
        var env = new Envelope { V = ProtocolInfo.Version, Kind = EnvelopeKind.Event, Type = type, Payload = payload is null ? null : ProtocolJson.ToNode(payload) };
        return Router.DispatchAsync(from, env.ToJson());
    }

    public void AssertNoProtocolViolations()
    {
        var all = _surfaces.SelectMany(s => s.Violations.Select(v => $"[{s.Id}] {v}")).ToArray();
        if (all.Length > 0) throw new Xunit.Sdk.XunitException("Protocol violations posted to surfaces:\n" + string.Join("\n", all));
    }

    public void Dispose()
    {
        Services.Dispose();
        try { if (Directory.Exists(Paths.Root)) Directory.Delete(Paths.Root, true); } catch { /* best effort */ }
        AssertNoProtocolViolations();
    }
}
