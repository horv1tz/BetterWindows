using BetterWindows.Core.Dock;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Tests.Dock;

public class DefaultPinsTests
{
    private static AppEntry E(string name, string? aumid = null, string? path = null) => new()
    {
        Id = aumid is not null ? "aumid:" + aumid : "exe:" + path!.ToLowerInvariant(), Name = name, Aumid = aumid, Path = path, Packaged = aumid is not null,
    };

    private static AppEntry[] WindowsEn() => new[]
    {
        E("Microsoft Store", "Microsoft.WindowsStore_8wekyb3d8bbwe!App"),
        E("Calculator", "Microsoft.WindowsCalculator_8wekyb3d8bbwe!App"),
        E("Settings", "windows.immersivecontrolpanel_cw5n1h2txyewy!microsoft.windows.immersivecontrolpanel"),
        E("Terminal", "Microsoft.WindowsTerminal_8wekyb3d8bbwe!App"),
        E("Terminal Preview", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe!App"),
        E("Microsoft Edge", "MSEdge"),
        E("File Explorer", "Microsoft.Windows.Explorer"),
        E("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"),
        E("Photos", "Microsoft.Windows.Photos_8wekyb3d8bbwe!App"),
        E("Mail", "microsoft.windowscommunicationsapps_8wekyb3d8bbwe!microsoft.windowslive.mail"),
        E("Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify"),
    };

    [Fact]
    public void English_catalog_yields_the_candidates_in_dock_order()
    {
        var pins = DefaultPins.Seed(WindowsEn());
        pins.Select(p => p.Name).Should().Equal("File Explorer", "Microsoft Edge", "Terminal", "Notepad", "Calculator", "Settings", "Microsoft Store", "Photos", "Mail");
        pins.Should().OnlyContain(p => p.Kind == PinnedKind.App && p.Id.StartsWith("aumid:") && p.Target.Length > 0);
        pins[0].Target.Should().Be("Microsoft.Windows.Explorer");
    }

    [Fact]
    public void Terminal_Preview_is_not_taken_for_Terminal()
    {
        var pins = DefaultPins.Seed(new[] { E("Terminal Preview", "Microsoft.WindowsTerminalPreview_8wekyb3d8bbwe!App") });
        pins.Should().BeEmpty();
    }

    [Fact]
    public void Russian_display_names_are_matched()
    {
        var pins = DefaultPins.Seed(new[]
        {
            E("Проводник", path: @"C:\Windows\explorer.exe"),
            E("Терминал", path: @"C:\Program Files\WindowsApps\wt.exe"),
            E("Блокнот", path: @"C:\Windows\notepad.exe"),
            E("Калькулятор", path: @"C:\Windows\calc.exe"),
            E("Параметры", path: @"C:\Windows\ImmersiveControlPanel\SystemSettings.exe"),
            E("Фотографии", path: @"C:\Photos\Photos.exe"),
            E("Почта", path: @"C:\Mail\mail.exe"),
        });
        pins.Select(p => p.Name).Should().Equal("Проводник", "Терминал", "Блокнот", "Калькулятор", "Параметры", "Фотографии", "Почта");
    }

    [Fact]
    public void Exe_names_match_when_the_aumid_is_unknown()
    {
        var pins = DefaultPins.Seed(new[] { E("Some localized name", path: @"C:\Program Files (x86)\Microsoft\Edge\Application\msedge.exe") });
        pins.Should().ContainSingle().Which.Id.Should().Be(@"exe:c:\program files (x86)\microsoft\edge\application\msedge.exe");
    }

    [Fact]
    public void Terminal_falls_back_to_PowerShell_and_never_pins_both()
    {
        var withoutTerminal = DefaultPins.Seed(new[]
        {
            E("Windows PowerShell", path: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe"),
            E("Command Prompt", path: @"C:\Windows\System32\cmd.exe"),
        });
        withoutTerminal.Should().ContainSingle().Which.Name.Should().Be("Windows PowerShell");

        var withTerminal = DefaultPins.Seed(WindowsEn().Concat(new[] { E("Windows PowerShell", path: @"C:\Windows\System32\WindowsPowerShell\v1.0\powershell.exe") }).ToArray());
        withTerminal.Select(p => p.Name).Should().NotContain("Windows PowerShell");
    }

    [Fact]
    public void Missing_candidates_are_skipped_and_an_empty_catalog_gives_no_pins()
    {
        DefaultPins.Seed(Array.Empty<AppEntry>()).Should().BeEmpty();
        DefaultPins.Seed(new[] { E("Spotify", "SpotifyAB.SpotifyMusic_zpdnekdrzrea0!Spotify") }).Should().BeEmpty();
        DefaultPins.Seed(new[] { E("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App") }).Should().ContainSingle();
    }

    [Fact]
    public void Entries_without_a_launch_target_are_not_pinned()
    {
        DefaultPins.Seed(new[] { new AppEntry { Id = "aumid:", Name = "Notepad", Aumid = null, Path = null } }).Should().BeEmpty();
    }

    [Fact]
    public void An_app_is_never_pinned_twice()
    {
        var pins = DefaultPins.Seed(new[] { E("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App"), E("Notepad", "Microsoft.WindowsNotepad_8wekyb3d8bbwe!App") });
        pins.Should().ContainSingle();
    }

    [Theory]
    [InlineData(false, 0, true)]
    [InlineData(true, 0, false)]
    [InlineData(false, 2, false)]
    [InlineData(true, 2, false)]
    public void ShouldSeed_requires_a_fresh_profile(bool firstRunDone, int pinCount, bool expected)
    {
        var pins = Enumerable.Range(0, pinCount).Select(i => new PinnedItem { Id = "x" + i, Target = "x" }).ToList();
        var settings = new Settings { General = new GeneralSettings { FirstRunDone = firstRunDone }, Dock = new DockSettings { Pins = pins } };
        DefaultPins.ShouldSeed(settings).Should().Be(expected);
    }
}
