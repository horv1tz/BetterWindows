using BetterWindows.Native.Interop;

namespace BetterWindows.Native;

/// <summary>
/// Stateless, dependency-free, idempotent: make Explorer's desktop icon list visible again if BetterWindows hid it.
/// Called from crash paths and by the next start after an unclean exit; never throws. Works from a FRESH process: the decision comes from the marker/snapshot files under
/// <c>%LOCALAPPDATA%\BetterWindows\state</c>, not from any in-memory state. The persisted Explorer setting "Show desktop icons" (HideIcons) is never touched; only the
/// visibility of the SysListView32 window is.
/// </summary>
public static class DesktopIconsRestorer
{
    /// <summary>Shows the icon list again if (and only if) we hid it, then removes the marker and snapshot. Safe to call any number of times.</summary>
    public static void RestoreNow()
    {
        try
        {
            Restore(DesktopIconsState.CreateDefault());
        }
        catch (Exception)
        {
            // Crash path: nothing sensible left to do and nothing may escape.
        }
    }

    /// <summary>The restore algorithm with an injectable state folder (tests, controller). Returns true when nothing is left to restore afterwards.</summary>
    internal static bool Restore(DesktopIconsState state)
    {
        try
        {
            var snapshot = state.ReadSnapshot();
            bool markerPresent = state.MarkerExists;
            if (!markerPresent && snapshot == null) return true; // we never hid anything (or already restored)

            // Default to "was visible": the marker proves we hid it, and a lost snapshot must lean towards showing the icons.
            bool shouldShow = snapshot?.ListViewWasVisible ?? true;
            if (shouldShow)
            {
                if (AppsDesktopWindows.TryFind(out var layout) && layout.ListView != 0)
                {
                    if (!AppsDesktopWindows.HasVisibleStyle(layout.ListView))
                    {
                        // Explorer is hung: keep the marker so the next attempt (or the next start) still knows there is work to do.
                        if (!AppsDesktopWindows.TryShow(layout.ListView, show: true)) return false;
                    }
                }
                // No desktop window found: Explorer is not running or is restarting; the new instance creates a visible list by itself.
            }
            state.Clear();
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }
}
