using BetterWindows.Core.Input;
using BetterWindows.Core.Overlay;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Hot corners: samples the pointer every 40 ms (a <see cref="TimeProvider"/> timer, no global mouse hook) and feeds <see cref="HotCornerDetector"/>. The timer exists ONLY while
/// settings.hotCorners.enabled is on and at least one corner has an action, so an idle shell costs nothing. A trigger executes <see cref="ShellActions"/> on the monitor that
/// owns the corner. Skipped ticks: pointer unavailable (secure desktop / lock screen: the dwell state is reset), previous tick still running. Fullscreen suppression asks
/// <see cref="IWindowSystem.IsFullscreenWindowActive"/> for the corner's monitor, but only at the moment a corner is about to fire.
/// </summary>
public sealed class HotCornerController : IShellController
{
    /// <summary>Pointer sampling period while hot corners are active.</summary>
    public static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(40);

    private readonly ISettingsService _settings;
    private readonly ICursorProvider _cursor;
    private readonly IMonitorProvider _monitors;
    private readonly IWindowSystem _windows;
    private readonly TimeProvider _time;
    private readonly ShellActions _actions;
    private readonly ILogger _log;
    private readonly HotCornerDetector _detector = new();   // touched ONLY by Tick (guarded by _ticking), never by other threads
    private readonly object _gate = new();                   // guards _timer; never held across a port call
    private ITimer? _timer;
    private Action<Settings>? _onSettings;
    private Action? _onMonitors;
    private long _epoch;
    private int _ticking;
    private volatile bool _started, _stopped, _active, _monitorsDirty = true, _resetRequested;
    private IReadOnlyList<MonitorInfo> _monitorInfos = Array.Empty<MonitorInfo>();
    private IReadOnlyList<HotCornerMonitor> _layout = Array.Empty<HotCornerMonitor>();

    public HotCornerController(ISettingsService settings, ICursorProvider cursor, IMonitorProvider monitors, IWindowSystem windows, TimeProvider time,
        IServiceProvider services, ILogger<HotCornerController>? log = null)
    {
        _settings = settings;
        _cursor = cursor;
        _monitors = monitors;
        _windows = windows;
        _time = time;
        _actions = ShellActions.Create(services);
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "HotCornerController";

    /// <summary>True while the polling timer runs (tests/diagnostics).</summary>
    public bool IsPolling { get { lock (_gate) return _timer is not null; } }

    public void Start()
    {
        if (_started) return;
        _started = true;
        _epoch = _time.GetTimestamp();
        _onSettings = _ => Reconcile();
        _settings.Changed += _onSettings;
        _onMonitors = () => _monitorsDirty = true;
        _monitors.MonitorsChanged += _onMonitors;
        Reconcile();
    }

    public void Stop()
    {
        _stopped = true;
        _active = false;
        if (_onSettings is not null) _settings.Changed -= _onSettings;
        if (_onMonitors is not null) _monitors.MonitorsChanged -= _onMonitors;
        _onSettings = null;
        _onMonitors = null;
        lock (_gate)
        {
            _timer?.Dispose();
            _timer = null;
        }
    }

    /// <summary>Start or stop polling to match the CURRENT settings (always reads Current, so racing events converge).</summary>
    private void Reconcile()
    {
        var hc = _settings.Current.HotCorners;
        var wanted = hc.Enabled && HotCornerActions.From(hc).AnyAssigned;
        lock (_gate)
        {
            if (_stopped) return;
            if (wanted && _timer is null)
            {
                _resetRequested = true;
                _active = true;
                _timer = _time.CreateTimer(_ => Tick(), null, PollInterval, PollInterval);
            }
            else if (!wanted && _timer is not null)
            {
                _active = false;
                _resetRequested = true;
                _timer.Dispose();
                _timer = null;
            }
        }
    }

    private void Tick()
    {
        if (!_active || Interlocked.Exchange(ref _ticking, 1) == 1) return; // a slow tick must not overlap the next one
        try
        {
            if (_resetRequested) { _resetRequested = false; _detector.Reset(); }

            if (!_cursor.TryGetCursorPosition(out var pointer))
            {
                _detector.Reset(); // secure desktop / lock screen: do not let a dwell survive the gap
                return;
            }

            RefreshMonitorsIfNeeded();
            var hc = _settings.Current.HotCorners;
            var dwell = TimeSpan.FromMilliseconds(Math.Clamp(hc.DwellMs, 50, 2000));
            var now = _time.GetElapsedTime(_epoch);
            var trigger = _detector.Update(now, pointer, _cursor.IsAnyMouseButtonDown(), _layout, HotCornerActions.From(hc), dwell, FullscreenOn);
            if (trigger is not { } fired || !_active) return;

            _log.LogDebug("Hot corner {Corner} on {Monitor}: {Action}", fired.Corner, fired.MonitorId, fired.Action);
            _actions.Execute(fired.Action, fired.MonitorId);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Hot corner tick failed"); }
        finally { Interlocked.Exchange(ref _ticking, 0); }
    }

    private bool FullscreenOn(string monitorId)
    {
        var monitor = _monitorInfos.FirstOrDefault(m => m.Id == monitorId);
        if (monitor is null) return false;
        try { return _windows.IsFullscreenWindowActive(monitor); }
        catch (Exception ex) { _log.LogDebug(ex, "Fullscreen query failed"); return false; }
    }

    private void RefreshMonitorsIfNeeded()
    {
        if (!_monitorsDirty && _layout.Count > 0) return;
        _monitorsDirty = false; // cleared first: a MonitorsChanged raised while we read sets it again
        var infos = _monitors.GetMonitors();
        _monitorInfos = infos;
        _layout = infos.Select(m => new HotCornerMonitor(m.Id, ToRect(m.BoundsPx))).ToArray();
        _detector.Reset();
    }

    private static NativeRect ToRect(Rect r)
    {
        var left = (int)Math.Round(r.X);
        var top = (int)Math.Round(r.Y);
        return new NativeRect(left, top, left + (int)Math.Round(r.Width), top + (int)Math.Round(r.Height));
    }
}
