using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Widgets;

/// <summary>
/// Fires <c>reminders.due</c> once per reminder. ONE pending timer (on the injected <see cref="TimeProvider"/>) is armed for the next due time and re-armed on every store
/// change (<see cref="Rearm"/>) and whenever it fires. A reminder that is due is flagged <c>notified</c> in the store BEFORE its callback runs (so it fires exactly once even
/// across restarts and concurrent passes); one that became due while nobody was watching (app not running, laptop asleep) still fires if it is at most
/// <see cref="CatchUpWindow"/> overdue, an older one is flagged silently. The wait is capped at <see cref="MaxWait"/> so clock changes and sleep/resume are noticed.
/// Completed reminders and reminders without a due time never fire.
/// </summary>
public sealed class ReminderScheduler : IDisposable
{
    public static readonly TimeSpan CatchUpWindow = TimeSpan.FromHours(24);
    public static readonly TimeSpan MaxWait = TimeSpan.FromMinutes(10);

    private readonly ReminderStore _store;
    private readonly TimeProvider _time;
    private readonly Action<Reminder> _onDue;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private ITimer? _timer;
    private bool _disposed;

    public ReminderScheduler(ReminderStore store, TimeProvider time, Action<Reminder> onDue, ILogger? log = null)
    {
        _store = store;
        _time = time;
        _onDue = onDue;
        _log = log ?? NullLogger.Instance;
    }

    /// <summary>Runs the catch-up pass (reminders already due) and arms the timer.</summary>
    public void Start()
    {
        lock (_gate) _timer ??= _time.CreateTimer(_ => OnTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        OnTimer();
    }

    /// <summary>The store changed: recompute the next due time. Cheap; safe from any thread.</summary>
    public void Rearm()
    {
        try
        {
            var now = _time.GetUtcNow();
            DateTimeOffset? next = null;
            foreach (var r in _store.Snapshot())
                if (DueUtc(r) is { } due && IsPending(r) && (next is null || due < next)) next = due;

            lock (_gate)
            {
                if (_disposed || _timer is null) return;
                if (next is null) { _timer.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan); return; }
                var wait = next.Value - now;
                if (wait < TimeSpan.Zero) wait = TimeSpan.Zero;
                if (wait > MaxWait) wait = MaxWait;
                _timer.Change(wait, Timeout.InfiniteTimeSpan);
            }
        }
        catch (Exception ex) { _log.LogError(ex, "Re-arming the reminder timer failed"); }
    }

    private void OnTimer()
    {
        try
        {
            lock (_gate) if (_disposed) return;
            Process();
        }
        catch (Exception ex) { _log.LogError(ex, "Reminder pass failed"); }
        finally { Rearm(); }
    }

    private void Process()
    {
        var now = _time.GetUtcNow();
        var overdue = new List<StoredReminder>();
        foreach (var r in _store.Snapshot())
            if (IsPending(r) && DueUtc(r) is { } due && due <= now) overdue.Add(r);
        if (overdue.Count == 0) return;

        IReadOnlyList<StoredReminder> flipped;
        try { flipped = _store.MarkNotified(overdue.Select(r => r.Id).ToHashSet(StringComparer.Ordinal)); }
        catch (IOException ex)
        {
            // Cannot persist the flag: firing now would fire again after a restart. Retry on the next pass instead.
            _log.LogWarning(ex, "Could not persist reminder state; will retry");
            return;
        }

        foreach (var r in flipped)
        {
            var overdueBy = now - (DueUtc(r) ?? now);
            if (overdueBy > CatchUpWindow) continue; // too old to be useful: flagged silently
            try { _onDue(r.ToProtocol()); }
            catch (Exception ex) { _log.LogError(ex, "reminders.due callback failed"); }
        }
    }

    /// <summary>Not completed, not yet notified.</summary>
    public static bool IsPending(StoredReminder r) => !r.Done && !r.Notified && r.Due is not null;

    /// <summary>The UTC instant of the local due time, null without one.</summary>
    public DateTimeOffset? DueUtc(StoredReminder r) =>
        r.Due is not null && ReminderRules.TryParseDue(r.Due, out var local) ? ReminderRules.ToUtc(local, _time.LocalTimeZone) : null;

    public void Dispose()
    {
        lock (_gate) { _disposed = true; _timer?.Dispose(); _timer = null; }
    }
}
