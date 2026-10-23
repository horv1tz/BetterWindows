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
/// Spins up the real Core DI graph with fake platform ports and fake surfaces, so a test can drive a controller exactly
/// like the UI would: <c>await h.CallAsync&lt;DockPinParams&gt;(surface, Msg.DockPin, ...)</c>.
/// </summary>
public sealed class CoreHarness : IDisposable
{
    private int _callId;
    public ServiceProvider Services { get; }
    public SurfaceRegistry Registry => Services.GetRequiredService<SurfaceRegistry>();
    public IMessageRouter Router => Services.GetRequiredService<IMessageRouter>();
    public IEventHub Hub => Services.GetRequiredService<IEventHub>();

    /// <param name="configure">Register fake ports / replace services (call AFTER AddBetterWindowsCore so replacements win).</param>
    /// <param name="addAllControllers">false: register only the services; controllers under test are added by the caller.</param>
    public CoreHarness(Action<IServiceCollection>? configure = null, bool addAllControllers = false)
    {
        var sc = new ServiceCollection();
        sc.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
        sc.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
        sc.AddBetterWindowsCore();
        if (!addAllControllers)
        {
            foreach (var d in sc.Where(d => d.ServiceType == typeof(IShellController)).ToArray()) sc.Remove(d);
        }
        configure?.Invoke(sc);
        Services = sc.BuildServiceProvider();
    }

    public FakeSurface AddSurface(SurfaceKind kind, string? monitorId = null)
    {
        var s = new FakeSurface(kind, monitorId);
        Registry.Register(s);
        return s;
    }

    /// <summary>Send a call as the given surface and return the full result envelope.</summary>
    public async Task<JsonObject> CallRawAsync(FakeSurface from, string type, object? payload = null)
    {
        var id = "c" + Interlocked.Increment(ref _callId);
        var env = new Envelope { Kind = EnvelopeKind.Call, Id = id, Type = type, Payload = payload is null ? null : ProtocolJson.ToNode(payload) };
        await Router.DispatchAsync(from.Id, JsonSerializer.Serialize(env, ProtocolJson.Options));
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
        var env = new Envelope { Kind = EnvelopeKind.Event, Type = type, Payload = payload is null ? null : ProtocolJson.ToNode(payload) };
        return Router.DispatchAsync(from.Id, JsonSerializer.Serialize(env, ProtocolJson.Options));
    }

    public void Dispose() => Services.Dispose();
}
