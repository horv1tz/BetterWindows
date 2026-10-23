using BetterWindows.Native.Interop;
using Microsoft.Win32;
using Windows.Win32;

namespace BetterWindows.Native;

/// <summary>
/// Stateless, dependency-free, idempotent: makes Explorer's desktop icon list visible again when BetterWindows hid it (the marker file
/// <c>state\desktop-icons-hidden.json</c> records that). Called from crash paths and the next start after an unclean exit; never throws.
/// Without a marker it does nothing, so icons the user hid with the desktop's own View menu are never touched. The
/// <c>HideIcons</c> preference is read but never written: when the user has turned the icons off, the marker is removed and the list stays hidden.
/// If Explorer is not running the marker is kept for the next attempt.
/// </summary>
public static class DesktopIconsRestorer
{
    /// <summary>Serializes the controller and the restorer inside this process (a second process is harmless: every step is idempotent).</summary>
    internal static readonly object Gate = new();

    private const string AdvancedKey = @"HKEY_CURRENT_USER\Software\Microsoft\Windows\CurrentVersion\Explorer\Advanced";

    /// <summary>Shows the desktop icon list again if BetterWindows hid it. Safe to call repeatedly, from any thread, at any time.</summary>
    public static void RestoreNow()
    {
        bool locked = false;
        try
        {
            // A crash handler must not hang behind a stuck controller: wait a bounded time, then go ahead (all steps are idempotent).
            locked = Monitor.TryEnter(Gate, TimeSpan.FromSeconds(3));
            RestoreUnderGate();
        }
        catch (Exception)
        {
            // Never throws, by contract.
        }
        finally
        {
            if (locked)
            {
                Monitor.Exit(Gate);
            }
        }
    }

    /// <summary>Restores the icon list if the marker exists. Call with <see cref="Gate"/> held or from a single owner thread.</summary>
    internal static void RestoreUnderGate()
    {
        if (!ShellStateStore.Exists(DesktopIconsMarker.FileName))
        {
            return; // we did not hide anything (or already restored): leave the desktop as the user has it
        }

        nint listView = DesktopShellNative.FindListView();
        if (listView == 0)
        {
            return; // Explorer's desktop is not running: keep the marker for the next attempt
        }

        if (!UserTurnedIconsOff())
        {
            // SW_SHOWNA: show without activating, and ASYNC so a hung Explorer cannot block the crash path.
            PInvoke.ShowWindowAsync(listView.ToHwnd(), Windows.Win32.UI.WindowsAndMessaging.SHOW_WINDOW_CMD.SW_SHOWNA);
        }

        ShellStateStore.Delete(DesktopIconsMarker.FileName);
    }

    /// <summary>
    /// True when the user has switched the desktop icons off in the shell's own preference (HideIcons = 1). That value is only read here:
    /// BetterWindows never writes it, and it wins over our marker.
    /// </summary>
    internal static bool UserTurnedIconsOff()
    {
        try
        {
            return Registry.GetValue(AdvancedKey, "HideIcons", 0) is int value && value == 1;
        }
        catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException or ArgumentException)
        {
            return false;
        }
    }
}
