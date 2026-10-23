using System.Text;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native;

/// <summary>
/// Read-only facts about the notification area for the diagnostics report: whether Shell_TrayWnd and TrayNotifyWnd exist and are visible.
/// Explains why <see cref="TrayHost"/> is unsupported. Never changes window state and never throws.
/// </summary>
public static class TrayDiagnostics
{
    /// <summary>Multi-line description; each line is "name: value". Safe to call from any thread.</summary>
    public static string Describe()
    {
        try
        {
            var sb = new StringBuilder();
            nint tray = DeskNative.FindTopLevel("Shell_TrayWnd");
            if (tray == 0)
            {
                sb.AppendLine("Shell_TrayWnd: not found (Explorer is not running or the taskbar is replaced)");
            }
            else
            {
                sb.Append("Shell_TrayWnd: found, ").AppendLine(DeskNative.IsVisible(tray) ? "visible" : "hidden (taskbar hidden or auto-hidden)");
                nint notify = DeskNative.FindChild(tray, "TrayNotifyWnd");
                sb.AppendLine(notify == 0
                    ? "TrayNotifyWnd: not found"
                    : "TrayNotifyWnd: found, " + (DeskNative.IsVisible(notify) ? "visible" : "hidden"));
            }
            sb.Append("Shell_NotifyIcon hosting: ").Append("unsupported (apps notify Explorer's own tray window; see TrayHost)");
            return sb.ToString();
        }
        catch (Exception ex)
        {
            return "Tray diagnostics unavailable: " + ex.GetType().Name;
        }
    }
}
