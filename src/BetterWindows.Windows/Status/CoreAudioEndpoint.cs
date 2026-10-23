using System.Runtime.InteropServices;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using NAudio.CoreAudioApi;
using NAudio.CoreAudioApi.Interfaces;

namespace BetterWindows.Native;

/// <summary>
/// Default playback endpoint through NAudio: master volume, mute, the active render device list and default-device switching.
/// <para>
/// All COM objects live on one <see cref="StatusComWorker"/> thread (NAudio's MMDevice tree and the IMMNotificationClient callbacks). Volume notifications,
/// endpoint add/remove and default-device changes are debounced and re-read on that thread; <see cref="Current"/> is a lock-free cache read.
/// Default-device switching uses the undocumented IPolicyConfig and is reported through <see cref="CanSetDefaultDevice"/>: false when the
/// interface cannot be bound or the feature gate is off, and <see cref="SetDefaultDevice"/> then returns false instead of throwing.
/// </para>
/// </summary>
public sealed class CoreAudioEndpoint : IAudioEndpoint, IDisposable
{
    private static readonly TimeSpan BindTimeout = TimeSpan.FromSeconds(2);
    private static readonly TimeSpan SetTimeout = TimeSpan.FromSeconds(3);

    private readonly ILogger _log;
    private readonly StatusCell<AudioState> _cell;
    private readonly StatusComWorker _worker;
    private readonly StatusDebouncer _debouncer;
    private readonly AudioEndpointVolumeNotificationDelegate _onVolume;
    private readonly object _gate = new();
    private bool _started;
    private volatile bool _disposed;

    // COM state: read and written only on the worker thread.
    private MMDeviceEnumerator? _enumerator;
    private NotificationClient? _notify;
    private AudioEndpointVolume? _volume;
    private string? _volumeDeviceId;
    private bool _policyProbed;
    private volatile bool _canSetDefault;
    private IStatusPolicyConfig? _policy;

    public CoreAudioEndpoint(ILogger<CoreAudioEndpoint>? log = null)
    {
        _log = log ?? NullLogger<CoreAudioEndpoint>.Instance;
        _cell = new StatusCell<AudioState>(new AudioState(), AudioMapper.Same, _log);
        _worker = new StatusComWorker(_log);
        _debouncer = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(120),
            () => _worker.Post(RefreshOnWorker), ex => _log.LogWarning(ex, "Audio refresh failed"));
        _onVolume = _ => _debouncer.Trigger();
    }

    /// <inheritdoc />
    public AudioState Current { get { EnsureStarted(); return _cell.Value; } }

    /// <inheritdoc />
    public event Action? Changed
    {
        add { _cell.Add(value); EnsureStarted(); }
        remove { _cell.Remove(value); }
    }

    /// <inheritdoc />
    public bool CanSetDefaultDevice
    {
        get
        {
            EnsureStarted();
            // The first probe binds IPolicyConfig on the worker; the wait is bounded and happens only on this (router) thread, never on the UI thread.
            return _worker.Invoke(ProbePolicy, BindTimeout, false);
        }
    }

    /// <inheritdoc />
    public void SetVolume(double volume01)
    {
        EnsureStarted();
        var v = (float)AudioMapper.ClampVolume(volume01);
        _worker.Post(() =>
        {
            try
            {
                var volume = DefaultVolume();
                if (volume is not null) volume.MasterVolumeLevelScalar = v;
                _debouncer.Trigger();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Setting the master volume failed");
            }
        });
    }

    /// <inheritdoc />
    public void SetMuted(bool muted)
    {
        EnsureStarted();
        _worker.Post(() =>
        {
            try
            {
                var volume = DefaultVolume();
                if (volume is not null) volume.Mute = muted;
                _debouncer.Trigger();
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "Setting mute failed");
            }
        });
    }

    /// <inheritdoc />
    public bool SetDefaultDevice(string deviceId)
    {
        // Device ids are COM strings of a few hundred characters at most: anything longer or empty is refused before it reaches COM.
        if (string.IsNullOrEmpty(deviceId) || deviceId.Length > 1024) return false;
        if (!CanSetDefaultDevice) return false;
        return _worker.Invoke(() => SetDefaultOnWorker(deviceId), SetTimeout, false);
    }

    private void EnsureStarted()
    {
        lock (_gate)
        {
            if (_started || _disposed) return;
            _started = true;
        }
        _worker.Post(RefreshOnWorker);
    }

    // ---- worker thread only -------------------------------------------------------------------------------------------------------------------------

    private void RefreshOnWorker()
    {
        if (_disposed) return;
        try
        {
            var enumerator = EnsureEnumerator();
            if (enumerator is null)
            {
                _cell.Publish(AudioMapper.Build(Array.Empty<(string, string)>(), null, null, null));
                return;
            }

            var endpoints = ListActiveEndpoints(enumerator);
            string? defaultId = null;
            try
            {
                using var defaultDevice = enumerator.GetDefaultAudioEndpoint(DataFlow.Render, Role.Multimedia);
                defaultId = defaultDevice.ID;
                AttachVolume(defaultDevice);
            }
            catch (Exception ex) when (ex is COMException or InvalidOperationException)
            {
                // No active render device (all disabled or unplugged): an empty default is a valid state, not an error.
                DetachVolume();
                _log.LogDebug(ex, "No default render device");
            }

            double? volume = null;
            bool? muted = null;
            if (_volume is not null)
            {
                try
                {
                    volume = _volume.MasterVolumeLevelScalar;
                    muted = _volume.Mute;
                }
                catch (Exception ex) when (ex is COMException or InvalidOperationException)
                {
                    // The endpoint vanished between the calls; the next refresh re-attaches.
                    DetachVolume();
                    _log.LogDebug(ex, "Reading the master volume failed");
                }
            }
            _cell.Publish(AudioMapper.Build(endpoints, defaultId, volume, muted));
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Reading the audio state failed");
        }
    }

    private MMDeviceEnumerator? EnsureEnumerator()
    {
        if (_enumerator is not null) return _enumerator;
        try
        {
            _enumerator = new MMDeviceEnumerator();
            _notify = new NotificationClient(() => _debouncer.Trigger(), _log);
            // Registration is what makes default-device and endpoint changes arrive without polling.
            _enumerator.RegisterEndpointNotificationCallback(_notify);
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "The audio device enumerator could not be created");
            _enumerator?.Dispose();
            _enumerator = null;
            _notify = null;
        }
        return _enumerator;
    }

    private static List<(string Id, string Name)> ListActiveEndpoints(MMDeviceEnumerator enumerator)
    {
        var list = new List<(string Id, string Name)>();
        var devices = enumerator.EnumerateAudioEndPoints(DataFlow.Render, DeviceState.Active);
        for (var i = 0; i < devices.Count; i++)
        {
            using var device = devices[i];
            list.Add((device.ID, device.FriendlyName));
        }
        return list;
    }

    private void AttachVolume(MMDevice device)
    {
        if (_volume is not null && string.Equals(_volumeDeviceId, device.ID, StringComparison.Ordinal)) return;
        DetachVolume();
        // The AudioEndpointVolume object keeps its own COM reference, so disposing the MMDevice afterwards is safe.
        _volume = device.AudioEndpointVolume;
        _volume.OnVolumeNotification += _onVolume;
        _volumeDeviceId = device.ID;
    }

    private void DetachVolume()
    {
        if (_volume is null) return;
        try { _volume.OnVolumeNotification -= _onVolume; }
        catch (Exception ex) { _log.LogDebug(ex, "Detaching the volume callback failed"); }
        try { _volume.Dispose(); }
        catch (Exception ex) { _log.LogDebug(ex, "Releasing the endpoint volume failed"); }
        _volume = null;
        _volumeDeviceId = null;
    }

    private AudioEndpointVolume? DefaultVolume()
    {
        if (_volume is null) RefreshOnWorker();
        return _volume;
    }

    private bool ProbePolicy()
    {
        if (_policyProbed) return _canSetDefault;
        _policyProbed = true;
        if (!StatusFeatures.UndocumentedDefaultDevice) return false;
        try
        {
            _policy = StatusPolicyConfigClass.Create();
            _canSetDefault = true;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "The undocumented IPolicyConfig interface is unavailable; default device switching is disabled");
        }
        return _canSetDefault;
    }

    private bool SetDefaultOnWorker(string deviceId)
    {
        if (_disposed || !_policyProbed) return false;
        try
        {
            var enumerator = EnsureEnumerator();
            if (enumerator is null || _policy is null) return false;
            // Only an endpoint that is active right now can become the default: the id must come from this enumeration.
            if (!ListActiveEndpoints(enumerator).Any(e => string.Equals(e.Id, deviceId, StringComparison.Ordinal))) return false;

            var ok = true;
            foreach (var role in StatusPolicyConfigClass.Roles)
            {
                var hr = _policy.SetDefaultEndpoint(deviceId, role);
                if (hr != 0)
                {
                    _log.LogWarning("SetDefaultEndpoint role {Role} failed: 0x{Hr:X8}", role, hr);
                    ok = false;
                }
            }
            _log.LogInformation("Default playback device change requested: {Ok}", ok);
            _debouncer.Trigger();
            return ok;
        }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "Setting the default playback device failed");
            return false;
        }
    }

    /// <inheritdoc />
    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
        }
        _debouncer.Dispose();
        // Release every COM object on the thread that created it, then stop the worker (Dispose of the worker runs queued work before it returns).
        _worker.Post(ReleaseOnWorker);
        _worker.Dispose();
    }

    private void ReleaseOnWorker()
    {
        try
        {
            if (_enumerator is not null && _notify is not null) _enumerator.UnregisterEndpointNotificationCallback(_notify);
        }
        catch (Exception ex) { _log.LogDebug(ex, "Unregistering the audio notification client failed"); }
        DetachVolume();
        _enumerator?.Dispose();
        _enumerator = null;
        _notify = null;
        if (_policy is not null)
        {
            Marshal.ReleaseComObject(_policy);
            _policy = null;
        }
    }

    /// <summary>
    /// IMMNotificationClient implementation. The callbacks come from the audio service's RPC threads, so each one only schedules a debounced refresh and
    /// never throws back into COM.
    /// </summary>
    private sealed class NotificationClient : IMMNotificationClient
    {
        private readonly Action _changed;
        private readonly ILogger _log;

        public NotificationClient(Action changed, ILogger log)
        {
            _changed = changed;
            _log = log;
        }

        public void OnDeviceStateChanged(string deviceId, DeviceState newState) => Schedule();
        public void OnDeviceAdded(string pwstrDeviceId) => Schedule();
        public void OnDeviceRemoved(string deviceId) => Schedule();
        public void OnDefaultDeviceChanged(DataFlow flow, Role role, string defaultDeviceId) => Schedule();
        public void OnPropertyValueChanged(string pwstrDeviceId, PropertyKey key) { }

        private void Schedule()
        {
            try { _changed(); }
            catch (Exception ex) { _log.LogDebug(ex, "Audio notification handler failed"); }
        }
    }
}
