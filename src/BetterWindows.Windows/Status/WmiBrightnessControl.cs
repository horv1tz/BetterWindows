using System.Management;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native;

/// <summary>
/// Built-in panel brightness through the WMI classes in <c>root\wmi</c> (<c>WmiMonitorBrightness</c> for reading, the
/// <c>WmiMonitorBrightnessMethods.WmiSetBrightness</c> method for writing). Supported only when an ACTIVE instance exists (laptop panels). External
/// monitors are not supported here (DDC/CI is future work). Changes are detected through <c>WmiMonitorBrightnessEvent</c> with a 5 s polling fallback;
/// both run only while someone subscribed to <see cref="Changed"/>.
/// </summary>
public sealed class WmiBrightnessControl : IBrightnessControl, IDisposable
{
    private static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(5);
    private const string Scope = @"root\wmi";

    private readonly ILogger _log;
    private readonly StatusCell<BrightnessState> _cell;
    private readonly object _gate = new();
    private readonly EventArrivedEventHandler _onEvent;
    private ManagementEventWatcher? _watcher;
    private System.Threading.Timer? _poll;
    private bool _started;
    private bool _disposed;

    public WmiBrightnessControl(ILogger<WmiBrightnessControl>? log = null)
    {
        _log = log ?? NullLogger<WmiBrightnessControl>.Instance;
        _cell = new StatusCell<BrightnessState>(new BrightnessState(), BrightnessMath.Same, _log);
        _onEvent = (_, _) => Refresh();
    }

    /// <inheritdoc />
    public BrightnessState Current
    {
        get
        {
            EnsureStarted();
            return _cell.Value;
        }
    }

    /// <inheritdoc />
    public event Action? Changed
    {
        add
        {
            _cell.Add(value);
            EnsureStarted();
            UpdateWatching();
        }
        remove
        {
            _cell.Remove(value);
            UpdateWatching();
        }
    }

    /// <inheritdoc />
    public void SetLevel(double level01)
    {
        if (_disposed) return;
        var percent = BrightnessMath.ToPercent(level01);
        try
        {
            if (!TryInvokeSet(percent))
            {
                _log.LogDebug("Brightness is not controllable on this display");
                return;
            }
            _log.LogDebug("Brightness set to {Percent}%", percent);
            var current = _cell.Value;
            _cell.Publish(new BrightnessState { Supported = current.Supported, Level = BrightnessMath.FromPercent(percent) });
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Setting brightness failed");
        }
    }

    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        // WMI queries take milliseconds: the first read runs on the thread pool so Current never blocks its caller.
        _ = Task.Run(Refresh);
    }

    /// <summary>Starts or stops the change watcher and the poll timer according to the subscriber count.</summary>
    private void UpdateWatching()
    {
        lock (_gate)
        {
            if (_disposed) return;
            if (_cell.SubscriberCount > 0)
            {
                if (_watcher is null)
                {
                    try
                    {
                        var watcher = new ManagementEventWatcher(new ManagementScope(Scope), new EventQuery("SELECT * FROM WmiMonitorBrightnessEvent"));
                        watcher.EventArrived += _onEvent;
                        watcher.Start();
                        _watcher = watcher;
                    }
                    catch (Exception ex)
                    {
                        // Not every driver raises the event; the poll below covers that case.
                        _log.LogDebug(ex, "WmiMonitorBrightnessEvent is unavailable; polling only");
                    }
                }
                _poll ??= new System.Threading.Timer(_ => Refresh(), null, PollPeriod, PollPeriod);
            }
            else
            {
                if (_watcher is not null)
                {
                    try
                    {
                        _watcher.EventArrived -= _onEvent;
                        _watcher.Stop();
                    }
                    catch (Exception ex)
                    {
                        _log.LogDebug(ex, "Stopping the brightness event watcher failed");
                    }
                    _watcher.Dispose();
                    _watcher = null;
                }
                _poll?.Dispose();
                _poll = null;
            }
        }
    }

    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            _cell.Publish(Read());
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Reading brightness failed");
        }
    }

    private static BrightnessState Read()
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(Scope), new ObjectQuery("SELECT * FROM WmiMonitorBrightness"));
        using var results = searcher.Get();
        foreach (ManagementObject mo in results)
        {
            using (mo)
            {
                if (mo["Active"] is not bool active || !active) continue;
                if (mo["CurrentBrightness"] is byte percent)
                    return new BrightnessState { Supported = true, Level = BrightnessMath.FromPercent(percent) };
            }
        }
        return new BrightnessState { Supported = false, Level = 0.0 };
    }

    private static bool TryInvokeSet(int percent)
    {
        using var searcher = new ManagementObjectSearcher(new ManagementScope(Scope), new ObjectQuery("SELECT * FROM WmiMonitorBrightnessMethods"));
        using var results = searcher.Get();
        foreach (ManagementObject mo in results)
        {
            using (mo)
            {
                if (mo["Active"] is not bool active || !active) continue;
                // WmiSetBrightness(Timeout uint32 seconds, Brightness uint8 percent): the timeout is how long the setting may take to apply.
                mo.InvokeMethod("WmiSetBrightness", new object[] { (uint)1, (byte)percent });
                return true;
            }
        }
        return false;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        lock (_gate)
        {
            _watcher?.Dispose();
            _watcher = null;
            _poll?.Dispose();
            _poll = null;
        }
    }
}
