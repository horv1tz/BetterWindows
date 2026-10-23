using System.Text;

namespace BetterWindows.Native.Interop;

/// <summary>
/// The single definition of <c>MonitorInfo.id</c>, shared by every stream that touches monitors (win-shell: monitor
/// enumeration; win-apps: IDesktopWallpaper, which addresses monitors by device path).
/// id = "m" + 8 lower-case hex digits of FNV-1a(32) over the lower-cased monitor DEVICE INTERFACE PATH
/// (e.g. "\\?\DISPLAY#DELA0B5#5&amp;...#{e6f07b5f-...}", as returned by IDesktopWallpaper.GetMonitorDevicePathAt or
/// EnumDisplayDevices(monitorDeviceName, ..., EDD_GET_DEVICE_INTERFACE_NAME).DeviceID). Stable across reboots/ports.
/// If no device path can be obtained, hash the GDI device name ("\\.\DISPLAY1") instead.
/// </summary>
public static class MonitorIdentity
{
    public static string IdFromDevicePath(string devicePath)
    {
        uint h = 2166136261;
        foreach (var b in Encoding.UTF8.GetBytes(devicePath.ToLowerInvariant()))
        {
            h ^= b;
            h *= 16777619;
        }
        return "m" + h.ToString("x8");
    }
}
