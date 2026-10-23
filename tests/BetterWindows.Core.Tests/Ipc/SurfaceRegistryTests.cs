using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Ipc;

public class SurfaceRegistryTests
{
    [Fact]
    public void A_late_unregister_of_a_replaced_window_does_not_remove_the_new_channel()
    {
        var reg = new SurfaceRegistry();
        var oldCh = new FakeSurface(SurfaceKind.Dock);
        var newCh = new FakeSurface(SurfaceKind.Dock);
        var closed = new List<SurfaceId>();
        reg.SurfaceClosed += closed.Add;
        reg.Register(oldCh);
        reg.Register(newCh);           // window re-created
        reg.Unregister(oldCh).Should().BeFalse(); // old window's Closed handler fires late

        reg.Get(newCh.Id).Should().BeSameAs(newCh);
        closed.Should().BeEmpty();
        reg.Unregister(newCh).Should().BeTrue();
        closed.Should().Equal(newCh.Id);
    }

    [Fact]
    public void Subscriber_exceptions_do_not_stop_other_subscribers()
    {
        var reg = new SurfaceRegistry();
        var ch = new FakeSurface(SurfaceKind.Menubar, "1");
        reg.Register(ch);
        var second = 0;
        reg.SurfaceReady += _ => throw new InvalidOperationException("boom");
        reg.SurfaceReady += _ => second++;
        reg.MarkReadyForTests(ch);
        second.Should().Be(1);
    }

    [Fact]
    public void Session_token_is_cancelled_on_close_and_renewed_by_a_new_handshake()
    {
        var reg = new SurfaceRegistry();
        var ch = new FakeSurface(SurfaceKind.Overlay);
        reg.Register(ch);
        var t1 = reg.SessionToken(ch);
        reg.BeginHandshake(ch);
        t1.IsCancellationRequested.Should().BeTrue();
        var t2 = reg.SessionToken(ch);
        t2.IsCancellationRequested.Should().BeFalse();
        reg.Unregister(ch);
        t2.IsCancellationRequested.Should().BeTrue();
    }

    [Fact]
    public void Channels_are_ready_only_between_handshakes()
    {
        var reg = new SurfaceRegistry();
        var ch = new FakeSurface(SurfaceKind.Overlay);
        reg.Register(ch);
        reg.IsReady(ch).Should().BeFalse();
        reg.CompleteHandshake(ch);
        reg.IsReady(ch).Should().BeTrue();
        reg.BeginHandshake(ch);
        reg.IsReady(ch).Should().BeFalse();
    }
}
