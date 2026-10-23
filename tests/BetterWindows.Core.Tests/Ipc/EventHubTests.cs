using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;

namespace BetterWindows.Core.Tests.Ipc;

public class EventHubTests
{
    [Fact]
    public void Publish_reaches_all_surfaces_with_envelope_shape()
    {
        using var h = new CoreHarness();
        var dock = h.AddSurface(SurfaceKind.Dock);
        var bar = h.AddSurface(SurfaceKind.Menubar, "1");

        h.Hub.Publish(Msg.StatusBattery, new BatteryState { Present = true, Percent = 80, Charging = true });

        foreach (var s in new[] { dock, bar })
        {
            var e = s.Events(Msg.StatusBattery).Single();
            e["v"]!.GetValue<int>().Should().Be(ProtocolInfo.Version);
            e["payload"]!["percent"]!.GetValue<int>().Should().Be(80);
            e["payload"]!["minutesRemaining"].Should().BeNull(); // nulls are omitted
        }
    }

    [Fact]
    public void PublishToKind_and_PublishTo_are_targeted()
    {
        using var h = new CoreHarness();
        var dock = h.AddSurface(SurfaceKind.Dock);
        var bar1 = h.AddSurface(SurfaceKind.Menubar, "1");
        var bar2 = h.AddSurface(SurfaceKind.Menubar, "2");

        h.Hub.PublishToKind(SurfaceKind.Menubar, Msg.ThemeChanged, new ThemeState { Mode = ThemeMode.Dark });
        h.Hub.PublishTo(bar2.Id, Msg.OverlayHide);

        dock.Events(Msg.ThemeChanged).Should().BeEmpty();
        bar1.Events(Msg.ThemeChanged).Should().HaveCount(1);
        bar2.Events(Msg.ThemeChanged).Should().HaveCount(1);
        bar1.Events(Msg.OverlayHide).Should().BeEmpty();
        bar2.Events(Msg.OverlayHide).Should().HaveCount(1);
        bar2.Events(Msg.OverlayHide).Single()["payload"].Should().BeNull();
    }

    [Fact]
    public void A_throwing_surface_does_not_block_others()
    {
        using var h = new CoreHarness();
        h.Registry.Register(new ThrowingSurface());
        var ok = h.AddSurface(SurfaceKind.Dock);
        h.Hub.Publish(Msg.ThemeChanged, new ThemeState());
        ok.Events(Msg.ThemeChanged).Should().HaveCount(1);
    }

    private sealed class ThrowingSurface : BetterWindows.Core.Ipc.ISurfaceChannel
    {
        public BetterWindows.Core.Ipc.SurfaceId Id => new(SurfaceKind.Overlay);
        public void Post(string json) => throw new InvalidOperationException("webview is gone");
    }
}
