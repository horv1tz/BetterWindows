using BetterWindows.Core.Protocol;

namespace BetterWindows.Native.Status;

/// <summary>Pure helpers for the Bluetooth source: battery parsing and value comparison (unit-tested on any OS).</summary>
internal static class BluetoothMapper
{
    /// <summary>Upper bound on listed paired devices: keeps the flyout short and bounds the per-device WinRT calls.</summary>
    public const int MaxDevices = 24;

    /// <summary>
    /// Parses the DEVPKEY_Device_BatteryLevel property value (a byte in practice; any integral type is accepted). Returns null for absent, non-integral or
    /// out-of-range values, so a device that reports garbage shows no battery rather than a wrong one.
    /// </summary>
    public static int? ParseBatteryPercent(object? raw)
    {
        if (raw is null) return null;
        long value;
        try
        {
            if (raw is string || raw is bool || raw is float || raw is double || raw is decimal) return null;
            value = Convert.ToInt64(raw, System.Globalization.CultureInfo.InvariantCulture);
        }
        catch (Exception ex) when (ex is InvalidCastException or FormatException or OverflowException or ArgumentException)
        {
            return null;
        }
        return value is >= 0 and <= 100 ? (int)value : null;
    }

    public static bool SameDevices(IReadOnlyList<BluetoothDevice> a, IReadOnlyList<BluetoothDevice> b)
    {
        if (a.Count != b.Count) return false;
        for (var i = 0; i < a.Count; i++)
        {
            var x = a[i];
            var y = b[i];
            if (x.Id != y.Id || x.Name != y.Name || x.Connected != y.Connected || x.BatteryPercent != y.BatteryPercent) return false;
        }
        return true;
    }

    public static bool Same(BluetoothState a, BluetoothState b) =>
        a.Supported == b.Supported && a.Enabled == b.Enabled && SameDevices(a.Devices, b.Devices);
}
