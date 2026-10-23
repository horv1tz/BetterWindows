using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native;

/// <summary>Pure monitor helpers (no Win32): point lookup, naming and snapshot comparison. Cross-platform, unit-tested.</summary>
internal static class MonitorMath
{
    /// <summary>
    /// The monitor containing the physical-pixel point; when the point lies outside every monitor, the NEAREST one (distance from the point
    /// to the monitor rectangle, like MonitorFromPoint(MONITOR_DEFAULTTONEAREST)). Null only for an empty list.
    /// </summary>
    public static MonitorInfo? FromPoint(IReadOnlyList<MonitorInfo> monitors, int x, int y)
    {
        MonitorInfo? best = null;
        long bestDistance = long.MaxValue;
        foreach (var m in monitors)
        {
            var r = RectMath.FromProtocol(m.BoundsPx);
            if (r.Contains(x, y))
            {
                return m;
            }

            long dx = x < r.Left ? r.Left - (long)x : (x >= r.Right ? x - (long)r.Right + 1 : 0);
            long dy = y < r.Top ? r.Top - (long)y : (y >= r.Bottom ? y - (long)r.Bottom + 1 : 0);
            long d = dx * dx + dy * dy;
            if (d < bestDistance)
            {
                bestDistance = d;
                best = m;
            }
        }

        return best;
    }

    /// <summary>User-visible monitor name: the friendly name when the driver provides one, otherwise "Display N" (1-based).</summary>
    public static string DisplayName(string? friendlyName, int index) =>
        string.IsNullOrWhiteSpace(friendlyName) ? $"Display {index + 1}" : friendlyName.Trim();

    /// <summary>Appends " (2)", " (3)"... to repeated names ("Generic PnP Monitor" twice) so the settings UI can tell monitors apart.</summary>
    public static IReadOnlyList<string> MakeUnique(IReadOnlyList<string> names)
    {
        var total = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var n in names)
        {
            total[n] = total.GetValueOrDefault(n) + 1;
        }

        var seen = new Dictionary<string, int>(StringComparer.Ordinal);
        var result = new string[names.Count];
        for (int i = 0; i < names.Count; i++)
        {
            string n = names[i];
            int k = seen[n] = seen.GetValueOrDefault(n) + 1;
            result[i] = total[n] > 1 ? $"{n} ({k})" : n;
        }

        return result;
    }

    /// <summary>
    /// Stable monitor id. The device interface path is preferred (stable across reboots and ports); if the driver gives none, the GDI device
    /// name ("\\.\DISPLAY1") is hashed instead, as documented on <see cref="MonitorIdentity"/>.
    /// </summary>
    public static string IdFor(string? devicePath, string gdiDeviceName) =>
        MonitorIdentity.IdFromDevicePath(string.IsNullOrWhiteSpace(devicePath) ? gdiDeviceName : devicePath);

    /// <summary>Value comparison of two snapshots (order sensitive).</summary>
    public static bool SameSnapshot(IReadOnlyList<MonitorInfo> a, IReadOnlyList<MonitorInfo> b)
    {
        if (a.Count != b.Count)
        {
            return false;
        }

        for (int i = 0; i < a.Count; i++)
        {
            if (!a[i].Equals(b[i]))
            {
                return false;
            }
        }

        return true;
    }
}
