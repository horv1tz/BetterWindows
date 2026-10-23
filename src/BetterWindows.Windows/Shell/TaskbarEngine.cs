using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Native;

/// <summary>
/// The taskbar state machine, independent of Win32 (it talks to <see cref="ITaskbarSurface"/> / <see cref="ITaskbarSnapshotStore"/>).
/// Invariants: (1) the original state is persisted BEFORE the first mutation and a failure to persist aborts the mutation (fail safe);
/// (2) Visible restores EXACTLY that state (auto-hide / always-on-top bits and per-taskbar visibility) and deletes the marker;
/// (3) a stale marker from a crashed run is restored before a new snapshot is taken, so the "original" is always the user's own state;
/// (4) Hidden = ABS_AUTOHIDE + SW_HIDE, AutoHide = ABS_AUTOHIDE only, never SPI_SETWORKAREA. Not thread-safe: callers serialize.
/// </summary>
internal sealed class TaskbarEngine
{
    private readonly ITaskbarSurface _surface;
    private readonly ITaskbarSnapshotStore _store;
    private readonly ILogger _log;
    private TaskbarSnapshot? _snapshot;

    public TaskbarEngine(ITaskbarSurface surface, ITaskbarSnapshotStore store, ILogger log)
    {
        _surface = surface;
        _store = store;
        _log = log;
    }

    /// <summary>We changed the taskbar and the marker file records the original state.</summary>
    public bool HasSnapshot => _snapshot != null;

    /// <summary>Applies a mode. Returns false when nothing could be applied (no Explorer taskbar yet, or the marker could not be written).</summary>
    public bool Apply(TaskbarMode mode)
    {
        if (mode == TaskbarMode.Visible)
        {
            Restore(_surface, _store, _log);
            _snapshot = null;
            return true;
        }

        if (!EnsureSnapshot())
        {
            return false;
        }

        bool hide = mode == TaskbarMode.Hidden;
        AssertAutoHide();
        foreach (var w in _surface.GetWindows())
        {
            if (hide)
            {
                if (w.IsVisible && !_surface.SetVisible(w.Handle, false))
                {
                    _log.LogWarning("Could not hide taskbar window {Handle}", w.Handle);
                }
            }
            else if (!w.IsVisible && WasVisible(w) && !_surface.SetVisible(w.Handle, true))
            {
                // AutoHide: windows hidden by an earlier Hidden mode come back (auto-hide itself keeps them out of the way).
                _log.LogWarning("Could not show taskbar window {Handle}", w.Handle);
            }
        }

        return true;
    }

    /// <summary>
    /// Re-asserts Hidden after the shell interfered (re-shown window, Explorer restart, display change): auto-hide back on, visible windows hidden.
    /// Cheap and idempotent. Returns whether anything was changed.
    /// </summary>
    public bool Enforce()
    {
        if (_snapshot == null)
        {
            return Apply(TaskbarMode.Hidden);
        }

        bool changed = AssertAutoHide();
        foreach (var w in _surface.GetWindows())
        {
            if (w.IsVisible)
            {
                changed |= _surface.SetVisible(w.Handle, false);
            }
        }

        return changed;
    }

    private bool WasVisible(TaskbarWindowInfo w) => _snapshot != null && (w.IsPrimary ? _snapshot.PrimaryVisible : _snapshot.SecondaryVisible);

    /// <summary>Makes sure ABS_AUTOHIDE is set while keeping the original always-on-top bit.</summary>
    private bool AssertAutoHide()
    {
        if (!_surface.TryGetState(out uint current))
        {
            return false;
        }

        uint wanted = (_snapshot!.StateKnown ? _snapshot.State & Win32Consts.ABS_ALWAYSONTOP : current & Win32Consts.ABS_ALWAYSONTOP) | Win32Consts.ABS_AUTOHIDE;
        if (current == wanted)
        {
            return false;
        }

        if (!_surface.TrySetState(wanted))
        {
            _log.LogWarning("ABM_SETSTATE {State} did not stick", wanted);
            return false;
        }

        return true;
    }

    private bool EnsureSnapshot()
    {
        if (_snapshot != null)
        {
            return true;
        }

        // A marker left by a crashed earlier run describes the REAL original state: undo it first, so that what we capture now is the user's own.
        if (_store.Exists)
        {
            Restore(_surface, _store, _log);
            if (_store.Exists)
            {
                _log.LogWarning("A stale taskbar marker could not be restored (Explorer not running?); not touching the taskbar yet");
                return false;
            }
        }

        var windows = _surface.GetWindows();
        if (windows.Count == 0)
        {
            return false; // Explorer is not running; the caller retries on TaskbarCreated
        }

        bool stateKnown = _surface.TryGetState(out uint state);
        var snapshot = new TaskbarSnapshot
        {
            StateKnown = stateKnown,
            State = stateKnown ? state & (Win32Consts.ABS_AUTOHIDE | Win32Consts.ABS_ALWAYSONTOP) : 0,
            PrimaryVisible = windows.Where(w => w.IsPrimary).All(w => w.IsVisible),
            SecondaryVisible = windows.Where(w => !w.IsPrimary).All(w => w.IsVisible),
            ProcessId = Environment.ProcessId,
            CreatedUtc = DateTime.UtcNow,
        };

        // Persist BEFORE the first mutation. If we cannot, do not touch the taskbar: an unrecoverable change is worse than no change.
        if (!_store.TryWrite(snapshot))
        {
            _log.LogError("Could not persist the taskbar snapshot; leaving the taskbar untouched");
            return false;
        }

        _snapshot = snapshot;
        return true;
    }

    // ============================================================================================
    //  Restore (also used by the static TaskbarRestorer, from a fresh process)
    // ============================================================================================

    /// <summary>
    /// Restores the original taskbar from the marker and deletes it. Without a marker it only repairs a taskbar window that is currently
    /// HIDDEN (which only we can cause): shows it and turns auto-hide off; a visible taskbar and the user's auto-hide preference are
    /// never touched. When Explorer is not running the marker is kept for the next attempt. Idempotent; never throws.
    /// </summary>
    public static void Restore(ITaskbarSurface surface, ITaskbarSnapshotStore store, ILogger log)
    {
        try
        {
            var snapshot = store.TryRead(out bool corrupt);
            var windows = surface.GetWindows();
            if (snapshot != null)
            {
                if (windows.Count == 0)
                {
                    log.LogInformation("Explorer's taskbar is not running; keeping the taskbar marker for the next restore");
                    return;
                }

                if (snapshot.StateKnown && surface.TryGetState(out uint current) && current != snapshot.State && !surface.TrySetState(snapshot.State))
                {
                    log.LogWarning("Could not restore the taskbar state {State}", snapshot.State);
                }

                foreach (var w in windows)
                {
                    bool shouldBeVisible = w.IsPrimary ? snapshot.PrimaryVisible : snapshot.SecondaryVisible;
                    if (shouldBeVisible && !w.IsVisible && !surface.SetVisible(w.Handle, true))
                    {
                        log.LogWarning("Could not show taskbar window {Handle}", w.Handle);
                    }
                }

                store.Delete();
                return;
            }

            RepairHiddenTaskbar(surface, windows, log);
            if (corrupt)
            {
                store.Delete(); // unreadable marker: the heuristic above was all we could do
            }
        }
        catch (Exception ex)
        {
            log.LogWarning(ex, "Taskbar restore failed");
        }
    }

    private static void RepairHiddenTaskbar(ITaskbarSurface surface, IReadOnlyList<TaskbarWindowInfo> windows, ILogger log)
    {
        var primary = windows.FirstOrDefault(w => w.IsPrimary);
        if (primary.Handle == 0 || primary.IsVisible)
        {
            return;
        }

        log.LogInformation("The taskbar is hidden without a marker; making it visible again");
        foreach (var w in windows)
        {
            if (!w.IsVisible)
            {
                surface.SetVisible(w.Handle, true);
            }
        }

        if (surface.TryGetState(out uint state) && (state & Win32Consts.ABS_AUTOHIDE) != 0)
        {
            surface.TrySetState(state & ~Win32Consts.ABS_AUTOHIDE);
        }
    }
}
