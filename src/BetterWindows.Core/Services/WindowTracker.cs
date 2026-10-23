using BetterWindows.Core.Platform;
using BetterWindows.Core.WindowTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Services;

/// <summary>
/// The taskbar-eligible windows with app identity, kept in sync from <see cref="IWindowSystem"/> events.
/// <para>
/// Model: ONE immutable <see cref="WindowSnapshot"/> (most recently activated window first; <see cref="WindowSnapshot.Version"/> increments on every change) is replaced
/// atomically under a short lock. Nothing is ever raised or called out while a lock is held.
/// </para>
/// <para>
/// Threading: <see cref="IWindowSystem.WindowChanged"/> arrives on the hook thread, so the handler only records "this handle is dirty" and arms a
/// <see cref="TimeProvider"/> timer (5 ms; 100 ms for mere moves); the dirty handles are then re-described (<see cref="IWindowSystem.TryDescribe"/>) on the timer
/// thread, one batch at a time (single flight). <see cref="Changed"/> is coalesced: at most one raise per 30 ms (leading edge, then one trailing raise).
/// A full re-enumeration (reconcile) runs at Start and every 30 s and heals missed events (including a window that became eligible without an event).
/// </para>
/// <para>
/// Foreground rule: the last eligible window that received the foreground and is not ours. When the foreground window is ours or not eligible the previous value is
/// kept, except that an owned window (a dialog) of a tracked window counts as its owner. A window that is destroyed while foreground leaves Foreground null until
/// the next foreground event. MRU: initial order = z-order of the first enumeration, new windows join at the end until they are activated.
/// </para>
/// </summary>
public sealed class WindowTracker : IWindowTracker, IDisposable
{
    /// <summary>Minimum spacing of <see cref="Changed"/> raises.</summary>
    public static readonly TimeSpan ChangedInterval = TimeSpan.FromMilliseconds(30);
    /// <summary>Period of the safety-net re-enumeration.</summary>
    public static readonly TimeSpan ReconcileInterval = TimeSpan.FromSeconds(30);
    internal static readonly TimeSpan UrgentDelay = TimeSpan.FromMilliseconds(5);
    internal static readonly TimeSpan CosmeticDelay = TimeSpan.FromMilliseconds(100);
    private const int MaxPending = 4096;
    private const int MaxOwnerHops = 4;

    private readonly IWindowSystem _ws;
    private readonly IAppIdentityResolver _resolver;
    private readonly IAppCatalog _catalog;
    private readonly TimeProvider _time;
    private readonly ILogger _log;

    // ---- published state (replaced atomically) ----
    private WindowSnapshot _snapshot = WindowSnapshot.Empty;
    private IReadOnlyDictionary<long, TrackedWindow> _index = new Dictionary<long, TrackedWindow>();

    // ---- mutable state, guarded by _gate ----
    private readonly object _gate = new();
    private readonly Dictionary<long, TrackedWindow> _windows = new();
    private readonly List<long> _mru = new();
    private long _foregroundHandle;

    // ---- pending work, guarded by _pendingGate ----
    private readonly object _pendingGate = new();
    private readonly Dictionary<long, WindowEventKind> _pending = new();
    private readonly List<long> _pendingForeground = new();
    private bool _processing, _processArmed, _rerun, _reconcileRequested, _reidentify;
    private TimeSpan _armedDue;

    // ---- lifecycle ----
    private readonly object _lifecycle = new();
    private volatile bool _started;
    private ITimer? _processTimer, _raiseTimer, _reconcileTimer;

    // ---- Changed coalescing, guarded by _raiseGate ----
    private readonly object _raiseGate = new();
    private bool _raiseArmed, _hasRaised;
    private long _lastRaise;

    public WindowTracker(IWindowSystem windows, IAppIdentityResolver resolver, IAppCatalog catalog, TimeProvider time, ILogger<WindowTracker>? log = null)
    {
        _ws = windows;
        _resolver = resolver;
        _catalog = catalog;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "WindowTracker";
    public WindowSnapshot Snapshot => Volatile.Read(ref _snapshot);
    public IReadOnlyList<TrackedWindow> Windows => Snapshot.Windows;
    public TrackedWindow? Foreground => Snapshot.Foreground;
    public TrackedWindow? Find(long handle) => Volatile.Read(ref _index).TryGetValue(handle, out var w) ? w : null;

    public event Action<WindowSnapshot>? Changed;
    public event Action<TrackedWindow?>? ForegroundChanged;

    // =====================================================================================================================================
    //  Lifecycle
    // =====================================================================================================================================

    public void Start()
    {
        try
        {
            var published = false;
            lock (_lifecycle)
            {
                if (_started) return;
                _started = true;
                _processTimer ??= _time.CreateTimer(OnProcessTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _raiseTimer ??= _time.CreateTimer(OnRaiseTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
                _reconcileTimer ??= _time.CreateTimer(OnReconcileTimer, null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

                // Hold the single-flight flag while the first snapshot is taken, so a batch triggered by an early event cannot interleave with it.
                lock (_pendingGate) { _processing = true; _rerun = false; }
                _ws.WindowChanged += OnWindowChanged;
                _catalog.Changed += OnCatalogChanged;
                try { _ws.Start(); } catch (Exception ex) { _log.LogError(ex, "IWindowSystem.Start failed; window tracking starts from a one-off enumeration only"); }
                try { published = Reconcile(); } catch (Exception ex) { _log.LogError(ex, "Initial window enumeration failed"); }

                bool again;
                lock (_pendingGate)
                {
                    _processing = false;
                    again = _rerun || _pending.Count > 0 || _reconcileRequested || _reidentify;
                    _rerun = false;
                }
                if (again) ScheduleProcess(urgent: true);
                SafeChange(_reconcileTimer, ReconcileInterval, ReconcileInterval);
            }
            Notify(published, 0); // outside the lifecycle lock: subscribers may call back into the tracker
        }
        catch (Exception ex) { _log.LogError(ex, "WindowTracker.Start failed"); }
    }

    public void Stop()
    {
        try
        {
            lock (_lifecycle)
            {
                if (!_started) return;
                _started = false;
                _ws.WindowChanged -= OnWindowChanged;
                _catalog.Changed -= OnCatalogChanged;
                SafeChange(_processTimer, Timeout.InfiniteTimeSpan);
                SafeChange(_raiseTimer, Timeout.InfiniteTimeSpan);
                SafeChange(_reconcileTimer, Timeout.InfiniteTimeSpan);
                lock (_pendingGate) { _pending.Clear(); _pendingForeground.Clear(); _processArmed = false; _reconcileRequested = false; _reidentify = false; }
                lock (_raiseGate) { _raiseArmed = false; }
                try { _ws.Stop(); } catch (Exception ex) { _log.LogError(ex, "IWindowSystem.Stop failed"); }
            }
        }
        catch (Exception ex) { _log.LogError(ex, "WindowTracker.Stop failed"); }
    }

    public void Dispose()
    {
        Stop();
        lock (_lifecycle)
        {
            _processTimer?.Dispose(); _processTimer = null;
            _raiseTimer?.Dispose(); _raiseTimer = null;
            _reconcileTimer?.Dispose(); _reconcileTimer = null;
        }
    }

    // =====================================================================================================================================
    //  Event intake (hook thread: record and arm a timer, nothing else)
    // =====================================================================================================================================

    private void OnWindowChanged(WindowEvent e)
    {
        try
        {
            if (!_started || e.Handle == 0) return;
            var cosmetic = e.Kind == WindowEventKind.Moved;
            // A move of a window we do not track cannot change membership in a way worth a describe; the 30 s reconcile heals the rare zero-size -> sized case.
            if (cosmetic && !Volatile.Read(ref _index).ContainsKey(e.Handle)) return;
            lock (_pendingGate)
            {
                if (_pending.Count >= MaxPending && !_pending.ContainsKey(e.Handle))
                {
                    // Event storm: stop remembering individual handles, one full enumeration will catch up.
                    _pending.Clear(); _pendingForeground.Clear(); _reconcileRequested = true;
                }
                else if (!cosmetic || !_pending.ContainsKey(e.Handle)) _pending[e.Handle] = e.Kind;
                if (e.Kind == WindowEventKind.Foreground)
                {
                    _pendingForeground.Remove(e.Handle);
                    if (_pendingForeground.Count < MaxPending) _pendingForeground.Add(e.Handle);
                }
            }
            ScheduleProcess(urgent: !cosmetic);
        }
        catch (Exception ex) { _log.LogError(ex, "WindowTracker event intake failed"); }
    }

    private void OnCatalogChanged()
    {
        // The identity resolver subscribed to the catalog first, so its cache is already cleared when this runs.
        try
        {
            if (!_started) return;
            lock (_pendingGate) _reidentify = true;
            ScheduleProcess(urgent: false);
        }
        catch (Exception ex) { _log.LogError(ex, "WindowTracker catalog notification failed"); }
    }

    private void OnReconcileTimer(object? _)
    {
        try
        {
            if (!_started) return;
            lock (_pendingGate) _reconcileRequested = true;
            ScheduleProcess(urgent: true);
        }
        catch (Exception ex) { _log.LogError(ex, "WindowTracker reconcile scheduling failed"); }
    }

    private void ScheduleProcess(bool urgent)
    {
        var due = urgent ? UrgentDelay : CosmeticDelay;
        ITimer? timer;
        lock (_pendingGate)
        {
            if (_processing) { _rerun = true; return; }
            if (_processArmed && _armedDue <= due) return;
            _processArmed = true;
            _armedDue = due;
            timer = _processTimer;
        }
        SafeChange(timer, due);
    }

    private void OnProcessTimer(object? _)
    {
        if (!_started) return;
        lock (_pendingGate)
        {
            _processArmed = false;
            if (_processing) { _rerun = true; return; }
            _processing = true;
        }
        try { ProcessBatch(); }
        catch (Exception ex) { _log.LogError(ex, "Window tracker batch failed"); }
        finally
        {
            bool again;
            lock (_pendingGate)
            {
                _processing = false;
                again = _rerun || _pending.Count > 0 || _reconcileRequested || _reidentify;
                _rerun = false;
            }
            if (again && _started) ScheduleProcess(urgent: true);
        }
    }

    // =====================================================================================================================================
    //  Processing (single flight)
    // =====================================================================================================================================

    private void ProcessBatch()
    {
        Dictionary<long, WindowEventKind> batch;
        List<long> foregroundEvents;
        bool reconcile, reidentify;
        lock (_pendingGate)
        {
            batch = new Dictionary<long, WindowEventKind>(_pending);
            foregroundEvents = new List<long>(_pendingForeground);
            _pending.Clear();
            _pendingForeground.Clear();
            reconcile = _reconcileRequested;
            reidentify = _reidentify;
            _reconcileRequested = false;
            _reidentify = false;
        }

        var foregroundBefore = Snapshot.Foreground?.Handle ?? 0;
        var published = false;
        if (reconcile) published |= Reconcile();

        if (batch.Count > 0 || reidentify)
        {
            var described = new Dictionary<long, RawWindow?>();
            var updates = new List<(long Handle, TrackedWindow? Window)>();
            bool Visible(long h) => Describe(described, h)?.IsVisible == true;

            foreach (var (handle, kind) in batch)
            {
                if (kind == WindowEventKind.Destroyed) { described[handle] = null; updates.Add((handle, null)); continue; }
                if (!TryDescribe(handle, out var raw)) continue; // transient failure: keep what we know
                described[handle] = raw;
                updates.Add((handle, Track(raw, Visible)));
            }

            if (reidentify)
            {
                foreach (var tw in Snapshot.Windows)
                {
                    if (updates.Any(u => u.Handle == tw.Handle)) continue;
                    updates.Add((tw.Handle, Retrack(tw)));
                }
            }

            // A window that appears already holding the foreground (its Foreground event came first, or was missed) becomes the foreground window.
            var fg = foregroundEvents;
            if (updates.Any(u => u.Window is not null && Find(u.Handle) is null))
            {
                var now = SafeForeground();
                if (now != 0 && updates.Any(u => u.Handle == now && u.Window is not null && Find(now) is null) && !fg.Contains(now)) fg = new List<long>(fg) { now };
            }

            published |= Commit(updates, presentSet: null, fg, h => Describe(described, h));
        }
        else if (foregroundEvents.Count > 0)
        {
            published |= Commit(new List<(long, TrackedWindow?)>(), presentSet: null, foregroundEvents, h => SafeDescribeOrNull(h));
        }

        Notify(published, foregroundBefore);
    }

    /// <summary>Full enumeration: adds new eligible windows, drops vanished/ineligible ones, refreshes changed ones, and re-syncs the foreground.</summary>
    private bool Reconcile()
    {
        IReadOnlyList<RawWindow> all;
        try { all = _ws.EnumerateTopLevel(); }
        catch (Exception ex) { _log.LogError(ex, "EnumerateTopLevel failed; keeping the current window list"); return false; }

        var byHandle = new Dictionary<long, RawWindow?>(all.Count);
        foreach (var raw in all) byHandle[raw.Handle] = raw;
        bool Visible(long h) => byHandle.TryGetValue(h, out var r) && r?.IsVisible == true;

        var present = new HashSet<long>();
        var updates = new List<(long Handle, TrackedWindow? Window)>(all.Count);
        foreach (var raw in all)
        {
            var tracked = Track(raw, Visible);
            if (tracked is null) continue;
            present.Add(raw.Handle);
            updates.Add((raw.Handle, tracked));
        }

        var fg = SafeForeground();
        return Commit(updates, present, fg != 0 ? new List<long> { fg } : new List<long>(), h => Describe(byHandle, h));
    }

    private TrackedWindow? Track(RawWindow? raw, Func<long, bool> ownerVisible)
    {
        if (raw is null || !WindowFilter.IsEligible(raw, ownerVisible)) return null;
        try
        {
            var id = _resolver.Resolve(raw);
            return new TrackedWindow(raw, id.AppId, id.Name);
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not resolve the identity of window {Handle}", raw.Handle);
            return null;
        }
    }

    private TrackedWindow Retrack(TrackedWindow tw)
    {
        try
        {
            var id = _resolver.Resolve(tw.Raw);
            return tw with { AppId = id.AppId, AppName = id.Name };
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Could not re-resolve the identity of window {Handle}", tw.Handle);
            return tw;
        }
    }

    private RawWindow? Describe(Dictionary<long, RawWindow?> cache, long handle)
    {
        if (cache.TryGetValue(handle, out var cached)) return cached;
        TryDescribe(handle, out var raw);
        cache[handle] = raw;
        return raw;
    }

    private RawWindow? SafeDescribeOrNull(long handle) { TryDescribe(handle, out var raw); return raw; }

    /// <summary>false = the port failed (do not conclude the window is gone); true with raw == null = the window no longer exists.</summary>
    private bool TryDescribe(long handle, out RawWindow? raw)
    {
        try { raw = _ws.TryDescribe(handle); return true; }
        catch (Exception ex) { _log.LogDebug(ex, "TryDescribe({Handle}) failed", handle); raw = null; return false; }
    }

    private long SafeForeground()
    {
        try { return _ws.GetForegroundWindow(); }
        catch (Exception ex) { _log.LogDebug(ex, "GetForegroundWindow failed"); return 0; }
    }

    // =====================================================================================================================================
    //  Commit: the only place that mutates the tracked state
    // =====================================================================================================================================

    /// <summary>Applies the updates under the state lock and publishes a new snapshot if anything changed. Returns true when a snapshot was published; raising is the caller's job (<see cref="Notify"/>).</summary>
    private bool Commit(List<(long Handle, TrackedWindow? Window)> updates, HashSet<long>? presentSet, IReadOnlyList<long> foregroundHandles, Func<long, RawWindow?> rawOf)
    {
        lock (_gate)
        {
            var changed = false;

            if (presentSet is not null)
            {
                foreach (var h in _windows.Keys.Where(k => !presentSet.Contains(k)).ToArray())
                    changed |= RemoveLocked(h);
            }

            foreach (var (handle, window) in updates)
            {
                if (window is null) { changed |= RemoveLocked(handle); continue; }
                if (_windows.TryGetValue(handle, out var old))
                {
                    if (!old.Equals(window)) { _windows[handle] = window; changed = true; }
                }
                else
                {
                    _windows[handle] = window;
                    _mru.Add(handle);
                    changed = true;
                }
            }

            foreach (var raw in foregroundHandles)
            {
                var target = MapForegroundLocked(raw, rawOf);
                if (target == 0) continue; // our own window / non-eligible: keep the previous foreground
                if (_mru.Count > 0 && _mru[0] != target)
                {
                    _mru.Remove(target);
                    _mru.Insert(0, target);
                    changed = true;
                }
                if (_foregroundHandle != target) { _foregroundHandle = target; changed = true; }
            }

            if (changed) PublishLocked();
            return changed;
        }
    }

    /// <summary>Raises ForegroundChanged (if the foreground handle differs from <paramref name="foregroundBefore"/>) and the coalesced Changed. Never called under a lock.</summary>
    private void Notify(bool published, long foregroundBefore)
    {
        if (!published) return;
        var snap = Snapshot;
        if ((snap.Foreground?.Handle ?? 0) != foregroundBefore) Raise(ForegroundChanged, snap.Foreground);
        RequestChanged();
    }

    private bool RemoveLocked(long handle)
    {
        if (!_windows.Remove(handle)) return false;
        _mru.Remove(handle);
        if (_foregroundHandle == handle) _foregroundHandle = 0;
        return true;
    }

    /// <summary>The tracked handle a foreground event stands for: the window itself, or the nearest tracked owner of a non-eligible window (a dialog); 0 for ours/unknown.</summary>
    private long MapForegroundLocked(long handle, Func<long, RawWindow?> rawOf)
    {
        for (var hop = 0; hop < MaxOwnerHops && handle != 0; hop++)
        {
            if (_windows.ContainsKey(handle)) return handle;
            var raw = rawOf(handle);
            if (raw is null || raw.IsOwnProcess) return 0;
            handle = raw.OwnerHandle;
        }
        return 0;
    }

    private WindowSnapshot PublishLocked()
    {
        var list = new TrackedWindow[_mru.Count];
        for (var i = 0; i < list.Length; i++) list[i] = _windows[_mru[i]];
        var fg = _foregroundHandle != 0 && _windows.TryGetValue(_foregroundHandle, out var f) ? f : null;
        var snap = new WindowSnapshot(_snapshot.Version + 1, list, fg);
        Volatile.Write(ref _index, new Dictionary<long, TrackedWindow>(_windows));
        Volatile.Write(ref _snapshot, snap);
        return snap;
    }

    // =====================================================================================================================================
    //  Changed coalescing (>= 30 ms apart: leading edge immediately, then one trailing raise)
    // =====================================================================================================================================

    private void RequestChanged()
    {
        var raiseNow = false;
        ITimer? timer = null;
        TimeSpan wait = default;
        lock (_raiseGate)
        {
            if (_raiseArmed) return; // the pending trailing raise will deliver the newest snapshot
            var now = _time.GetTimestamp();
            var since = _hasRaised ? _time.GetElapsedTime(_lastRaise, now) : TimeSpan.MaxValue;
            if (since >= ChangedInterval)
            {
                _hasRaised = true;
                _lastRaise = now;
                raiseNow = true;
            }
            else
            {
                _raiseArmed = true;
                wait = ChangedInterval - since;
                timer = _raiseTimer;
            }
        }
        if (raiseNow) RaiseChanged();
        else SafeChange(timer, wait);
    }

    private void OnRaiseTimer(object? _)
    {
        if (!_started) return;
        lock (_raiseGate)
        {
            if (!_raiseArmed) return;
            _raiseArmed = false;
            _hasRaised = true;
            _lastRaise = _time.GetTimestamp();
        }
        RaiseChanged();
    }

    private void RaiseChanged() => Raise(Changed, Snapshot);

    private void Raise<T>(Action<T>? handler, T arg)
    {
        if (handler is null) return;
        foreach (var d in handler.GetInvocationList())
        {
            try { ((Action<T>)d)(arg); }
            catch (Exception ex) { _log.LogError(ex, "WindowTracker subscriber threw"); }
        }
    }

    private static void SafeChange(ITimer? timer, TimeSpan due, TimeSpan? period = null)
    {
        try { timer?.Change(due, period ?? Timeout.InfiniteTimeSpan); }
        catch (ObjectDisposedException) { /* stopped concurrently */ }
    }
}
