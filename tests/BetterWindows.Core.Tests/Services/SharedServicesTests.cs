using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Tests.Services;

public class SharedServicesTests
{
    [Fact]
    public void Path_grants_cover_the_granted_path_and_its_children_only()
    {
        IPathGrants g = new PathGrants();
        g.Grant(@"C:\Users\me\Pictures");
        g.IsGranted(@"c:\users\me\pictures").Should().BeTrue();
        g.IsGranted(@"C:\Users\me\Pictures\a\b.png").Should().BeTrue();
        g.IsGranted(@"C:\Users\me\Pictures2\x.png").Should().BeFalse();
        g.IsGranted(@"C:\Users\me").Should().BeFalse();
        g.IsGranted(@"\\evil\share\x").Should().BeFalse();
        g.Grant(@"\\evil\share"); // invalid input is ignored
        g.IsGranted(@"\\evil\share\x").Should().BeFalse();
    }

    [Fact]
    public void Escape_hotkey_status_requires_exit_or_toggleTaskbar()
    {
        IHotkeyStatus s = new HotkeyStatusService();
        var changes = 0;
        s.Changed += () => changes++;
        s.EscapeHotkeyRegistered.Should().BeFalse();
        s.Report(new HotkeyRegistration("spotlight", "Alt+Space", true, null));
        s.EscapeHotkeyRegistered.Should().BeFalse();
        s.Report(new HotkeyRegistration("exit", "Ctrl+Alt+Q", false, "taken"));
        s.EscapeHotkeyRegistered.Should().BeFalse();
        s.Report(new HotkeyRegistration("toggleTaskbar", "Ctrl+Alt+Shift+T", true, null));
        s.EscapeHotkeyRegistered.Should().BeTrue();
        s.Clear("toggleTaskbar");
        s.EscapeHotkeyRegistered.Should().BeFalse();
        changes.Should().Be(4);
    }

    [Fact]
    public void Overlay_state_raises_only_on_real_changes()
    {
        var o = new OverlayStateService();
        var seen = new List<OverlayState>();
        ((IOverlayState)o).Changed += seen.Add;
        o.Set(OverlayState.Hidden);
        o.Set(new OverlayState(true, OverlayMode.Spotlight, "m1", 1, null, null));
        o.Set(new OverlayState(true, OverlayMode.Spotlight, "m1", 1, null, null));
        seen.Should().HaveCount(1);
        o.Current.Mode.Should().Be(OverlayMode.Spotlight);
    }
}
