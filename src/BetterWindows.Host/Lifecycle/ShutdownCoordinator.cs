using BetterWindows.Host.Bridge;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Host.Lifecycle;

/// <summary>
/// Entry point of every shutdown path (system.quit, tray, Restart, SessionEnding, fatal errors). <see cref="RequestExit"/> NEVER blocks: it records the request, waits a
/// short grace so the reply of the call that triggered it can still be delivered, then runs the <see cref="ShutdownSequencer"/> on the UI thread. A fail-safe timer
/// force-restores and terminates the process if the UI thread never gets to run the sequence at all. Idempotent: the first request wins.
/// </summary>
public sealed class ShutdownCoordinator
{
    /// <summary>Delay between the request and the start of the sequence (lets the in-flight call result be posted).</summary>
    public static readonly TimeSpan ReplyGrace = TimeSpan.FromMilliseconds(100);
    /// <summary>If the sequence has not even started after this long (UI thread blocked), the process force-restores and exits.</summary>
    public static readonly TimeSpan FailSafe = TimeSpan.FromSeconds(10);

    private readonly IUiScheduler _ui;
    private readonly TimeProvider _time;
    private readonly ILogger<ShutdownCoordinator> _log;
    private readonly object _gate = new();
    private Func<bool, IReadOnlyList<ShutdownStep>>? _buildSteps;
    private Action? _forceExit;
    private ShutdownSequencer? _sequencer;
    private ITimer? _failSafe;
    private int _requested;
    private volatile bool _restart;
    private int _exitCode;

    public ShutdownCoordinator(IUiScheduler ui, TimeProvider time, ILogger<ShutdownCoordinator> log)
    {
        _ui = ui;
        _time = time;
        _log = log;
    }

    /// <summary>True once any shutdown was requested or started.</summary>
    public bool IsShuttingDown => Volatile.Read(ref _requested) != 0;
    /// <summary>The request asked for a relaunch after the restore (Restart).</summary>
    public bool RestartRequested => _restart;
    /// <summary>Process exit code the final step passes to <c>Application.Shutdown</c>.</summary>
    public int ExitCode => Volatile.Read(ref _exitCode);

    /// <summary>Wires the step list (built lazily at shutdown time from whatever exists then) and the last-resort action (static restorers + terminate).</summary>
    public void Configure(Func<bool, IReadOnlyList<ShutdownStep>> buildSteps, Action forceRestoreAndExit)
    {
        lock (_gate)
        {
            _buildSteps = buildSteps;
            _forceExit = forceRestoreAndExit;
        }
    }

    /// <summary>Asks for the ordered shutdown and returns immediately.</summary>
    public void RequestExit(bool restart, int exitCode = 0)
    {
        if (Interlocked.Exchange(ref _requested, 1) != 0) return;
        _restart = restart;
        Volatile.Write(ref _exitCode, exitCode);
        _log.LogInformation("Shutdown requested (restart: {Restart}, exit code {Code})", restart, exitCode);
        lock (_gate) _failSafe = _time.CreateTimer(_ => OnFailSafe(), null, FailSafe, Timeout.InfiniteTimeSpan);
        _ = Task.Delay(ReplyGrace, _time).ContinueWith(_ => _ui.Post(() => RunNow()), CancellationToken.None, TaskContinuationOptions.None, TaskScheduler.Default);
    }

    /// <summary>
    /// Runs (or joins) the sequence on the CALLING thread. The posted request uses it on the UI thread; WPF's SessionEnding handler calls it directly because Windows
    /// terminates the process right after that handler returns. <paramref name="join"/>: how long to wait when another thread is already running it.
    /// </summary>
    public ShutdownResult? RunNow(TimeSpan? join = null)
    {
        Interlocked.Exchange(ref _requested, 1);
        ShutdownSequencer seq;
        lock (_gate)
        {
            _sequencer ??= new ShutdownSequencer(_buildSteps?.Invoke(_restart) ?? Array.Empty<ShutdownStep>(), () => _forceExit?.Invoke(), _time, log: _log);
            seq = _sequencer;
        }
        var result = seq.Run(join);
        lock (_gate) { _failSafe?.Dispose(); _failSafe = null; }
        return result;
    }

    private void OnFailSafe()
    {
        bool started;
        lock (_gate) started = _sequencer?.Started ?? false;
        if (started) return;
        _log.LogError("Shutdown did not start within {Seconds} s (UI thread blocked?); forcing restore and exit", FailSafe.TotalSeconds);
        try { _forceExit?.Invoke(); } catch (Exception ex) { _log.LogError(ex, "Fail-safe exit failed"); }
    }
}
