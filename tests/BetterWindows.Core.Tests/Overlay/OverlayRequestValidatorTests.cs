using System.Text.Json.Nodes;
using BetterWindows.Core.Overlay;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Overlay;

public class OverlayRequestValidatorTests
{
    private static readonly IReadOnlyList<MonitorInfo> Monitors = new[]
    {
        new MonitorInfo { Id = "m1" }, new MonitorInfo { Id = "m2" },
    };

    private static Rect Anchor(double x = 10, double y = 5, double w = 40, double h = 28) => new() { X = x, Y = y, Width = w, Height = h };

    private static OverlayRequest Popover(PopoverKind kind = PopoverKind.ControlCenter, Rect? anchor = null, string? itemId = null, JsonNode? data = null, string? monitor = null) => new()
    {
        Mode = OverlayMode.Popover, MonitorId = monitor,
        Popover = new PopoverRequest { Kind = kind, Anchor = anchor ?? Anchor(), ItemId = itemId, Data = data },
    };

    private static void ShouldBeBadRequest(OverlayRequest r) =>
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(r, Monitors)).Should().Throw<ProtocolException>().Which.Code.Should().Be("badRequest");

    [Theory]
    [InlineData(OverlayMode.Spotlight)]
    [InlineData(OverlayMode.Launchpad)]
    [InlineData(OverlayMode.MissionControl)]
    public void Plain_modes_are_valid_without_a_popover(OverlayMode mode)
    {
        var r = new OverlayRequest { Mode = mode };
        OverlayRequestValidator.Validate(r, Monitors).Should().BeSameAs(r);
    }

    [Theory]
    [InlineData(OverlayMode.Spotlight)]
    [InlineData(OverlayMode.Launchpad)]
    [InlineData(OverlayMode.MissionControl)]
    public void Plain_modes_must_not_carry_a_popover(OverlayMode mode) =>
        ShouldBeBadRequest(new OverlayRequest { Mode = mode, Popover = new PopoverRequest { Kind = PopoverKind.Apple, Anchor = Anchor() } });

    [Fact]
    public void Popover_mode_requires_a_popover() => ShouldBeBadRequest(new OverlayRequest { Mode = OverlayMode.Popover });

    [Theory]
    [MemberData(nameof(AllKinds))]
    public void Every_popover_kind_is_accepted(PopoverKind kind) => OverlayRequestValidator.Validate(Popover(kind), Monitors).Popover!.Kind.Should().Be(kind);

    public static IEnumerable<object[]> AllKinds() => Enum.GetValues<PopoverKind>().Select(k => new object[] { k });

    [Fact]
    public void Undefined_enum_values_are_rejected()
    {
        ShouldBeBadRequest(new OverlayRequest { Mode = (OverlayMode)99 });
        ShouldBeBadRequest(Popover((PopoverKind)99));
    }

    [Theory]
    [InlineData(double.NaN, 0, 10, 10)]
    [InlineData(0, double.NaN, 10, 10)]
    [InlineData(0, 0, double.NaN, 10)]
    [InlineData(0, 0, 10, double.NaN)]
    [InlineData(double.PositiveInfinity, 0, 10, 10)]
    [InlineData(0, double.NegativeInfinity, 10, 10)]
    [InlineData(0, 0, double.PositiveInfinity, 10)]
    [InlineData(1e9, 0, 10, 10)]
    [InlineData(0, -1e9, 10, 10)]
    [InlineData(0, 0, 1e9, 10)]
    [InlineData(0, 0, 10, 1e9)]
    [InlineData(0, 0, -1, 10)]
    [InlineData(0, 0, 10, -0.5)]
    [InlineData(16385, 0, 10, 10)]
    [InlineData(0, 0, 16385, 10)]
    public void Anchors_that_are_not_finite_or_not_sane_are_rejected(double x, double y, double w, double h) =>
        ShouldBeBadRequest(Popover(anchor: Anchor(x, y, w, h)));

    [Theory]
    [InlineData(0, 0, 0, 0)]
    [InlineData(-200, -50, 10, 10)]
    [InlineData(16384, 16384, 16384, 16384)]
    [InlineData(1919.5, 0.25, 33.3, 28)]
    public void Reasonable_anchors_are_accepted(double x, double y, double w, double h) =>
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(Popover(anchor: Anchor(x, y, w, h)), Monitors)).Should().NotThrow();

    [Fact]
    public void ItemId_must_be_bounded_and_printable()
    {
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(Popover(PopoverKind.DockMenu, itemId: "exe:c:\\apps\\a.exe"), Monitors)).Should().NotThrow();
        ShouldBeBadRequest(Popover(PopoverKind.DockMenu, itemId: "a\u0000b"));
        ShouldBeBadRequest(Popover(PopoverKind.DockMenu, itemId: "line\nbreak"));
        ShouldBeBadRequest(Popover(PopoverKind.DockMenu, itemId: new string('x', 4201)));
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(Popover(PopoverKind.DockMenu, itemId: new string('x', 4200)), Monitors)).Should().NotThrow();
    }

    [Fact]
    public void MonitorId_must_be_a_known_monitor_when_present()
    {
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(Popover(monitor: "m2"), Monitors)).Should().NotThrow();
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(new OverlayRequest { Mode = OverlayMode.Spotlight, MonitorId = "m1" }, Monitors)).Should().NotThrow();
        ShouldBeBadRequest(Popover(monitor: "m3"));
        ShouldBeBadRequest(Popover(monitor: ""));
        ShouldBeBadRequest(Popover(monitor: "M1"));
        ShouldBeBadRequest(new OverlayRequest { Mode = OverlayMode.Spotlight, MonitorId = "../../etc" });
    }

    [Fact]
    public void AppMenu_data_must_be_an_object_with_a_string_appId()
    {
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(Popover(PopoverKind.AppMenu, data: new JsonObject { ["appId"] = "exe:c:\\a.exe" }), Monitors)).Should().NotThrow();
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(Popover(PopoverKind.AppMenu, data: new JsonObject()), Monitors)).Should().NotThrow();
        ShouldBeBadRequest(Popover(PopoverKind.AppMenu, data: JsonValue.Create("string")));
        ShouldBeBadRequest(Popover(PopoverKind.AppMenu, data: new JsonArray(1, 2)));
        ShouldBeBadRequest(Popover(PopoverKind.AppMenu, data: new JsonObject { ["appId"] = 12 }));
        ShouldBeBadRequest(Popover(PopoverKind.AppMenu, data: new JsonObject { ["appId"] = new JsonObject() }));
        ShouldBeBadRequest(Popover(PopoverKind.AppMenu, data: new JsonObject { ["appId"] = "bad\u0001" }));
    }

    [Fact]
    public void Popover_data_is_size_limited_for_every_kind()
    {
        var big = new JsonObject { ["blob"] = new string('x', OverlayRequestValidator.MaxDataChars) };
        ShouldBeBadRequest(Popover(PopoverKind.Calendar, data: big));
        ShouldBeBadRequest(Popover(PopoverKind.AppMenu, data: big));
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(Popover(PopoverKind.Calendar, data: new JsonObject { ["x"] = 1 }), Monitors)).Should().NotThrow();
    }

    [Fact]
    public void A_null_request_is_an_argument_error_not_a_crash_of_the_router()
    {
        FluentActions.Invoking(() => OverlayRequestValidator.Validate(null!, Monitors)).Should().Throw<ArgumentNullException>();
    }
}
