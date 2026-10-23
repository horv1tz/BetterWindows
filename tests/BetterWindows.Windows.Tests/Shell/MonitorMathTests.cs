using BetterWindows.Core.Protocol;
using BetterWindows.Native.Interop;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Monitor id/name/lookup logic that does not need a display; runs everywhere.</summary>
public class MonitorMathTests
{
    private static MonitorInfo Mon(string id, int x, int y, int w, int h, bool primary = false) => new()
    {
        Id = id,
        Primary = primary,
        BoundsPx = new Rect { X = x, Y = y, Width = w, Height = h },
        WorkAreaPx = new Rect { X = x, Y = y, Width = w, Height = h },
        Scale = 1,
    };

    private static readonly MonitorInfo Left = Mon("L", -1920, 0, 1920, 1080);
    private static readonly MonitorInfo Main = Mon("M", 0, 0, 2560, 1440, primary: true);
    private static readonly MonitorInfo[] Both = { Left, Main };

    [Fact]
    public void Monitor_id_is_fnv1a_of_the_lowercased_device_path()
    {
        MonitorIdentity.IdFromDevicePath("").Should().Be("m811c9dc5");
        MonitorIdentity.IdFromDevicePath(@"\\?\DISPLAY#DELA0B5#5&1a2b3c4d&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}").Should().Be("m9dbf144d");
        MonitorIdentity.IdFromDevicePath(@"\\?\display#dela0b5#5&1a2b3c4d&0&uid4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}").Should().Be("m9dbf144d");
    }

    [Fact]
    public void Id_prefers_the_device_path_and_falls_back_to_the_gdi_name()
    {
        MonitorMath.IdFor(@"\\?\DISPLAY#DELA0B5#5&1a2b3c4d&0&UID4352#{e6f07b5f-ee97-4a90-b076-33f57bf4eaa7}", @"\\.\DISPLAY1").Should().Be("m9dbf144d");
        MonitorMath.IdFor(null, @"\\.\DISPLAY1").Should().Be("mfa5ce78a");
        MonitorMath.IdFor("  ", @"\\.\DISPLAY1").Should().Be("mfa5ce78a");
        MonitorMath.IdFor("x", "y").Should().MatchRegex("^m[0-9a-f]{8}$");
    }

    [Fact]
    public void FromPoint_returns_the_containing_monitor_including_negative_coordinates()
    {
        MonitorMath.FromPoint(Both, -1, 10)!.Id.Should().Be("L");
        MonitorMath.FromPoint(Both, 0, 0)!.Id.Should().Be("M");
        MonitorMath.FromPoint(Both, -1920, 0)!.Id.Should().Be("L");
        MonitorMath.FromPoint(Both, 2559, 1439)!.Id.Should().Be("M");
    }

    [Fact]
    public void FromPoint_outside_everything_returns_the_nearest_monitor()
    {
        MonitorMath.FromPoint(Both, -5000, 500)!.Id.Should().Be("L");
        MonitorMath.FromPoint(Both, 9000, 500)!.Id.Should().Be("M");
        MonitorMath.FromPoint(Both, -1000, 1300)!.Id.Should().Be("L"); // below the left monitor, much closer to it than to M
        MonitorMath.FromPoint(Both, 100, -50)!.Id.Should().Be("M");
    }

    [Fact]
    public void FromPoint_with_no_monitors_is_null() => MonitorMath.FromPoint(Array.Empty<MonitorInfo>(), 0, 0).Should().BeNull();

    [Fact]
    public void Display_names()
    {
        MonitorMath.DisplayName("DELL U2720Q", 0).Should().Be("DELL U2720Q");
        MonitorMath.DisplayName("  ", 1).Should().Be("Display 2");
        MonitorMath.DisplayName(null, 0).Should().Be("Display 1");
    }

    [Fact]
    public void Repeated_names_get_numbered_so_the_ui_can_tell_them_apart()
    {
        MonitorMath.MakeUnique(new[] { "Generic PnP Monitor", "Generic PnP Monitor", "DELL" })
            .Should().Equal("Generic PnP Monitor (1)", "Generic PnP Monitor (2)", "DELL");
        MonitorMath.MakeUnique(new[] { "A" }).Should().Equal("A");
        MonitorMath.MakeUnique(Array.Empty<string>()).Should().BeEmpty();
    }

    [Fact]
    public void Snapshot_comparison_detects_work_area_and_scale_changes()
    {
        var a = new[] { Main };
        MonitorMath.SameSnapshot(a, new[] { Main with { } }).Should().BeTrue();
        MonitorMath.SameSnapshot(a, new[] { Main with { Scale = 1.5 } }).Should().BeFalse();
        MonitorMath.SameSnapshot(a, new[] { Main with { WorkAreaPx = new Rect { X = 0, Y = 28, Width = 2560, Height = 1412 } } }).Should().BeFalse();
        MonitorMath.SameSnapshot(a, Both).Should().BeFalse();
        MonitorMath.SameSnapshot(Array.Empty<MonitorInfo>(), Array.Empty<MonitorInfo>()).Should().BeTrue();
    }
}
