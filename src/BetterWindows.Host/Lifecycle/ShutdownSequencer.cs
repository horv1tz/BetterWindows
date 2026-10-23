using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>One named step of the ordered shutdown. <paramref name="Run"/> may throw: the sequencer logs it and continues with the next step.</summary>
public sealed record ShutdownStep(string Name, Action Run);

/// <summary>What happened to one step.</summary>
public sealed record ShutdownStepResult(string Name, bool Succeeded, TimeSpan Elapsed, string? Error);

/// <summary>Outcome of a shutdown run.</summary>
public sealed record ShutdownResult(IReadOnlyList<ShutdownStepResult> Steps, bool TimedOut);

/// <summary>
/// Runs the shutdown steps in order, exactly once, on the calling thread (the WPF UI thread in production: surface windows must be closed there). Every step is isolated
/// (an exception is logged, the next step still runs). A watchdog armed at the beginning calls <c>onTimeout</c> if the whole sequence has not finished within
/// <see cref="DefaultWatchdog"/> (a hung controller, a blocked UI thread): the Host then force-restores through the static restorers and terminates the process, so a
/// stuck shutdown can never leave the taskbar hidden. Idempotent and thread-safe: later callers get the first run's result immediately (or wait for it).
/// </summary>
public sealed class ShutdownSequencer
{
    /// <summary>Hard limit for the whole sequence (docs/IPC.md section 15).</summary>
    public static readonly TimeSpan DefaultWatchdog = TimeSpan.FromSeconds(5);

    private readonly IReadOnlyList<ShutdownStep> _steps;
    private readonly Action _onTimeout;
    private readonly TimeProvider _time;
    private readonly TimeSpan _watchdog;
    private readonly ILogger? _log;
    private readonly object _gate = new();
    private readonly ManualResetEventSlim _done = new(false);
    private bool _started;
    private ShutdownResult? _result;

    public ShutdownSequencer(IReadOnlyList<ShutdownStep> steps, Action onTimeout, TimeProvider? time = null, TimeSpan? watchdog = null, ILogger? log = null)
    {
        _steps = steps;
        _onTimeout = onTimeout;
        _time = time ?? TimeProvider.System;
        _watchdog = watchdog ?? DefaultWatchdog;
        _log = log;
    }

    /// <summary>True once <see cref="Run"/> was called (the sequence may still be in progress).</summary>
    public bool Started { get { lock (_gate) return _started; } }
    /// <summary>True once every step finished (or the watchdog fired).</summary>
    public bool Completed => _done.IsSet;
    /// <summary>The result of the finished run, null while it is still running.</summary>
    public ShutdownResult? Result { get { lock (_gate) return _result; } }

    /// <summary>
    /// Executes the steps if this is the first call; otherwise waits up to <paramref name="joinTimeout"/> for the first run (null = do not wait) and returns its result
    /// (null if it has not finished).
    /// </summary>
    public ShutdownResult? Run(TimeSpan? joinTimeout = null)
    {
        bool first;
        lock (_gate)
        {
            first = !_started;
            _started = true;
        }
        if (!first)
        {
            if (joinTimeout is { } t && !_done.IsSet) _done.Wait(t);
            return Result;
        }

        var results = new List<ShutdownStepResult>(_steps.Count);
        var timedOut = false;
        using var timer = _time.CreateTimer(_ =>
        {
            if (_done.IsSet) return;
            timedOut = true;
            _log?.LogError("Shutdown watchdog fired after {Seconds} s; forcing restore and exit", _watchdog.TotalSeconds);
            try { _onTimeout(); } catch (Exception ex) { _log?.LogError(ex, "Shutdown watchdog action failed"); }
        }, null, _watchdog, Timeout.InfiniteTimeSpan);

        foreach (var step in _steps)
        {
            var sw = Stopwatch.StartNew();
            try
            {
                step.Run();
                results.Add(new ShutdownStepResult(step.Name, true, sw.Elapsed, null));
                _log?.LogDebug("Shutdown step {Step} done in {Ms} ms", step.Name, sw.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                results.Add(new ShutdownStepResult(step.Name, false, sw.Elapsed, ex.Message));
                _log?.LogError(ex, "Shutdown step {Step} failed", step.Name);
            }
        }

        lock (_gate) _result = new ShutdownResult(results, timedOut);
        _done.Set();
        return _result;
    }
}
