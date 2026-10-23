using BetterWindows.Host.Bridge;
using BetterWindows.Host.Cli;
using BetterWindows.Host.Lifecycle;
using BetterWindows.Host.Tests.Support;

namespace BetterWindows.Host.Tests.Lifecycle;

public class ProcessAndHelperTests
{
    [Fact]
    public void Probe_finds_this_process_and_rejects_impossible_pids()
    {
        var probe = new SystemProcessProbe();
        probe.IsAlive(Environment.ProcessId, null).Should().BeTrue();
        probe.IsAlive(0, null).Should().BeFalse();
        probe.IsAlive(-3, null).Should().BeFalse();
    }

    [Fact]
    public void Probe_treats_a_different_start_time_as_a_recycled_pid()
    {
        var probe = new SystemProcessProbe();
        probe.IsAlive(Environment.ProcessId, DateTimeOffset.UtcNow.AddDays(-10)).Should().BeFalse();
    }

    [Fact]
    public void Probe_accepts_the_real_start_time_within_tolerance()
    {
        var start = new DateTimeOffset(System.Diagnostics.Process.GetCurrentProcess().StartTime.ToUniversalTime(), TimeSpan.Zero);
        new SystemProcessProbe().IsAlive(Environment.ProcessId, start).Should().BeTrue();
    }

    [Theory]
    // The first token is the executable (it is passed to ProcessStartInfo separately) and is skipped.
    [InlineData("\"x.exe\" --guardian 12", new[] { "--guardian", "12" })]
    [InlineData("\"C:\\Program Files\\BW\\BetterWindows.exe\" --guardian 12 --data-dir \"D:\\x y\"", new[] { "--guardian", "12", "--data-dir", "D:\\x y" })]
    [InlineData("x.exe   a    b  ", new[] { "a", "b" })]
    [InlineData("x.exe", new string[0])]
    [InlineData("", new string[0])]
    public void Argument_splitting_for_the_fallback_path(string commandLine, string[] expected)
    {
        NativeHelpers.SplitArguments(commandLine).Should().Equal(expected);
    }

    [Fact]
    public void Guardian_command_line_quotes_the_path_and_drops_a_trailing_backslash_of_the_data_dir()
    {
        var cmd = GuardianLauncher.BuildCommandLine(@"C:\BW Apps\BetterWindows.exe", 4321, @"D:\data\");
        cmd.Should().Be("\"C:\\BW Apps\\BetterWindows.exe\" --guardian 4321 --data-dir \"D:\\data\"");
    }

    [Fact]
    public void Guardian_command_line_without_data_dir()
    {
        GuardianLauncher.BuildCommandLine(@"C:\bw.exe", 7, null).Should().Be("\"C:\\bw.exe\" --guardian 7");
    }

    [Theory]
    [InlineData("1.2.3+abcdef0123.dirty", "1.2.3", "abcdef01")]
    [InlineData("0.1.0", "0.1.0", null)]
    [InlineData("2.0.0+xyz", "2.0.0", null)]        // not hex: no sha
    [InlineData("+9f2c1a7d", null, "9f2c1a7d")]
    [InlineData("", null, null)]
    public void Informational_version_is_split_into_version_and_sha(string input, string? version, string? sha)
    {
        var (v, s) = BuildMetadata.ParseInformationalVersion(input);
        v.Should().Be(version);
        s.Should().Be(sha);
    }

    [Fact]
    public void Os_description_gets_the_build_number_once()
    {
        BuildMetadata.DescribeOs("Microsoft Windows 11 Pro", new Version(10, 0, 22631)).Should().Be("Microsoft Windows 11 Pro (build 22631)");
        BuildMetadata.DescribeOs("Windows 10 (build 19045)", new Version(10, 0, 19045)).Should().Be("Windows 10 (build 19045)");
        BuildMetadata.DescribeOs("", new Version(10, 0, 0)).Should().Be("Windows");
    }

    [Fact]
    public void Restore_journal_scanner_names_no_directory_and_never_throws_on_bad_paths()
    {
        RestoreJournalScanner.Scan("\0bad").Should().BeEmpty();
    }

    [Fact]
    public void Host_options_default_to_normal_mode()
    {
        var o = new HostRuntimeOptions(CommandLineParser.Parse(Array.Empty<string>()));
        o.SafeMode.Should().BeFalse();
        o.NoHideTaskbar.Should().BeFalse();
        o.HotkeysDisabled.Should().BeFalse();
        o.DesktopLayerDisabled.Should().BeFalse();
        o.ReadyTimeout.Should().Be(TimeSpan.FromSeconds(10));
        o.UiOrigin.Should().BeNull();
        o.DevTools.Should().BeFalse();
    }

    [Fact]
    public void Crash_loop_forces_safe_mode_with_its_reason()
    {
        var o = new HostRuntimeOptions(CommandLineParser.Parse(Array.Empty<string>()), crashLoopSafeMode: true);
        o.SafeMode.Should().BeTrue();
        o.NoHideTaskbar.Should().BeTrue();
        o.HotkeysDisabled.Should().BeTrue();
        o.DesktopLayerDisabled.Should().BeTrue();
        o.SafeModeReason.Should().Be("repeated unclean exits");
    }

    [Fact]
    public void Smoke_never_hides_the_taskbar_and_hides_the_surfaces()
    {
        var o = new HostRuntimeOptions(CommandLineParser.Parse(new[] { "--smoke" }));
        o.Smoke.Should().BeTrue();
        o.NoHideTaskbar.Should().BeTrue();
        o.HideSurfaces.Should().BeTrue();
        o.ReadyTimeout.Should().Be(TimeSpan.FromSeconds(40));
    }

    [Fact]
    public void Dev_origin_comes_from_the_command_line_before_the_environment()
    {
        var env = new HostRuntimeOptions(CommandLineParser.Parse(Array.Empty<string>()), uiOriginFromEnvironment: "http://localhost:5173");
        env.UiOrigin.Should().Be("http://localhost:5173");

        var cli = new HostRuntimeOptions(CommandLineParser.Parse(new[] { "--ui-url", "http://localhost:6000" }), uiOriginFromEnvironment: "http://localhost:5173");
        cli.UiOrigin.Should().Be("http://localhost:6000");

        var bad = new HostRuntimeOptions(CommandLineParser.Parse(Array.Empty<string>()), uiOriginFromEnvironment: "javascript:x");
        bad.UiOrigin.Should().BeNull();
    }

    [Fact]
    public void Debug_builds_allow_devtools()
    {
        new HostRuntimeOptions(CommandLineParser.Parse(Array.Empty<string>()), debugBuild: true).DevTools.Should().BeTrue();
    }
}
