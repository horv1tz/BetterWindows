using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Ipc;

/// <summary>Who sent a message.</summary>
public sealed record CallContext(SurfaceId Surface, CancellationToken Cancellation = default);

/// <summary>
/// Registry + dispatcher for UI -> host messages. Controllers register handlers in Start();
/// the Host feeds every incoming web message to <see cref="DispatchAsync"/>.
/// </summary>
public interface IMessageRouter
{
    /// <summary>Call with params and a result.</summary>
    void Handle<TParams, TResult>(string type, Func<CallContext, TParams, Task<TResult>> handler);
    /// <summary>Call with params and no result.</summary>
    void Handle<TParams>(string type, Func<CallContext, TParams, Task> handler);
    /// <summary>Call without params, with a result.</summary>
    void HandleNoParams<TResult>(string type, Func<CallContext, Task<TResult>> handler);
    /// <summary>Call without params or result.</summary>
    void HandleNoParams(string type, Func<CallContext, Task> handler);
    /// <summary>Fire-and-forget UI event with payload.</summary>
    void OnEvent<T>(string type, Action<CallContext, T> handler);
    /// <summary>Fire-and-forget UI event without payload.</summary>
    void OnEvent(string type, Action<CallContext> handler);

    /// <summary>All call/event types that currently have a handler (tests assert full coverage of the catalog).</summary>
    IReadOnlyCollection<string> RegisteredTypes { get; }

    /// <summary>Parse one raw web message from <paramref name="from"/>, run the handler, post the result to the sender.</summary>
    Task DispatchAsync(SurfaceId from, string rawJson, CancellationToken ct = default);
}

public sealed class MessageRouter : IMessageRouter
{
    private delegate Task<JsonNode?> CallHandler(CallContext ctx, JsonNode? payload);
    private delegate void EventHandler(CallContext ctx, JsonNode? payload);

    private readonly ConcurrentDictionary<string, CallHandler> _calls = new();
    private readonly ConcurrentDictionary<string, EventHandler> _events = new();
    private readonly SurfaceRegistry _registry;
    private readonly ILogger<MessageRouter> _log;

    public MessageRouter(SurfaceRegistry registry, ILogger<MessageRouter> log)
    {
        _registry = registry;
        _log = log;
    }

    public IReadOnlyCollection<string> RegisteredTypes => _calls.Keys.Concat(_events.Keys).ToArray();

    public void Handle<TParams, TResult>(string type, Func<CallContext, TParams, Task<TResult>> handler) =>
        Add(type, async (ctx, p) => ProtocolJson.ToNode(await handler(ctx, Parse<TParams>(p)).ConfigureAwait(false)));

    public void Handle<TParams>(string type, Func<CallContext, TParams, Task> handler) =>
        Add(type, async (ctx, p) => { await handler(ctx, Parse<TParams>(p)).ConfigureAwait(false); return null; });

    public void HandleNoParams<TResult>(string type, Func<CallContext, Task<TResult>> handler) =>
        Add(type, async (ctx, _) => ProtocolJson.ToNode(await handler(ctx).ConfigureAwait(false)));

    public void HandleNoParams(string type, Func<CallContext, Task> handler) =>
        Add(type, async (ctx, _) => { await handler(ctx).ConfigureAwait(false); return null; });

    public void OnEvent<T>(string type, Action<CallContext, T> handler) =>
        _events[type] = (ctx, p) => handler(ctx, Parse<T>(p));

    public void OnEvent(string type, Action<CallContext> handler) => _events[type] = (ctx, _) => handler(ctx);

    private void Add(string type, CallHandler h)
    {
        if (!_calls.TryAdd(type, h)) throw new InvalidOperationException($"duplicate handler for call '{type}'");
    }

    private static T Parse<T>(JsonNode? payload)
    {
        if (payload is null)
        {
            // Missing payload: records with defaults are valid "empty" params (e.g. WeatherGetParams).
            try { return Activator.CreateInstance<T>(); }
            catch (MissingMethodException) { throw ProtocolException.BadRequest($"missing payload ({typeof(T).Name})"); }
        }
        try { return payload.Deserialize<T>(ProtocolJson.Options)!; }
        catch (JsonException ex) { throw ProtocolException.BadRequest($"invalid payload for {typeof(T).Name}: {ex.Message}"); }
    }

    public async Task DispatchAsync(SurfaceId from, string rawJson, CancellationToken ct = default)
    {
        Envelope? env;
        try { env = JsonSerializer.Deserialize<Envelope>(rawJson, ProtocolJson.Options); }
        catch (JsonException ex)
        {
            _log.LogWarning("Malformed message from {Surface}: {Error}", from, ex.Message);
            return;
        }
        if (env is null || env.Type is null) return;

        var ctx = new CallContext(from, ct);
        if (env.Kind == EnvelopeKind.Event)
        {
            if (_events.TryGetValue(env.Type, out var eh))
            {
                try { eh(ctx, env.Payload); }
                catch (Exception ex) { _log.LogError(ex, "Event handler '{Type}' failed", env.Type); }
            }
            else _log.LogDebug("No handler for UI event '{Type}' from {Surface}", env.Type, from);
            return;
        }

        if (env.Kind != EnvelopeKind.Call || env.Id is null) return;

        Envelope reply;
        if (!_calls.TryGetValue(env.Type, out var ch))
        {
            reply = Error(env.Id, "notFound", $"no handler for call '{env.Type}'");
        }
        else
        {
            try
            {
                var result = await ch(ctx, env.Payload).ConfigureAwait(false);
                reply = new Envelope { Kind = EnvelopeKind.Result, Id = env.Id, Ok = true, Payload = result };
            }
            catch (ProtocolException pe)
            {
                reply = Error(env.Id, pe.Code, pe.Message);
            }
            catch (OperationCanceledException)
            {
                reply = Error(env.Id, "cancelled", "operation cancelled");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "Call '{Type}' from {Surface} failed", env.Type, from);
                reply = Error(env.Id, "internal", ex.Message);
            }
        }

        var channel = _registry.Get(from);
        channel?.Post(JsonSerializer.Serialize(reply, ProtocolJson.Options));

        if (env.Type == Msg.SurfaceReady && reply.Ok == true) _registry.RaiseReady(from);
    }

    private static Envelope Error(string id, string code, string message) => new()
    {
        Kind = EnvelopeKind.Result, Id = id, Ok = false, Error = new ErrorInfo { Code = code, Message = message },
    };
}
