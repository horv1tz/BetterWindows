using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Status;

/// <summary>
/// Pure mapping from raw connection facts to the protocol <see cref="NetworkState"/> (unit-tested on any OS).
/// Raw values: NetworkConnectivityLevel (None=0, LocalAccess=1, ConstrainedInternetAccess=2, InternetAccess=3) and IANA interface types
/// (ethernet 6, IEEE 802.11 71, WWAN 243/244) as documented for <c>NetworkAdapter.IanaInterfaceType</c>.
/// </summary>
internal static class NetworkMapper
{
    public const int LevelNone = 0, LevelLocalAccess = 1, LevelConstrainedInternet = 2, LevelInternet = 3;
    public const int IanaEthernet = 6, IanaWifi = 71, IanaWwan = 243, IanaWwanAlt = 244;

    /// <summary>Connected means the machine has at least local network access (the UI shows the network as usable).</summary>
    public static bool IsConnected(int connectivityLevel) =>
        connectivityLevel is LevelLocalAccess or LevelConstrainedInternet or LevelInternet;

    /// <summary>Kind of the internet connection profile. A missing profile is <see cref="NetworkKind.None"/>.</summary>
    public static NetworkKind Kind(bool hasProfile, bool isWlan, bool isWwan, int? ianaInterfaceType)
    {
        if (!hasProfile) return NetworkKind.None;
        if (isWlan) return NetworkKind.Wifi;
        if (ianaInterfaceType is IanaEthernet) return NetworkKind.Ethernet;
        if (isWwan || ianaInterfaceType is IanaWwan or IanaWwanAlt) return NetworkKind.Cellular;
        return ianaInterfaceType is IanaWifi ? NetworkKind.Wifi : NetworkKind.Other;
    }

    /// <summary>
    /// Maps <c>ConnectionProfile.GetSignalBars()</c> (0..5 by the documented range, nullable) onto the protocol's 0..4 range: null -> 0, values above 4 are
    /// clamped, so a full-strength 5 is shown as 4 bars (the protocol's maximum).
    /// </summary>
    public static int SignalBars(int? raw) => raw is null ? 0 : Math.Clamp(raw.Value, 0, 4);

    /// <summary>Value comparison of two snapshots (used by the source to swallow identical updates).</summary>
    public static bool Same(NetworkState a, NetworkState b) =>
        a.Kind == b.Kind && a.Connected == b.Connected && a.Name == b.Name && a.SignalBars == b.SignalBars;
}
