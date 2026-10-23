using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;
using BetterWindows.Native.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Windows.Networking.Connectivity;

namespace BetterWindows.Native;

/// <summary>
/// Current internet connection from <c>NetworkInformation</c>: kind, connectivity, SSID or adapter name and Wi-Fi signal bars. Changes arrive through
/// <c>NetworkStatusChanged</c> (debounced) and a 15 s timer, because signal strength changes do not raise an event.
/// </summary>
public sealed class NetworkInfoSource : INetworkSource, IDisposable
{
    private static readonly TimeSpan PollPeriod = TimeSpan.FromSeconds(15);

    private readonly ILogger _log;
    private readonly StatusCell<NetworkState> _cell;
    private readonly StatusDebouncer _debouncer;
    private readonly NetworkStatusChangedEventHandler _onStatus;
    private readonly object _gate = new();
    private bool _started;
    private bool _disposed;
    private System.Threading.Timer? _poll;

    public NetworkInfoSource(ILogger<NetworkInfoSource>? log = null)
    {
        _log = log ?? NullLogger<NetworkInfoSource>.Instance;
        _cell = new StatusCell<NetworkState>(new NetworkState(), NetworkMapper.Same, _log);
        _debouncer = new StatusDebouncer(TimeProvider.System, TimeSpan.FromMilliseconds(250), TimeSpan.FromSeconds(1), Refresh,
            ex => _log.LogWarning(ex, "Network refresh failed"));
        _onStatus = _ => _debouncer.Trigger();
    }

    /// <inheritdoc />
    public NetworkState Current { get { EnsureStarted(); return _cell.Value; } }

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
                NetworkInformation.NetworkStatusChanged += _onStatus;
            }
            catch (Exception ex)
            {
                _log.LogWarning(ex, "NetworkStatusChanged is unavailable; relying on the poll timer");
            }
            _poll = new System.Threading.Timer(_ => _debouncer.Trigger(), null, PollPeriod, PollPeriod);
        }
        Refresh();
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
            _log.LogWarning(ex, "Reading the network state failed");
        }
    }

    private NetworkState Read()
    {
        // GetInternetConnectionProfile returns null when no profile has internet or local access (airplane mode, no adapter).
        var profile = NetworkInformation.GetInternetConnectionProfile();
        if (profile is null) return new NetworkState { Kind = NetworkKind.None, Connected = false, Name = null, SignalBars = 0 };

        var connected = NetworkMapper.IsConnected((int)profile.GetNetworkConnectivityLevel());
        var kind = NetworkMapper.Kind(
            hasProfile: true,
            isWlan: profile.IsWlanConnectionProfile,
            isWwan: profile.IsWwanConnectionProfile,
            ianaInterfaceType: profile.NetworkAdapter is { } adapter ? (int)adapter.IanaInterfaceType : null);

        string? name = null;
        if (kind == NetworkKind.Wifi)
        {
            // The SSID is only available for WLAN profiles; the profile name is the fallback label.
            name = profile.WlanConnectionProfileDetails?.GetConnectedSsid();
        }
        name ??= profile.ProfileName;

        var bars = connected && kind == NetworkKind.Wifi ? NetworkMapper.SignalBars((int?)profile.GetSignalBars()) : 0;
        return new NetworkState { Kind = kind, Connected = connected, Name = string.IsNullOrWhiteSpace(name) ? null : name, SignalBars = bars };
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _disposed = true;
            if (_started)
            {
                try { NetworkInformation.NetworkStatusChanged -= _onStatus; }
                catch (Exception ex) { _log.LogDebug(ex, "Unsubscribing from NetworkStatusChanged failed"); }
            }
            _poll?.Dispose();
            _poll = null;
        }
        _debouncer.Dispose();
    }
}
