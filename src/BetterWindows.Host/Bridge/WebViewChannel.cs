using System.Collections.Concurrent;
using BetterWindows.Core.Ipc;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Bridge;

/// <summary>
/// The Host side of <see cref="ISurfaceChannel"/> for one WebView (docs/IPC.md sections 11 and 13). <see cref="Post"/> may be called from ANY thread and never blocks: the
/// message goes into a bounded <see cref="OutboundQueue"/> and one UI-thread flush delivers it with <c>PostWebMessageAsString</c> (the delegate passed as <c>deliver</c>).
/// Rules: replies to calls are always queued; EVENTS are dropped until the surface completed surface.ready (the registry's ready flag) and while the surface is failed;
/// nothing is delivered before the page finished loading (NavigationCompleted) and a new navigation discards what was queued for the old page.
/// </summary>
public sealed class WebViewChannel : ISurfaceChannel
{
    /// <summary>Messages delivered per UI dispatch (keeps input and rendering responsive under bursts).</summary>
    public const int FlushBatch = 32;

    private readonly IUiScheduler _ui;
    private readonly Func<ISurfaceChannel, bool> _isReady;
    private readonly Func<string, bool> _deliver;
    private readonly ILogger _log;
    private readonly bool _countTypes;
    private readonly OutboundQueue _queue = new();
    private readonly ConcurrentDictionary<string, int> _posted = new();
    private int _flushScheduled;
    private volatile bool _navCompleted, _failed, _closed;
    private long _delivered, _droppedNotReady, _droppedFailed;

    /// <param name="deliver">Runs on the UI thread: posts one JSON string to the page; returns false when the WebView is gone.</param>
    /// <param name="isReady">Has this channel completed surface.ready (SurfaceRegistry.IsReady)?</param>
    /// <param name="countTypes">Count delivered event types (smoke test: "dock.state arrived").</param>
    public WebViewChannel(SurfaceId id, IUiScheduler ui, Func<ISurfaceChannel, bool> isReady, Func<string, bool> deliver, ILogger log, bool countTypes = false)
    {
        Id = id;
        _ui = ui;
        _isReady = isReady;
        _deliver = deliver;
        _log = log;
        _countTypes = countTypes;
    }

    public SurfaceId Id { get; }
    /// <summary>Messages handed to the page.</summary>
    public long Delivered => Interlocked.Read(ref _delivered);
    /// <summary>Events dropped because the surface was not ready yet.</summary>
    public long DroppedNotReady => Interlocked.Read(ref _droppedNotReady);
    /// <summary>Messages dropped because the surface is failed.</summary>
    public long DroppedFailed => Interlocked.Read(ref _droppedFailed);
    /// <summary>Messages dropped or coalesced by the queue bounds.</summary>
    public long QueueDropped => _queue.Dropped;
    public int Pending => _queue.Count;
    public bool IsClosed => _closed;

    /// <summary>How many events of <paramref name="type"/> were delivered (only counted when constructed with <c>countTypes</c>).</summary>
    public int DeliveredCount(string type) => _posted.TryGetValue(type, out var n) ? n : 0;

    public void Post(string json)
    {
        if (_closed) { _log.LogDebug("Dropped message for closed channel {Surface}", Id); return; }
        if (_failed) { Interlocked.Increment(ref _droppedFailed); return; }
        var head = EnvelopeSniffer.Sniff(json);
        // The reply to surface.ready is posted BEFORE the registry marks the channel ready, so only events are gated.
        if (head.Kind != OutboundKind.Result && !_isReady(this)) { Interlocked.Increment(ref _droppedNotReady); return; }
        _queue.Enqueue(new OutboundMessage(head.Kind, head.Type, json));
        ScheduleFlush();
    }

    /// <summary>A new document is loading: whatever was queued belongs to the old page.</summary>
    public void NavigationStarted()
    {
        _navCompleted = false;
        _queue.Clear();
    }

    /// <summary>The document finished loading (success or not): release the queue.</summary>
    public void NavigationCompleted()
    {
        _navCompleted = true;
        ScheduleFlush();
    }

    /// <summary>A failed surface receives nothing (and its queue is emptied) until it recovers.</summary>
    public void SetFailed(bool failed)
    {
        _failed = failed;
        if (failed) _queue.Clear();
    }

    /// <summary>The attachment is gone: every later Post is dropped.</summary>
    public void Close()
    {
        _closed = true;
        _queue.Clear();
    }

    private void ScheduleFlush()
    {
        if (Interlocked.CompareExchange(ref _flushScheduled, 1, 0) != 0) return;
        _ui.Post(Flush);
    }

    private void Flush()
    {
        Volatile.Write(ref _flushScheduled, 0);
        if (_closed) { _queue.Clear(); return; }
        if (!_navCompleted) return; // NavigationCompleted schedules the next flush
        try
        {
            for (var i = 0; i < FlushBatch && _queue.TryDequeue(out var m); i++)
            {
                if (!_deliver(m.Json)) { _log.LogDebug("WebView of {Surface} is gone; dropped a message", Id); _queue.Clear(); return; }
                Interlocked.Increment(ref _delivered);
                if (_countTypes && m.Type is not null) _posted.AddOrUpdate(m.Type, 1, static (_, n) => n + 1);
            }
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Posting to {Surface} failed", Id);
        }
        if (_queue.Count > 0) ScheduleFlush();
    }
}
