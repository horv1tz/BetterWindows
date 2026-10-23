using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Devices.Enumeration;
using Windows.Devices.Radios;
using Windows.Foundation;

namespace BetterWindows.Native;

/// <summary>
/// Bluetooth radio state and paired devices. The radio comes from <c>Radio.GetRadiosAsync</c> (after <c>Radio.RequestAccessAsync</c>); paired devices come
/// from <c>DeviceInformation.FindAllAsync</c> over the classic and LE pairing selectors, enriched per device by <c>BluetoothDevice.ConnectionStatus</c>
/// (falls back to the AEP IsConnected property) and the DEVPKEY battery property when the device exposes one. A <see cref="DeviceWatcher"/> pair
/// and the radio's StateChanged event trigger a debounced refresh. Every WinRT call has a timeout; a machine without Bluetooth reports
/// Supported = false.
/// </summary>
public sealed class BluetoothSource : IBluetoothSource, IDisposable
{
    private const string ConnectedProperty = "System.Devices.Aep.IsConnected";
    private const string BatteryProperty = "{104EA319-6EE2-4701-BD47-8DDBF425BBE5} 2";
    private static readonly string[] DeviceProperties = { ConnectedProperty, BatteryProperty };

    private readonly ILogger _log;
    private readonly StatusCell<BluetoothState> _cell;
    private readonly StatusDebouncer _debouncer;
    private readonly SemaphoreSlim _refreshGate = new(1, 1);
    private readonly object _gate = new();
    private readonly List<DeviceWatcher> _watchers = new();
    private readonly TypedEventHandler<DeviceWatcher, DeviceInformation> _onAdded;
    private readonly TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> _onUpdated;
    private readonly TypedEventHandler<DeviceWatcher, DeviceInformationUpdate> _onRemoved;
    private readonly TypedEventHandler<Radio, object> _onRadioState;
    private bool _started;
    private bool _disposed;
    private Radio? _radio;

    public BluetoothSource(ILogger<BluetoothSource>? log = null)
    {
        _log = log ?? NullLogger<BluetoothSource>.Instance;
        _cell = new StatusCell<BluetoothState>(new BluetoothState(), BluetoothMapper.Same, _log);
        _debouncer = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(300), TimeSpan.FromSeconds(2),
            () => _ = RefreshAsync(), ex => _log.LogWarning(ex, "Bluetooth refresh trigger failed"));
        _onAdded = (_, _) => _debouncer.Trigger();
        _onUpdated = (_, _) => _debouncer.Trigger();
        _onRemoved = (_, _) => _debouncer.Trigger();
        _onRadioState = (_, _) => _debouncer.Trigger();
    }

    /// <inheritdoc />
    public BluetoothState Current { get { EnsureStarted(); return _cell.Value; } }

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
            try
            {
                // The classic and the LE pairing selectors are separate watchers: a device is found by exactly one of them.
                var selectors = new[]
                {
                    Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                    Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                };
                foreach (var selector in selectors)
                {
                    try
                    {
                        var watcher = DeviceInformation.CreateWatcher(selector, DeviceProperties);
                        watcher.Added += _onAdded;
                        watcher.Updated += _onUpdated;
                        watcher.Removed += _onRemoved;
                        watcher.Start();
                        _watchers.Add(watcher);
                    }
                    catch (Exception ex)
                    {
                        _log.LogWarning(ex, "Bluetooth device watcher could not start");
                    }
                }
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Bluetooth selectors are unavailable");
            }
        }
        // First read right away (not debounced) so the first Current read is not stale for long.
        _ = RefreshAsync();
    }

    private async Task RefreshAsync()
    {
        if (_disposed) return;
        await _refreshGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (_disposed) return;
            _cell.Publish(await ReadAsync().ConfigureAwait(false));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Bluetooth refresh failed");
        }
        finally
        {
            _refreshGate.Release();
        }
    }

    private async Task<BluetoothState> ReadAsync()
    {
        var unsupported = new BluetoothState { Supported = false, Enabled = false, Devices = Array.Empty<BluetoothDevice>() };

        var access = await StatusAsync.TryAsync(Radio.RequestAccessAsync(), StatusAsync.DefaultTimeout, _log, "Radio access request").ConfigureAwait(false);
        if (access != RadioAccessStatus.Allowed) return unsupported;

        var radios = await StatusAsync.TryAsync(Radio.GetRadiosAsync(), StatusAsync.DefaultTimeout, _log, "Radio enumeration").ConfigureAwait(false);
        var radio = radios?.FirstOrDefault(r => r.Kind == RadioKind.Bluetooth);
        if (radio is null) return unsupported;
        AttachRadio(radio);

        var enabled = radio.State == RadioState.On;
        var devices = await ReadDevicesAsync().ConfigureAwait(false);
        return new BluetoothState { Supported = true, Enabled = enabled, Devices = devices };
    }

    private void AttachRadio(Radio radio)
    {
        lock (_gate)
        {
            if (ReferenceEquals(_radio, radio)) return;
            if (_radio is not null)
            {
                try { _radio.StateChanged -= _onRadioState; } catch (Exception ex) { _log.LogDebug(ex, "Detaching the radio handler failed"); }
            }
            _radio = radio;
            try { radio.StateChanged += _onRadioState; }
            catch (Exception ex) { _log.LogDebug(ex, "Attaching the radio handler failed"); }
        }
    }

    private async Task<IReadOnlyList<BluetoothDevice>> ReadDevicesAsync()
    {
        var found = new List<BluetoothDevice>();
        foreach (var selector in new[]
                 {
                     Windows.Devices.Bluetooth.BluetoothDevice.GetDeviceSelectorFromPairingState(true),
                     Windows.Devices.Bluetooth.BluetoothLEDevice.GetDeviceSelectorFromPairingState(true),
                 })
        {
            var infos = await StatusAsync.TryAsync(DeviceInformation.FindAllAsync(selector, DeviceProperties), StatusAsync.DefaultTimeout, _log, "Paired device enumeration")
                .ConfigureAwait(false);
            if (infos is null) continue;
            foreach (var info in infos)
            {
                if (found.Count >= BluetoothMapper.MaxDevices) return found;
                if (found.Any(d => d.Id == info.Id)) continue;
                found.Add(await ToDeviceAsync(info).ConfigureAwait(false));
            }
        }
        return found;
    }

    private async Task<BluetoothDevice> ToDeviceAsync(DeviceInformation info)
    {
        var connected = info.Properties.TryGetValue(ConnectedProperty, out var aep) && aep is true;
        var battery = info.Properties.TryGetValue(BatteryProperty, out var raw) ? BluetoothMapper.ParseBatteryPercent(raw) : null;

        // BluetoothDevice.ConnectionStatus is the authoritative answer; the AEP property is only the fallback when the device object cannot be opened.
        var status = await ConnectionStatusAsync(info.Id).ConfigureAwait(false);
        if (status is not null) connected = status.Value;

        return new BluetoothDevice
        {
            Id = info.Id,
            Name = string.IsNullOrWhiteSpace(info.Name) ? info.Id : info.Name,
            Connected = connected,
            BatteryPercent = battery,
        };
    }

    private async Task<bool?> ConnectionStatusAsync(string deviceId)
    {
        // Classic devices use BTHENUM# ids, LE devices BTHLE# ids. Opening the device object does not connect it.
        if (deviceId.StartsWith("BTHLE", StringComparison.OrdinalIgnoreCase))
        {
            var le = await StatusAsync.TryAsync(Windows.Devices.Bluetooth.BluetoothLEDevice.FromIdAsync(deviceId), StatusAsync.DefaultTimeout, _log, "LE device open")
                .ConfigureAwait(false);
            if (le is null) return null;
            try { return le.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected; }
            finally { le.Dispose(); }
        }
        var classic = await StatusAsync.TryAsync(Windows.Devices.Bluetooth.BluetoothDevice.FromIdAsync(deviceId), StatusAsync.DefaultTimeout, _log, "Device open")
            .ConfigureAwait(false);
        if (classic is null) return null;
        try { return classic.ConnectionStatus == Windows.Devices.Bluetooth.BluetoothConnectionStatus.Connected; }
        finally { classic.Dispose(); }
    }

    public void Dispose()
    {
        List<DeviceWatcher> watchers;
        Radio? radio;
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            watchers = new List<DeviceWatcher>(_watchers);
            _watchers.Clear();
            radio = _radio;
            _radio = null;
        }
        foreach (var w in watchers)
        {
            try
            {
                w.Added -= _onAdded;
                w.Updated -= _onUpdated;
                w.Removed -= _onRemoved;
                w.Stop();
            }
            catch (Exception ex)
            {
                _log.LogDebug(ex, "Stopping a Bluetooth watcher failed");
            }
        }
        if (radio is not null)
        {
            try { radio.StateChanged -= _onRadioState; } catch (Exception ex) { _log.LogDebug(ex, "Detaching the radio handler failed"); }
        }
        _debouncer.Dispose();
    }
}
