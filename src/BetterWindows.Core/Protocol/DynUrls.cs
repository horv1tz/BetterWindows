using System.Globalization;

namespace BetterWindows.Core.Protocol;

/// <summary>
/// URL scheme of the host's in-memory resource server (https://app.local/dyn/...).
/// Everything here is resolved by <see cref="Services.IDynamicResourceProvider"/>; the Host only forwards
/// WebResourceRequested for the "/dyn/" prefix. Keep this file and docs/IPC.md in sync.
/// </summary>
public static class DynUrls
{
    public const string Origin = "https://app.local";
    public const string Prefix = "/dyn/";

    /// <summary>Icon of a catalog/running app. size = requested px (the provider rounds to 32/64/128/256).</summary>
    public static string AppIcon(string appId, int size = 128) => $"{Prefix}icon/app/{Uri.EscapeDataString(appId)}?s={size}";
    /// <summary>Icon of one window (WM_GETICON fallback for windows without a known app).</summary>
    public static string WindowIcon(long handle, int size = 128) => $"{Prefix}icon/window/{handle.ToString(CultureInfo.InvariantCulture)}?s={size}";
    /// <summary>Shell icon of a file or folder path.</summary>
    public static string PathIcon(string path, int size = 128) => $"{Prefix}icon/path/{Uri.EscapeDataString(path)}?s={size}";
    /// <summary>PNG snapshot of a window. rev busts the cache after a re-capture.</summary>
    public static string WindowThumb(long handle, int width = 480, int rev = 0) => $"{Prefix}thumb/{handle.ToString(CultureInfo.InvariantCulture)}?w={width}&rev={rev}";
    /// <summary>Current media cover art.</summary>
    public static string MediaArt(int rev) => $"{Prefix}art/{rev.ToString(CultureInfo.InvariantCulture)}";
    /// <summary>Wallpaper image for a monitor.</summary>
    public static string Wallpaper(string monitorId, int rev = 0) => $"{Prefix}wallpaper/{Uri.EscapeDataString(monitorId)}?rev={rev}";
    /// <summary>Tray icon image.</summary>
    public static string TrayIcon(string id, int rev = 0) => $"{Prefix}tray/{Uri.EscapeDataString(id)}?rev={rev}";

    /// <summary>Absolute https URL for a path produced by the helpers above (UI uses the relative form).</summary>
    public static string Absolute(string relative) => Origin + relative;
}
