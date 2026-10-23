using BetterWindows.Host.Cli;

namespace BetterWindows.Host.Tests.Cli;

public class CommandLineParserTests
{
    private static CommandLineOptions P(params string[] args) => CommandLineParser.Parse(args);

    [Fact]
    public void No_arguments_is_a_normal_run_without_errors()
    {
        var o = P();
        o.Mode.Should().Be(LaunchMode.Run);
        o.Errors.Should().BeEmpty();
        o.Unknown.Should().BeEmpty();
    }

    [Theory]
    [InlineData("--restore-taskbar", LaunchMode.RestoreTaskbar)]
    [InlineData("--diagnose", LaunchMode.Diagnose)]
    [InlineData("--smoke", LaunchMode.Smoke)]
    [InlineData("--RESTORE-TASKBAR", LaunchMode.RestoreTaskbar)] // options are case-insensitive
    public void Mode_flags(string flag, LaunchMode mode)
    {
        var o = P(flag);
        o.Mode.Should().Be(mode);
        o.Errors.Should().BeEmpty();
    }

    [Fact]
    public void Guardian_takes_the_owner_pid_as_next_argument_or_inline()
    {
        var a = P("--guardian", "4242");
        a.Mode.Should().Be(LaunchMode.Guardian);
        a.GuardianOwnerPid.Should().Be(4242);
        a.Errors.Should().BeEmpty();

        var b = P("--guardian=77");
        b.Mode.Should().Be(LaunchMode.Guardian);
        b.GuardianOwnerPid.Should().Be(77);
    }

    [Fact]
    public void Legacy_watchdog_alias_is_a_guardian()
    {
        var o = P("--watchdog", "5");
        o.Mode.Should().Be(LaunchMode.Guardian);
        o.GuardianOwnerPid.Should().Be(5);
    }

    [Theory]
    [InlineData("--guardian")]          // missing value
    [InlineData("--guardian", "abc")]   // not a number
    [InlineData("--guardian", "-5")]    // not a positive pid (NumberStyles.None)
    [InlineData("--guardian", "0")]
    [InlineData("--guardian", "1.5")]
    public void Guardian_with_bad_pid_reports_an_error(params string[] args)
    {
        var o = P(args);
        o.Errors.Should().NotBeEmpty();
        o.GuardianOwnerPid.Should().Be(0);
    }

    [Fact]
    public void A_second_different_mode_is_a_conflict_and_the_first_wins()
    {
        var o = P("--smoke", "--diagnose");
        o.Mode.Should().Be(LaunchMode.Smoke);
        o.Errors.Should().ContainSingle().Which.Should().Contain("conflicting mode");
    }

    [Fact]
    public void The_same_mode_twice_is_fine()
    {
        P("--diagnose", "--diagnose").Errors.Should().BeEmpty();
    }

    [Fact]
    public void Boolean_flags_are_parsed()
    {
        var o = P("--json", "--no-hide-taskbar", "--safe-mode", "--devtools", "--autostart", "--reset-settings");
        o.Json.Should().BeTrue();
        o.NoHideTaskbar.Should().BeTrue();
        o.SafeMode.Should().BeTrue();
        o.DevTools.Should().BeTrue();
        o.Autostart.Should().BeTrue();
        o.ResetSettings.Should().BeTrue();
    }

    [Theory]
    [InlineData("http://localhost:5173", "http://localhost:5173")]
    [InlineData("http://localhost:5173/some/path?q=1", "http://localhost:5173")]
    [InlineData("https://dev.example.test", "https://dev.example.test")]
    public void Ui_url_is_reduced_to_its_origin(string url, string origin)
    {
        var o = P("--ui-url", url);
        o.UiOrigin.Should().Be(origin);
        o.Errors.Should().BeEmpty();
    }

    [Theory]
    [InlineData("ftp://host")]
    [InlineData("file:///C:/x")]
    [InlineData("https://user:pw@host")]
    [InlineData("not a url")]
    public void Ui_url_rejects_other_schemes_and_user_info(string url)
    {
        var o = P("--ui-url=" + url);
        o.UiOrigin.Should().BeNull();
        o.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Ui_url_without_value_when_next_is_an_option_reports_missing_value()
    {
        var o = P("--ui-url", "--devtools");
        o.UiOrigin.Should().BeNull();
        o.DevTools.Should().BeTrue();
        o.Errors.Should().ContainSingle().Which.Should().Contain("missing value");
    }

    [Fact]
    public void Data_dir_must_be_absolute_and_is_normalised()
    {
        // InputPolicy is platform-neutral: a local path is a drive-letter path, UNC and relative paths are refused.
        var ok = P("--data-dir", @"D:\BetterWindows\data\");
        ok.DataDir.Should().Be(@"D:\BetterWindows\data");
        ok.Errors.Should().BeEmpty();

        var rel = P("--data-dir", "relative/dir");
        rel.DataDir.Should().BeNull();
        rel.Errors.Should().ContainSingle();
    }

    [Fact]
    public void Out_file_must_be_absolute()
    {
        P("--out", "relative.txt").OutputFile.Should().BeNull();
        P("--out", "relative.txt").Errors.Should().ContainSingle();
    }

    [Fact]
    public void Unknown_options_and_positional_arguments_are_kept_as_unknown_not_errors()
    {
        var o = P("--frobnicate", "stray", "--json");
        o.Unknown.Should().BeEquivalentTo(new[] { "--frobnicate", "stray" });
        o.Errors.Should().BeEmpty();
        o.Json.Should().BeTrue();
    }

    [Fact]
    public void Null_entries_do_not_throw()
    {
        var o = CommandLineParser.Parse(new string?[] { null!, "--json" }.Select(s => s!));
        o.Json.Should().BeTrue();
        o.Unknown.Should().Contain("");
    }

    [Theory]
    [InlineData("http://localhost:5173", true)]
    [InlineData("https://a.b", true)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData(null, false)]
    [InlineData("javascript:alert(1)", false)]
    [InlineData("http://", false)]
    public void TryParseOrigin_accepts_only_http_origins(string? url, bool ok)
    {
        CommandLineParser.TryParseOrigin(url, out var origin).Should().Be(ok);
        if (!ok) origin.Should().BeEmpty();
    }

    [Fact]
    public void TryParseOrigin_rejects_overlong_input()
    {
        CommandLineParser.TryParseOrigin("http://h/" + new string('a', 3000), out _).Should().BeFalse();
    }
}
