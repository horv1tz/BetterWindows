using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Host.Surfaces;

/// <summary>Snapshot of the two pointer zones of one auto-hiding surface (immutable: swapped atomically by the UI thread, read by the poll thread).</summary>
internal sealed record AutoHideZoneSnapshot(NativeRect RevealZonePx, NativeRect KeepZonePx)
{
    public static readonly AutoHideZoneSnapshot None = new(new NativeRect(0, 0, 0, 0), new NativeRect(0, 0, 0, 0));
}

/// <summary>
/// THE single cursor poll loop of the Host (~50 ms), alive ONLY while at least one auto-hiding surface (dock or menu bar) is registered; idle otherwise (the project rule is "no
/// timer faster than 1 s unless it is needed"). Each registered surface has an <see cref="AutoHideStateMachine"/>; every tick feeds it the pointer position
/// (ICursorProvider.TryGetCursorPosition; a failed read - secure desktop, UAC - skips the tick) and reports reveal/hide transitions through a callback that the caller
/// marshals to the UI thread. The poll thread never touches WPF: surfaces publish their zones as immutable snapshots. Time comes from the injected TimeProvider.
/// </summary>
internal sealed class AutoHideCoordinator : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(50);

    private sealed class Entry
    {
        public required SurfaceId Id;
        public required AutoHideStateMachine Machine;
        public AutoHideZoneSnapshot Zones = AutoHideZoneSnapshot.None;
    }

    private readonly ICursorProvider _cursor;
    private readonly TimeProvider _time;
    private readonly Action<SurfaceId, bool> _onTransition;
    private readonly Func<bool> _overlayVisible;
    private readonly ILogger _log;
    private readonly AutoHideOptions _options;
    private readonly object _gate = new();
    private readonly Dictionary<SurfaceId, Entry> _entries = new();
    private readonly long _t0;
    private ITimer? _timer;
    private int _ticking;
    private bool _disposed;

    public AutoHideCoordinator(ICursorProvider cursor, TimeProvider time, Action<SurfaceId, bool> onTransition, Func<bool> overlayVisible, ILogger? log = null, AutoHideOptions? options = null)
    {
        _cursor = cursor;
        _time = time;
        _onTransition = onTransition;
        _overlayVisible = overlayVisible;
        _log = log ?? NullLogger.Instance;
        _options = options ?? AutoHideOptions.Default;
        _t0 = time.GetTimestamp();
    }

    /// <summary>True while the poll timer runs (diagnostics/tests).</summary>
    public bool IsPolling { get { lock (_gate) return _timer is not null; } }

    public int Count { get { lock (_gate) return _entries.Count; } }

    public bool IsRevealed(SurfaceId id) { lock (_gate) return _entries.TryGetValue(id, out var e) && e.Machine.Revealed; }

    /// <summary>Starts watching a surface. <paramref name="initiallyRevealed"/>: what the surface shows right now.</summary>
    public void Register(SurfaceId id, bool initiallyRevealed)
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_entries.TryGetValue(id, out var existing)) { existing.Machine.Reset(initiallyRevealed); return; }
            _entries[id] = new Entry { Id = id, Machine = new AutoHideStateMachine(_options, initiallyRevealed) };
            EnsureTimerLocked();
        }
    }

    public void Unregister(SurfaceId id)
    {
        lock (_gate)
        {
            if (!_entries.Remove(id)) return;
            if (_entries.Count == 0) StopTimerLocked();
        }
    }

    /// <summary>The surface moved/resized: swap in its new zones (physical px).</summary>
    public void UpdateZones(SurfaceId id, NativeRect revealZonePx, NativeRect keepZonePx)
    {
        lock (_gate)
        {
            if (_entries.TryGetValue(id, out var e)) e.Zones = new AutoHideZoneSnapshot(revealZonePx, keepZonePx);
        }
    }

    /// <summary>Core override (ISurfaceManager.SetDockRevealed). Returns the transition it caused.</summary>
    public AutoHideTransition SetForced(SurfaceId id, bool forced)
    {
        AutoHideTransition t;
        lock (_gate)
        {
            if (!_entries.TryGetValue(id, out var e)) return AutoHideTransition.None;
            t = e.Machine.SetForced(forced);
        }
        if (t != AutoHideTransition.None) Raise(id, t);
        return t;
    }

    /// <summary>The surface was re-shown/hidden by the UI itself (e.g. reveal state forced by a settings change).</summary>
    public void ResetState(SurfaceId id, bool revealed)
    {
        lock (_gate) { if (_entries.TryGetValue(id, out var e)) e.Machine.Reset(revealed); }
    }

    /// <summary>One poll tick at monotonic time <paramref name="now"/> (public for tests; the timer calls <see cref="Tick()"/>).</summary>
    public void Tick(TimeSpan now)
    {
        if (!_cursor.TryGetCursorPosition(out var p)) return;
        var overlay = SafeOverlayVisible();
        List<(SurfaceId, AutoHideTransition)>? transitions = null;
        lock (_gate)
        {
            if (_disposed) return;
            foreach (var e in _entries.Values)
            {
                var z = e.Zones;
                var inReveal = AutoHideZones.Contains(z.RevealZonePx, p);
                var inKeep = AutoHideZones.Contains(z.KeepZonePx, p);
                var t = e.Machine.Update(now, inReveal, inKeep, overlay);
                if (t != AutoHideTransition.None) (transitions ??= new()).Add((e.Id, t));
            }
        }
        if (transitions is null) return;
        foreach (var (id, t) in transitions) Raise(id, t);
    }

    private void Tick()
    {
        // The timer may fire while the previous tick is still running (slow cursor API): skip instead of queueing.
        if (Interlocked.Exchange(ref _ticking, 1) == 1) return;
        try { Tick(_time.GetElapsedTime(_t0)); }
        catch (Exception ex) { _log.LogDebug(ex, "auto-hide tick failed"); }
        finally { Volatile.Write(ref _ticking, 0); }
    }

    private bool SafeOverlayVisible()
    {
        try { return _overlayVisible(); }
        catch { return false; }
    }

    private void Raise(SurfaceId id, AutoHideTransition t)
    {
        try { _onTransition(id, t == AutoHideTransition.Reveal); }
        catch (Exception ex) { _log.LogError(ex, "auto-hide transition handler failed for {Surface}", id); }
    }

    private void EnsureTimerLocked()
    {
        if (_timer is not null) return;
        _timer = _time.CreateTimer(_ => Tick(), null, PollInterval, PollInterval);
    }

    private void StopTimerLocked()
    {
        _timer?.Dispose();
        _timer = null;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _entries.Clear();
            StopTimerLocked();
        }
    }
}
