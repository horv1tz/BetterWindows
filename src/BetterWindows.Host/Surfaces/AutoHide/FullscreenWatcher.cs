using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Detects "a fullscreen app is in front on monitor M" for the monitors that carry a dock or menu bar. Event-driven (IWindowSystem.FullscreenChanged and the AppBar
/// fullscreen notification call <see cref="RequestEvaluation"/>) plus a 500 ms safety poll that exists only while <see cref="Start"/>ed (i.e. while surfaces exist):
/// the shell notifications are not reliable for every kind of fullscreen window, the poll is the backstop. The (cheap but not free) IsFullscreenWindowActive queries run
/// on the thread pool and only a CHANGED result is reported through the callback, which the caller marshals to the UI thread.
/// </summary>
internal sealed class FullscreenWatcher : IDisposable
{
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(500);
    public static readonly TimeSpan EventDebounce = TimeSpan.FromMilliseconds(40);

    private readonly IWindowSystem _windows;
    private readonly TimeProvider _time;
    private readonly Func<IReadOnlyList<MonitorInfo>> _monitors;
    private readonly Action<IReadOnlyCollection<string>> _onChanged;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private ITimer? _poll;
    private ITimer? _debounce;
    private int _evaluating;
    private int _rerun;
    private string _last = "";
    private bool _disposed;

    public FullscreenWatcher(IWindowSystem windows, TimeProvider time, Func<IReadOnlyList<MonitorInfo>> monitors, Action<IReadOnlyCollection<string>> onChanged, ILogger? log = null)
    {
        _windows = windows;
        _time = time;
        _monitors = monitors;
        _onChanged = onChanged;
        _log = log ?? NullLogger.Instance;
    }

    public bool IsRunning { get { lock (_gate) return _poll is not null; } }

    public void Start()
    {
        lock (_gate)
        {
            if (_disposed || _poll is not null) return;
            _poll = _time.CreateTimer(_ => EvaluateAsync(), null, PollInterval, PollInterval);
        }
        EvaluateAsync();
    }

    /// <summary>Stops the safety poll (no surfaces left). The owner lifts the suppressions itself.</summary>
    public void Stop()
    {
        lock (_gate)
        {
            _poll?.Dispose();
            _poll = null;
            _debounce?.Dispose();
            _debounce = null;
        }
    }

    /// <summary>Called from OS threads (hook thread, WndProc): returns immediately; evaluation is debounced and runs on the thread pool.</summary>
    public void RequestEvaluation()
    {
        lock (_gate)
        {
            if (_disposed || _poll is null) return;
            _debounce ??= _time.CreateTimer(_ => { lock (_gate) { _debounce?.Dispose(); _debounce = null; } EvaluateAsync(); }, null, EventDebounce, Timeout.InfiniteTimeSpan);
        }
    }

    private void EvaluateAsync()
    {
        // One evaluation at a time; a request arriving meanwhile triggers exactly one more run.
        if (Interlocked.Exchange(ref _evaluating, 1) == 1) { Volatile.Write(ref _rerun, 1); return; }
        _ = Task.Run(() =>
        {
            try
            {
                do
                {
                    Volatile.Write(ref _rerun, 0);
                    Evaluate();
                }
                while (Volatile.Read(ref _rerun) == 1);
            }
            catch (Exception ex) { _log.LogDebug(ex, "fullscreen evaluation failed"); }
            finally { Volatile.Write(ref _evaluating, 0); }
        });
    }

    /// <summary>Synchronous evaluation (tests call it directly).</summary>
    public void Evaluate()
    {
        IReadOnlyList<MonitorInfo> monitors;
        lock (_gate) { if (_disposed) return; }
        try { monitors = _monitors(); }
        catch { return; }
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var m in monitors)
        {
            try { if (_windows.IsFullscreenWindowActive(m)) set.Add(m.Id); }
            catch (Exception ex) { _log.LogDebug(ex, "IsFullscreenWindowActive failed for {Monitor}", m.Id); }
        }
        var key = string.Join('|', set);
        lock (_gate)
        {
            if (_disposed || key == _last) return;
            _last = key;
        }
        try { _onChanged(set.ToArray()); }
        catch (Exception ex) { _log.LogError(ex, "fullscreen change handler failed"); }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            _disposed = true;
            _poll?.Dispose();
            _debounce?.Dispose();
            _poll = null;
            _debounce = null;
        }
    }
}
