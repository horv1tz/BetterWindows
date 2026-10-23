using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Surfaces;

namespace BetterWindows.Native.Tests.HostSurfaces;

public class AutoHideStateMachineTests
{
    private static readonly AutoHideOptions Options = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(400));
    private static TimeSpan Ms(int ms) => TimeSpan.FromMilliseconds(ms);

    [Fact]
    public void Reveal_needs_continuous_presence_in_the_edge_strip()
    {
        var m = new AutoHideStateMachine(Options);
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(0), true, false, false));
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(100), true, false, false));
        Assert.Equal(AutoHideTransition.Reveal, m.Update(Ms(150), true, false, false));
        Assert.True(m.Revealed);
    }

    [Fact]
    public void Leaving_the_strip_restarts_the_reveal_timer()
    {
        var m = new AutoHideStateMachine(Options);
        m.Update(Ms(0), true, false, false);
        m.Update(Ms(100), false, false, false);
        m.Update(Ms(120), true, false, false);
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(200), true, false, false));
        Assert.Equal(AutoHideTransition.Reveal, m.Update(Ms(270), true, false, false));
    }

    [Fact]
    public void Hide_needs_400_ms_away_from_both_zones()
    {
        var m = new AutoHideStateMachine(Options, initiallyRevealed: true);
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(0), false, false, false));
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(399), false, false, false));
        Assert.Equal(AutoHideTransition.Hide, m.Update(Ms(400), false, false, false));
        Assert.False(m.Revealed);
    }

    [Fact]
    public void Being_on_the_dock_or_a_visible_popover_keeps_it_revealed()
    {
        var m = new AutoHideStateMachine(Options, initiallyRevealed: true);
        m.Update(Ms(0), false, false, false);
        m.Update(Ms(300), false, true, false); // back on the dock resets the countdown
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(1000), false, false, false)); // only 700 ms after the last contact, and 400 ms counted from the keep zone
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(2000), false, false, overlayVisible: true));
        Assert.True(m.Revealed);
    }

    [Fact]
    public void Core_forcing_keeps_it_revealed_until_released()
    {
        var m = new AutoHideStateMachine(Options);
        Assert.Equal(AutoHideTransition.Reveal, m.SetForced(true));
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(0), false, false, false));
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(5000), false, false, false));
        Assert.Equal(AutoHideTransition.None, m.SetForced(false));
        Assert.Equal(AutoHideTransition.None, m.Update(Ms(5100), false, false, false));
        Assert.Equal(AutoHideTransition.Hide, m.Update(Ms(5500), false, false, false));
    }

    [Fact]
    public void Reset_reinitialises_but_keeps_the_core_force()
    {
        var m = new AutoHideStateMachine(Options);
        m.SetForced(true);
        m.Reset(revealed: false);
        Assert.True(m.Forced);
        Assert.False(m.Revealed);
    }
}

public class AutoHideCoordinatorTests
{
    private static readonly AutoHideOptions Options = new(TimeSpan.FromMilliseconds(150), TimeSpan.FromMilliseconds(400));

    [Fact]
    public void Pointer_at_the_edge_reveals_and_leaving_hides_the_registered_dock()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor { Position = new NativePoint(50, 1079) };
        var transitions = new List<(SurfaceId Id, bool Reveal)>();
        var dock = new SurfaceId(SurfaceKind.Dock);
        using var c = new AutoHideCoordinator(cursor, clock, (id, reveal) => transitions.Add((id, reveal)), () => false, null, Options);
        c.Register(dock, initiallyRevealed: false);
        c.UpdateZones(dock, new NativeRect(0, 1078, 100, 1080), new NativeRect(0, 0, 100, 100));

        c.Tick(TimeSpan.Zero);
        c.Tick(TimeSpan.FromMilliseconds(150));
        Assert.Equal(new[] { (dock, true) }, transitions);

        cursor.Position = new NativePoint(50, 500);
        c.Tick(TimeSpan.FromMilliseconds(200));
        c.Tick(TimeSpan.FromMilliseconds(600));
        Assert.Equal(new[] { (dock, true), (dock, false) }, transitions);
    }

    [Fact]
    public void A_failed_cursor_read_skips_the_tick_without_changing_state()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor { Position = new NativePoint(50, 1079), Available = false };
        var transitions = 0;
        var dock = new SurfaceId(SurfaceKind.Dock);
        using var c = new AutoHideCoordinator(cursor, clock, (_, _) => transitions++, () => false, null, Options);
        c.Register(dock, initiallyRevealed: false);
        c.UpdateZones(dock, new NativeRect(0, 1078, 100, 1080), new NativeRect(0, 0, 100, 100));
        c.Tick(TimeSpan.Zero);
        c.Tick(TimeSpan.FromSeconds(1));
        Assert.Equal(0, transitions);
        Assert.False(c.IsRevealed(dock));
    }

    [Fact]
    public void Unregistered_surfaces_are_not_driven_and_forcing_an_unknown_id_is_a_no_op()
    {
        var clock = new ManualClock();
        var dock = new SurfaceId(SurfaceKind.Dock, "2");
        using var c = new AutoHideCoordinator(new FakeCursor(), clock, (_, _) => throw new InvalidOperationException("must not be called"), () => false, null, Options);
        Assert.Equal(AutoHideTransition.None, c.SetForced(dock, true));
        c.Register(dock, false);
        c.Unregister(dock);
        Assert.Equal(0, c.Count);
    }

    [Fact]
    public void Overlay_visibility_blocks_hiding_through_the_coordinator()
    {
        var clock = new ManualClock();
        var cursor = new FakeCursor { Position = new NativePoint(50, 1079) };
        var dock = new SurfaceId(SurfaceKind.Dock);
        using var c = new AutoHideCoordinator(cursor, clock, (_, _) => { }, () => true, null, Options);
        c.Register(dock, initiallyRevealed: true);
        c.UpdateZones(dock, new NativeRect(0, 1078, 100, 1080), new NativeRect(0, 0, 100, 100));
        cursor.Position = new NativePoint(500, 500);
        c.Tick(TimeSpan.Zero);
        c.Tick(TimeSpan.FromSeconds(2));
        Assert.True(c.IsRevealed(dock));
    }
}
