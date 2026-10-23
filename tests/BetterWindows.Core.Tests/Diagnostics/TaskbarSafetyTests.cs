using BetterWindows.Core.Diagnostics;
using BetterWindows.Core.Protocol;
using Xunit;

namespace BetterWindows.Core.Tests.Diagnostics;

public class TaskbarSafetyTests
{
    [Fact]
    public void A_visible_request_is_always_visible()
    {
        var d = TaskbarSafety.Decide(TaskbarMode.Visible, dockReady: false, menubarReady: false, escapeHotkeyRegistered: false);
        Assert.Equal(TaskbarMode.Visible, d.Mode);
    }

    [Theory]
    [InlineData(TaskbarMode.Hidden)]
    [InlineData(TaskbarMode.AutoHide)]
    public void Hiding_needs_dock_menubar_and_an_escape_hotkey(TaskbarMode requested)
    {
        Assert.Equal(requested, TaskbarSafety.Decide(requested, true, true, true).Mode);
        Assert.Equal(TaskbarMode.Visible, TaskbarSafety.Decide(requested, false, true, true).Mode);
        Assert.Equal(TaskbarMode.Visible, TaskbarSafety.Decide(requested, true, false, true).Mode);
        Assert.Equal(TaskbarMode.Visible, TaskbarSafety.Decide(requested, true, true, false).Mode);
    }

    [Fact]
    public void Every_refusal_says_why()
    {
        Assert.Contains("dock and the menu bar", TaskbarSafety.Decide(TaskbarMode.Hidden, false, true, true).Reason);
        Assert.Contains("exit nor the toggle-taskbar", TaskbarSafety.Decide(TaskbarMode.Hidden, true, true, false).Reason);
        Assert.Equal("applied", TaskbarSafety.Decide(TaskbarMode.Hidden, true, true, true).Reason);
    }
}
