using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Channels;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Ipc;

/// <summary>
/// Who sent a message. <see cref="Surface"/> is the identity the HOST assigned to the channel (never taken from the payload).
/// <see cref="Cancellation"/> is cancelled when the surface closes, when it restarts its handshake (surface.ready again), or when the caller's token is.
/// </summary>
public sealed record CallContext(SurfaceId Surface, ISurfaceChannel Channel, CancellationToken Cancellation = default);

/// <summary>
/// Registry + dispatcher for UI -> host messages (docs/IPC.md sections 9-11).
/// Threading: the Host calls <see cref="DispatchAsync"/> from WebMessageReceived on the UI thread and does not await it. The envelope is parsed and validated
/// on the caller thread; the handler is then STARTED on the thread pool in arrival order per surface (one queue + one reader per channel; slow handlers never delay
/// the START of later ones). The returned task completes after the reply is posted.
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

    /// <summary>Validate and run one raw web message from <paramref name="from"/>; the reply is posted to THAT channel object.</summary>
    Task DispatchAsync(ISurfaceChannel from, string rawJson, CancellationToken ct = default);
}

public sealed class MessageRouter : IMessageRouter
{
    private delegate Task<JsonNode?> CallHandler(CallContext ctx, JsonNode? payload);
    private delegate void EventHandler(CallContext ctx, JsonNode? payload);

    private sealed class PerSurface
    {
        public required Channel<Func<Task>> Queue;
        public required TokenBucket Messages, Logs, Shapes, Resizes;
        public int InFlight;
    }

    private readonly ConcurrentDictionary<string, CallHandler> _calls = new();
    private readonly ConcurrentDictionary<string, EventHandler> _events = new();
    private readonly ConcurrentDictionary<ISurfaceChannel, PerSurface> _surfaces = new(ReferenceEqualityComparer.Instance as IEqualityComparer<ISurfaceChannel>);
    private readonly SurfaceRegistry _registry;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    public MessageRouter(SurfaceRegistry registry, TimeProvider time, ILogger<MessageRouter>? log = null)
    {
        _registry = registry;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _registry.ChannelClosed += ch => { if (_surfaces.TryRemove(ch, out var s)) s.Queue.Writer.TryComplete(); };
    }

    public IReadOnlyCollection<string> RegisteredTypes => _calls.Keys.Concat(_events.Keys).ToArray();

    public void Handle<TParams, TResult>(string type, Func<CallContext, TParams, Task<TResult>> handler) =>
        AddCall(type, async (ctx, p) => ProtocolJson.ToNode(await handler(ctx, Parse<TParams>(p)).ConfigureAwait(false)));

    public void Handle<TParams>(string type, Func<CallContext, TParams, Task> handler) =>
        AddCall(type, async (ctx, p) => { await handler(ctx, Parse<TParams>(p)).ConfigureAwait(false); return null; });

    public void HandleNoParams<TResult>(string type, Func<CallContext, Task<TResult>> handler) =>
        AddCall(type, async (ctx, _) => ProtocolJson.ToNode(await handler(ctx).ConfigureAwait(false)));

    public void HandleNoParams(string type, Func<CallContext, Task> handler) =>
        AddCall(type, async (ctx, _) => { await handler(ctx).ConfigureAwait(false); return null; });

    public void OnEvent<T>(string type, Action<CallContext, T> handler) => AddEvent(type, (ctx, p) => handler(ctx, Parse<T>(p)));
    public void OnEvent(string type, Action<CallContext> handler) => AddEvent(type, (ctx, _) => handler(ctx));

    private void AddCall(string type, CallHandler h)
    {
        if (!_calls.TryAdd(type, h)) throw new InvalidOperationException($"duplicate handler for call '{type}'");
    }

    private void AddEvent(string type, EventHandler h)
    {
        if (!_events.TryAdd(type, h)) throw new InvalidOperationException($"duplicate handler for ui event '{type}'");
    }

    /// <summary>Missing payload is the same as "{}": records with defaults are valid empty params, [JsonRequired] fields make it a badRequest.</summary>
    private static T Parse<T>(JsonNode? payload)
    {
        try { return (payload ?? new JsonObject()).Deserialize<T>(ProtocolJson.Options)!; }
        catch (JsonException ex) { throw ProtocolException.BadRequest($"invalid payload for {typeof(T).Name}: {Truncate(ex.Message, 200)}"); }
        catch (NotSupportedException ex) { throw ProtocolException.BadRequest($"invalid payload for {typeof(T).Name}: {Truncate(ex.Message, 200)}"); }
    }

    private static string Truncate(string s, int n) => s.Length <= n ? s : s[..n];

    private PerSurface Surface(ISurfaceChannel ch) => _surfaces.GetOrAdd(ch, c =>
    {
        var s = new PerSurface
        {
            Queue = Channel.CreateUnbounded<Func<Task>>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false }),
            Messages = new TokenBucket(Limits.MessagesPerSecond, Limits.MessageBurst, _time),
            Logs = new TokenBucket(Limits.LogPerSecond, Limits.LogPerSecond, _time),
            Shapes = new TokenBucket(Limits.ShapePerSecond, Limits.ShapePerSecond, _time),
            Resizes = new TokenBucket(Limits.ResizePerSecond, Limits.ResizePerSecond, _time),
        };
        _ = Task.Run(() => ReaderLoop(s));
        return s;
    });

    /// <summary>Starts queued work in arrival order; never awaits a handler (the work item returns its own Task).</summary>
    private async Task ReaderLoop(PerSurface s)
    {
        try
        {
            await foreach (var work in s.Queue.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                try { _ = work(); }
                catch (Exception ex) { _log.LogError(ex, "Queued message work threw synchronously"); }
            }
        }
        catch (Exception ex) { _log.LogError(ex, "Message reader loop failed"); }
    }

    public Task DispatchAsync(ISurfaceChannel from, string rawJson, CancellationToken ct = default)
    {
        if (rawJson.Length > Limits.MaxMessageChars)
        {
            _log.LogWarning("Dropped oversized message ({Length} chars) from {Surface}", rawJson.Length, from.Id);
            return Task.CompletedTask;
        }

        Envelope? env;
        try { env = JsonSerializer.Deserialize<Envelope>(rawJson, ProtocolJson.Options); }
        catch (JsonException ex)
        {
            _log.LogWarning("Malformed message from {Surface}: {Error}", from.Id, Truncate(ex.Message, 200));
            ReplyToUnparsable(from, rawJson);
            return Task.CompletedTask;
        }
        if (env?.Type is null) return Task.CompletedTask;

        var sf = Surface(from);
        var ctx = new CallContext(from.Id, from, ct);
        var kind = from.Id.Kind;

        if (env.Kind == EnvelopeKind.Event) return DispatchEvent(env, sf, ctx, kind);
        if (env.Kind != EnvelopeKind.Call || env.Id is null) return Task.CompletedTask;
        return DispatchCall(env, sf, ctx, kind);
    }

    private Task DispatchEvent(Envelope env, PerSurface sf, CallContext ctx, SurfaceKind kind)
    {
        var type = env.Type!;
        if (env.V != ProtocolInfo.Version) { _log.LogWarning("Dropped event '{Type}' with protocol version {V} from {Surface}", type, env.V, ctx.Surface); return Task.CompletedTask; }
        if (!sf.Messages.TryTake()) { _log.LogDebug("Rate limit: dropped event '{Type}' from {Surface}", type, ctx.Surface); return Task.CompletedTask; }
        if (!MessageCatalog.ByType.TryGetValue(type, out var def) || def.Kind != MessageKind.UiEvent)
        {
            _log.LogDebug("Unknown UI event '{Type}' from {Surface}", type, ctx.Surface);
            return Task.CompletedTask;
        }
        if (!def.IsAllowedFrom(kind)) { _log.LogWarning("Forbidden UI event '{Type}' from {Surface}", type, ctx.Surface); return Task.CompletedTask; }
        var bucket = type switch { Msg.SurfaceLog => sf.Logs, Msg.SurfaceShape => sf.Shapes, Msg.SurfaceResize => sf.Resizes, _ => null };
        if (bucket is not null && !bucket.TryTake()) return Task.CompletedTask;
        if (!_events.TryGetValue(type, out var handler)) { _log.LogDebug("No handler for UI event '{Type}'", type); return Task.CompletedTask; }

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(sf, ctx, () =>
        {
            try { handler(ctx with { Cancellation = Link(ctx) }, env.Payload); }
            catch (Exception ex) { _log.LogError(ex, "Event handler '{Type}' failed", type); }
            tcs.TrySetResult();
            return Task.CompletedTask;
        }, tcs);
        return tcs.Task;
    }

    private Task DispatchCall(Envelope env, PerSurface sf, CallContext ctx, SurfaceKind kind)
    {
        var type = env.Type!;
        var id = env.Id!;
        if (env.V != ProtocolInfo.Version) { Reply(ctx.Channel, Envelope.Fail(id, "badRequest", $"protocol mismatch (host {ProtocolInfo.Version}, ui {env.V})")); return Task.CompletedTask; }
        if (!sf.Messages.TryTake()) { Reply(ctx.Channel, Envelope.Fail(id, "busy", "rate limit exceeded")); return Task.CompletedTask; }
        if (!_calls.TryGetValue(type, out var handler)) { Reply(ctx.Channel, Envelope.Fail(id, "notFound", $"no handler for call '{type}'")); return Task.CompletedTask; }
        if (MessageCatalog.ByType.TryGetValue(type, out var def) && def.Kind == MessageKind.Call && !def.IsAllowedFrom(kind))
        {
            _log.LogWarning("Forbidden call '{Type}' from {Surface}", type, ctx.Surface);
            Reply(ctx.Channel, Envelope.Fail(id, "forbidden", $"'{type}' is not allowed from {kind}"));
            return Task.CompletedTask;
        }
        if (Interlocked.Increment(ref sf.InFlight) > Limits.MaxInFlightCallsPerSurface)
        {
            Interlocked.Decrement(ref sf.InFlight);
            Reply(ctx.Channel, Envelope.Fail(id, "busy", "too many calls in flight"));
            return Task.CompletedTask;
        }

        var isReady = type == Msg.SurfaceReady;
        if (isReady) _registry.BeginHandshake(ctx.Channel);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        Enqueue(sf, ctx, async () =>
        {
            Envelope reply;
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(_registry.SessionToken(ctx.Channel), ctx.Cancellation);
            var callCtx = ctx with { Cancellation = linked.Token };
            try
            {
                var result = await handler(callCtx, env.Payload).ConfigureAwait(false);
                reply = Envelope.Ok_(id, result);
            }
            catch (ProtocolException pe) { reply = Envelope.Fail(id, pe.Code, pe.Message); }
            catch (OperationCanceledException) { reply = Envelope.Fail(id, "cancelled", "operation cancelled"); }
            catch (Exception ex)
            {
                _log.LogError(ex, "Call '{Type}' from {Surface} failed", type, ctx.Surface);
                reply = Envelope.Fail(id, "internal", Truncate(ex.Message, 300));
            }
            finally { Interlocked.Decrement(ref sf.InFlight); }

            var posted = Reply(ctx.Channel, reply);
            if (isReady && reply.Ok == true && posted) _registry.CompleteHandshake(ctx.Channel);
            tcs.TrySetResult();
        }, tcs);
        return tcs.Task;
    }

    private void Enqueue(PerSurface sf, CallContext ctx, Func<Task> work, TaskCompletionSource tcs)
    {
        if (!sf.Queue.Writer.TryWrite(work))
        {
            // Surface already closed: nothing will run the work, so release the caller.
            tcs.TrySetResult();
        }
    }

    private CancellationToken Link(CallContext ctx) => _registry.SessionToken(ctx.Channel);

    private bool Reply(ISurfaceChannel channel, Envelope reply)
    {
        try { channel.Post(reply.ToJson()); return true; }
        catch (Exception ex) { _log.LogDebug(ex, "Could not post a reply to {Surface} (webview gone?)", channel.Id); return false; }
    }

    /// <summary>A call whose envelope could not be parsed still deserves an answer if we can read its id: otherwise the UI waits for its 30 s timeout.</summary>
    private void ReplyToUnparsable(ISurfaceChannel channel, string raw)
    {
        try
        {
            if (JsonNode.Parse(raw) is JsonObject o && o["kind"]?.GetValue<string>() == EnvelopeKind.Call && o["id"] is JsonValue idv && idv.TryGetValue<string>(out var id))
                Reply(channel, Envelope.Fail(id, "badRequest", "malformed envelope"));
        }
        catch { /* not even JSON */ }
    }
}
