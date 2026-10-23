using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Host.Surfaces;

namespace BetterWindows.Native.Tests.HostSurfaces;

public class SettingsWindowPlacementTests
{
    private static readonly MonitorInfo Full = Mon.Make("1", 0, 0, 1920, 1080, primary: true);

    [Fact]
    public void First_open_is_centred_on_the_primary_work_area_at_980_by_680_dip()
    {
        var layout = SettingsWindowPlacement.Resolve(null, new[] { Full }, Full);
        Assert.Equal(new NativeRect(470, 180, 1450, 860), layout.BoundsPx);
        Assert.False(layout.Maximized);
        Assert.Equal(1.0, layout.Scale);
    }

    [Fact]
    public void No_monitors_gives_a_default_rectangle_at_the_origin()
    {
        var layout = SettingsWindowPlacement.Resolve(null, Array.Empty<MonitorInfo>());
        Assert.Equal(new NativeRect(0, 0, 980, 680), layout.BoundsPx);
    }

    [Fact]
    public void A_remembered_visible_rectangle_is_restored_including_maximised()
    {
        var saved = new SettingsWindowState { Left = 100, Top = 100, Width = 1000, Height = 700, Dpi = 96, Maximized = true };
        var layout = SettingsWindowPlacement.Resolve(saved, new[] { Full }, Full);
        Assert.Equal(new NativeRect(100, 100, 1100, 800), layout.BoundsPx);
        Assert.True(layout.Maximized);
    }

    [Fact]
    public void A_rectangle_on_an_unplugged_monitor_falls_back_to_the_default()
    {
        var saved = new SettingsWindowState { Left = 5000, Top = 100, Width = 1000, Height = 700, Dpi = 96 };
        Assert.Equal(new NativeRect(470, 180, 1450, 860), SettingsWindowPlacement.Resolve(saved, new[] { Full }, Full).BoundsPx);
    }

    [Fact]
    public void A_monitor_with_a_different_dpi_keeps_the_centre_and_rescales_the_size()
    {
        var hiDpi = Mon.Make("1", 0, 0, 1920, 1080, scale: 1.5, primary: true);
        var saved = new SettingsWindowState { Left = 100, Top = 100, Width = 800, Height = 600, Dpi = 96 };
        var layout = SettingsWindowPlacement.Resolve(saved, new[] { hiDpi }, hiDpi);
        Assert.Equal(1200, layout.BoundsPx.Width);
        Assert.Equal(900, layout.BoundsPx.Height);
        Assert.Equal(1.5, layout.Scale);
    }

    [Fact]
    public void Garbage_sizes_are_ignored_and_minimum_size_follows_the_scale()
    {
        var tiny = new SettingsWindowState { Left = 0, Top = 0, Width = 100, Height = 100, Dpi = 96 };
        Assert.Equal(new NativeRect(470, 180, 1450, 860), SettingsWindowPlacement.Resolve(tiny, new[] { Full }, Full).BoundsPx);
        Assert.Equal((1140, 780), SettingsWindowPlacement.MinSizePx(1.5));
    }
}

public class SettingsWindowTitleTests
{
    [Theory]
    [InlineData("ru", "en-US", "Настройки BetterWindows")]
    [InlineData("en", "ru-RU", "BetterWindows Settings")]
    [InlineData("auto", "ru-RU", "Настройки BetterWindows")]
    [InlineData("auto", "de-DE", "BetterWindows Settings")]
    [InlineData(null, "ru", "Настройки BetterWindows")]
    public void Title_follows_the_language_setting_or_the_ui_culture(string? language, string culture, string expected) =>
        Assert.Equal(expected, SettingsWindowTitle.For(language, culture));

    [Fact]
    public void Only_known_pages_are_accepted()
    {
        Assert.Equal("dock", SettingsWindowTitle.NormalizePage("dock"));
        Assert.Null(SettingsWindowTitle.NormalizePage("../etc"));
        Assert.Null(SettingsWindowTitle.NormalizePage(null));
    }
}

public class SettingsWindowStateFileTests
{
    [Fact]
    public void State_round_trips()
    {
        var state = new SettingsWindowState { Left = 3, Top = 4, Width = 980, Height = 680, Dpi = 144, Maximized = true };
        var back = SettingsWindowStateFile.TryParse(SettingsWindowStateFile.Serialize(state));
        Assert.Equal(state, back);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("not json")]
    public void Missing_or_damaged_files_mean_no_remembered_placement(string? json) =>
        Assert.Null(SettingsWindowStateFile.TryParse(json));

    [Fact]
    public void Oversized_files_are_refused()
    {
        Assert.Null(SettingsWindowStateFile.TryParse(new string(' ', 5000)));
    }
}
