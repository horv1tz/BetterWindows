using BetterWindows.Core.Platform;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Pure rules for the desktop layer and the desktop-icon marker; run on every OS.</summary>
public class DesktopLayerSelectionTests
{
    private static DesktopCandidate W(nint h, bool defView = false) => new(h, DesktopLayerSelection.WorkerWClass, defView);

    [Fact]
    public void Prefers_the_wallpaper_WorkerW_directly_below_the_icon_host()
    {
        var z = new List<DesktopCandidate>
        {
            new(10, "Notepad", false),
            W(20),                       // some WorkerW above the host: not chosen
            W(30, defView: true),        // icon host
            W(40),                       // directly below: the wallpaper host
            W(50),
        };

        DesktopLayerSelection.ChooseWorkerW(z).Should().Be(40);
    }

    [Fact]
    public void Falls_back_to_any_wallpaper_WorkerW_when_the_host_is_not_below_it()
    {
        var z = new List<DesktopCandidate>
        {
            W(20),
            new(30, DesktopShellNative.ProgmanClass, true), // icon view under Progman (layout without a WorkerW host)
        };

        DesktopLayerSelection.ChooseWorkerW(z).Should().Be(20);
    }

    [Fact]
    public void Never_picks_the_icon_host_or_a_non_WorkerW_window()
    {
        var z = new List<DesktopCandidate>
        {
            new(10, "Shell_TrayWnd", false),
            W(30, defView: true),
        };

        DesktopLayerSelection.ChooseWorkerW(z).Should().Be(0);
    }

    [Fact]
    public void Returns_zero_for_an_empty_or_unexpected_layout()
    {
        DesktopLayerSelection.ChooseWorkerW(Array.Empty<DesktopCandidate>()).Should().Be(0);
        DesktopLayerSelection.ChooseWorkerW(new List<DesktopCandidate> { W(0) }).Should().Be(0); // a null handle is never a target
    }

    [Fact]
    public void Ignores_WorkerW_with_a_DefView_when_no_plain_one_exists()
    {
        var z = new List<DesktopCandidate> { W(20, defView: true), W(30, defView: true) };
        DesktopLayerSelection.ChooseWorkerW(z).Should().Be(0);
    }

    [Fact]
    public void Null_layout_is_rejected()
    {
        var act = () => DesktopLayerSelection.ChooseWorkerW(null!);
        act.Should().Throw<ArgumentNullException>();
    }
}

/// <summary>Marker serialisation: what a fresh process reads back after a crash.</summary>
public class DesktopIconsMarkerTests
{
    [Fact]
    public void Marker_round_trips_through_its_json_form()
    {
        var created = new DateTime(2026, 10, 23, 12, 0, 0, DateTimeKind.Utc);
        var marker = new DesktopIconsMarker(4242, created);

        var parsed = DesktopIconsMarker.TryParse(marker.ToJson());

        parsed.Should().NotBeNull();
        parsed!.ProcessId.Should().Be(4242);
        parsed.CreatedUtc.Should().Be(created);
        parsed.Version.Should().Be(1);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("{\"version\":2,\"processId\":1,\"createdUtc\":\"2026-01-01T00:00:00Z\"}")]
    public void Unreadable_or_unknown_version_markers_are_not_trusted(string? json) =>
        DesktopIconsMarker.TryParse(json).Should().BeNull();
}
