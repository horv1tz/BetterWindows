using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Win32;
using Windows.Win32.System.Power;
using static Windows.Win32.PInvoke;

namespace BetterWindows.Native;

/// <summary>
/// Battery state from <c>Windows.System.Power.PowerManager</c> (WinRT, event driven) cross-checked with <c>GetSystemPowerStatus</c> (Win32). Presence and
/// AC state prefer the Win32 answer; WinRT fills what Win32 does not know. Reads are synchronous and cheap, so the first <see cref="Current"/> read is
/// already accurate; a 30 s cross-check timer covers missed events (sleep/resume, battery hot-swap).
/// </summary>
public sealed class PowerBatterySource : IBatterySource, IDisposable
{
    private static readonly TimeSpan CrossCheckPeriod = TimeSpan.FromSeconds(30);

    private readonly ILogger _log;
    private readonly StatusCell<BatteryState> _cell;
    private readonly object _gate = new();
    private readonly EventHandler<object> _onChanged;
    private bool _started;
    private bool _disposed;
    private System.Threading.Timer? _timer;

    public PowerBatterySource(ILogger<PowerBatterySource>? log = null)
    {
        _log = log ?? NullLogger<PowerBatterySource>.Instance;
        _cell = new StatusCell<BatteryState>(new BatteryState(), BatteryMapper.Same, _log);
        _onChanged = (_, _) => Refresh();
    }

    /// <inheritdoc />
    public BatteryState Current { get { EnsureStarted(); return _cell.Value; } }

    /// <inheritdoc />
    public event Action? Changed
    {
        add { _cell.Add(value); EnsureStarted(); }
        remove { _cell.Remove(value); }
    }

    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
            // WinRT static events: fired on a thread-pool thread; the handler only refreshes (cheap, never throws).
            try
            {
                Windows.System.Power.PowerManager.BatteryStatusChanged += _onChanged;
                Windows.System.Power.PowerManager.RemainingChargePercentChanged += _onChanged;
                Windows.System.Power.PowerManager.PowerSupplyStatusChanged += _onChanged;
                Windows.System.Power.PowerManager.RemainingDischargeTimeChanged += _onChanged;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "PowerManager events are unavailable; relying on the cross-check timer");
            }
            _timer = new System.Threading.Timer(_ => Refresh(), null, CrossCheckPeriod, CrossCheckPeriod);
        }
        Refresh();
    }

    private void Refresh()
    {
        if (_disposed) return;
        try
        {
            _cell.Publish(BatteryMapper.Map(ReadInputs()));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Battery refresh failed");
        }
    }

    private BatteryInputs ReadInputs()
    {
        // GetSystemPowerStatus returns BOOL; a failed call leaves the fields at 255 (unknown), which the mapper treats as "not available".
        var win32 = false;
        byte ac = 255, flag = 255, percent = 255;
        uint lifeSeconds = BatteryMapper.UnknownSeconds;
        if (GetSystemPowerStatus(out SYSTEM_POWER_STATUS status))
        {
            win32 = true;
            ac = status.ACLineStatus;
            flag = status.BatteryFlag;
            percent = status.BatteryLifePercent;
            lifeSeconds = status.BatteryLifeTime;
        }

        var winRt = false;
        int winRtStatus = 0, winRtPercent = -1, winRtSupply = 0;
        var winRtRemaining = TimeSpan.Zero;
        try
        {
            // Documented static properties; each read is a synchronous query of the power manager service.
            winRtStatus = (int)Windows.System.Power.PowerManager.BatteryStatus;
            winRtPercent = Windows.System.Power.PowerManager.RemainingChargePercent;
            winRtSupply = (int)Windows.System.Power.PowerManager.PowerSupplyStatus;
            winRtRemaining = Windows.System.Power.PowerManager.RemainingDischargeTime;
            winRt = true;
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "PowerManager is unavailable");
        }

        return new BatteryInputs(winRt, winRtStatus, winRtPercent, winRtSupply, winRtRemaining, win32, ac, flag, percent, lifeSeconds);
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_started)
            {
                try
                {
                    Windows.System.Power.PowerManager.BatteryStatusChanged -= _onChanged;
                    Windows.System.Power.PowerManager.RemainingChargePercentChanged -= _onChanged;
                    Windows.System.Power.PowerManager.PowerSupplyStatusChanged -= _onChanged;
                    Windows.System.Power.PowerManager.RemainingDischargeTimeChanged -= _onChanged;
                }
                catch (Exception ex)
                {
                    _log.LogDebug(ex, "Unsubscribing from PowerManager events failed");
                }
            }
            _timer?.Dispose();
            _timer = null;
        }
    }
}
