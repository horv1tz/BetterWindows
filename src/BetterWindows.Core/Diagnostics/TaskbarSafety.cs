using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Diagnostics;

/// <summary>
/// The rule that decides whether the native taskbar may be hidden (docs/IPC.md section 15, step 9): Hidden/AutoHide only while the dock AND the menu bar are running
/// AND at least one escape hotkey ("exit" or "toggleTaskbar") is registered; otherwise the taskbar stays Visible and the reason says why. Pure.
/// </summary>
public static class TaskbarSafety
{
    public sealed record Decision(TaskbarMode Mode, string Reason);

    public static Decision Decide(TaskbarMode requested, bool dockReady, bool menubarReady, bool escapeHotkeyRegistered)
    {
        if (requested == TaskbarMode.Visible) return new Decision(TaskbarMode.Visible, "the taskbar is shown (setting)");
        if (!dockReady || !menubarReady) return new Decision(TaskbarMode.Visible, "the taskbar stays visible until the dock and the menu bar are running");
        if (!escapeHotkeyRegistered) return new Decision(TaskbarMode.Visible, "the taskbar stays visible: neither the exit nor the toggle-taskbar hotkey is registered");
        return new Decision(requested, "applied");
    }
}
