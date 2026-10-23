using BetterWindows.Core.Protocol;
using BetterWindows.Native;

namespace BetterWindows.Native.Tests.Desktops;

public class TrayHostTests
{
    [Fact]
    public async Task TrayHost_is_honestly_unsupported_and_inert()
    {
        using var host = new TrayHost();
        host.IsSupported.Should().BeFalse();
        host.Icons.Should().BeEmpty();
        host.Start();
        host.Start(); // idempotent, logs once
        host.Stop();
        host.Click("anything", TrayButton.Left, new Core.Platform.NativePoint(0, 0));
        (await host.GetIconPngAsync("anything")).Should().BeNull();
    }

    [Fact]
    public void TrayHost_changed_event_can_be_subscribed_and_removed()
    {
        using var host = new TrayHost();
        Action handler = () => throw new InvalidOperationException("never raised");
        host.Changed += handler;
        host.Changed -= handler;
    }
}
