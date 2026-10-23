using BetterWindows.Native;

namespace BetterWindows.Native.Tests.Desktops;

public class DesktopInternalPathTests
{
    [Theory]
    [InlineData(19045, null)]        // Windows 10
    [InlineData(22000, "Windows 11 21H2")]
    [InlineData(22620, "Windows 11 21H2")]
    [InlineData(22621, "Windows 11 22H2")]
    [InlineData(26099, "Windows 11 22H2")]
    [InlineData(26100, "Windows 11 24H2")]
    [InlineData(27000, "Windows 11 24H2")]
    [InlineData(0, null)]
    [InlineData(-1, null)]
    public void Build_table_maps_builds_to_release_ranges(int build, string? label)
    {
        DesktopBuildTable.Lookup(build)?.Label.Should().Be(label);
    }

    [Fact]
    public void Gate_passes_through_a_successful_move()
    {
        var gate = new DesktopInternalGate();
        int calls = 0;
        gate.TryRun(() => { calls++; return DesktopInternalOutcome.Moved; }).Should().BeTrue();
        gate.IsDisabled.Should().BeFalse();
        calls.Should().Be(1);
    }

    [Fact]
    public void Gate_rejection_does_not_disable_the_path()
    {
        var gate = new DesktopInternalGate();
        gate.TryRun(() => DesktopInternalOutcome.Rejected).Should().BeFalse();
        gate.IsDisabled.Should().BeFalse();
    }

    [Fact]
    public void Gate_disables_for_the_session_after_the_first_failure()
    {
        var logged = new List<string>();
        var gate = new DesktopInternalGate(logged.Add);
        int calls = 0;

        gate.TryRun(() => { calls++; return DesktopInternalOutcome.Failed; }).Should().BeFalse();
        gate.IsDisabled.Should().BeTrue();

        gate.TryRun(() => { calls++; return DesktopInternalOutcome.Moved; }).Should().BeFalse();
        calls.Should().Be(1, "a disabled path is never entered again");
        logged.Should().ContainSingle();
    }

    [Fact]
    public void Gate_turns_an_exception_into_a_disable_and_never_throws()
    {
        var gate = new DesktopInternalGate();
        gate.TryRun(() => throw new InvalidOperationException("boom")).Should().BeFalse();
        gate.IsDisabled.Should().BeTrue();
    }

    [Fact]
    public void Gate_survives_a_throwing_logger()
    {
        var gate = new DesktopInternalGate(_ => throw new IOException("disk full"));
        gate.TryRun(() => DesktopInternalOutcome.Failed).Should().BeFalse();
        gate.IsDisabled.Should().BeTrue();
    }
}
