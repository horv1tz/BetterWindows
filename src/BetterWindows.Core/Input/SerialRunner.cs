using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Input;

/// <summary>
/// Runs a "reconcile with the latest state" action one at a time and collapses bursts: any number of triggers while a run is in flight cause exactly ONE more run afterwards.
/// Used for work that must not happen on the caller's thread (port events and settings callbacks return in &lt; 1 ms, docs/IPC.md section 13) but must converge
/// to the newest settings (hotkey re-registration, tray host start/stop). The action itself reads the latest state, so skipped intermediate triggers lose nothing.
/// </summary>
public sealed class SerialRunner
{
    private readonly Action _work;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private bool _running, _pending, _stopped;

    public SerialRunner(Action work, ILogger? log = null)
    {
        _work = work;
        _log = log;
    }

    /// <summary>Schedule a run on the thread pool (returns immediately).</summary>
    public void Trigger()
    {
        if (Arm()) _ = Task.Run(Drain);
    }

    /// <summary>Run now on the calling thread (used by Start() for the initial, deterministic pass). If another run is in flight the request is queued behind it and this returns immediately.</summary>
    public void RunInline()
    {
        if (Arm()) Drain();
    }

    /// <summary>No further runs start; a run already in flight finishes.</summary>
    public void Stop()
    {
        lock (_gate) _stopped = true;
    }

    /// <summary>True while a run is executing or queued (tests/diagnostics).</summary>
    public bool Busy { get { lock (_gate) return _running || _pending; } }

    private bool Arm()
    {
        lock (_gate)
        {
            if (_stopped) return false;
            _pending = true;
            if (_running) return false; // the active drain loop picks the request up
            _running = true;
            return true;
        }
    }

    private void Drain()
    {
        while (true)
        {
            lock (_gate)
            {
                if (!_pending || _stopped) { _pending = false; _running = false; return; }
                _pending = false;
            }
            try { _work(); }
            catch (Exception ex) { _log?.LogError(ex, "Background reconcile failed"); }
        }
    }
}
