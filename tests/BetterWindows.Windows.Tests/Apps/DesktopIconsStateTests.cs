namespace BetterWindows.Native.Tests.Apps;

public class DesktopIconsStateTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-test-" + Guid.NewGuid().ToString("N"), "state");

    public void Dispose()
    {
        try { Directory.Delete(Path.GetDirectoryName(_dir)!, recursive: true); }
        catch (Exception) { }
    }

    [Fact]
    public void Default_directory_is_below_local_app_data()
    {
        var s = DesktopIconsState.CreateDefault();
        s.Directory.Should().EndWith(Path.Combine("BetterWindows", "state"));
        Path.GetFileName(s.MarkerPath).Should().Be("desktop-icons-hidden");
        Path.GetFileName(s.SnapshotPath).Should().Be("desktopicons.json");
    }

    [Fact]
    public void Begin_writes_snapshot_and_marker_and_clear_removes_both()
    {
        var state = new DesktopIconsState(_dir);
        state.MarkerExists.Should().BeFalse();
        var snap = new DesktopIconsSnapshot(1, true, 1234, 77, new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc));

        state.TryBegin(snap).Should().BeTrue();

        state.MarkerExists.Should().BeTrue();
        File.Exists(state.SnapshotPath).Should().BeTrue();
        state.ReadSnapshot().Should().Be(snap);
        var marker = state.ReadMarker();
        marker.Should().NotBeNull();
        marker!.Version.Should().Be(DesktopIconsState.CurrentVersion);
        marker.ProcessId.Should().Be(Environment.ProcessId);

        state.Clear();
        state.MarkerExists.Should().BeFalse();
        File.Exists(state.SnapshotPath).Should().BeFalse();
        state.Clear(); // idempotent
    }

    [Fact]
    public void Writes_leave_no_temp_files_behind_and_overwrite_cleanly()
    {
        var state = new DesktopIconsState(_dir);
        state.TryBegin(new DesktopIconsSnapshot(1, true, 1, 1, DateTime.UtcNow)).Should().BeTrue();
        state.TryBegin(new DesktopIconsSnapshot(1, false, 2, 2, DateTime.UtcNow)).Should().BeTrue();
        Directory.GetFiles(_dir).Select(Path.GetFileName).Should().BeEquivalentTo("desktop-icons-hidden", "desktopicons.json");
        state.ReadSnapshot()!.ListViewWasVisible.Should().BeFalse();
    }

    [Fact]
    public void Serialization_uses_stable_camel_case_names()
    {
        var json = DesktopIconsState.Serialize(new DesktopIconsSnapshot(1, true, 99, 5, new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc)));
        json.Should().Contain("\"version\":1").And.Contain("\"listViewWasVisible\":true").And.Contain("\"listViewHwnd\":99").And.Contain("\"pid\":5");
        DesktopIconsState.Deserialize<DesktopIconsSnapshot>(json)!.ListViewHwnd.Should().Be(99);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("{not json")]
    [InlineData("[1,2,3]")]
    [InlineData("\0\0\0")]
    public void Corrupt_state_reads_as_null_instead_of_throwing(string? json)
        => DesktopIconsState.Deserialize<DesktopIconsSnapshot>(json).Should().BeNull();

    [Fact]
    public void Corrupt_files_on_disk_do_not_throw()
    {
        Directory.CreateDirectory(_dir);
        var state = new DesktopIconsState(_dir);
        File.WriteAllText(state.SnapshotPath, "garbage");
        File.WriteAllText(state.MarkerPath, "garbage");
        state.ReadSnapshot().Should().BeNull();
        state.ReadMarker().Should().BeNull();
        state.MarkerExists.Should().BeTrue("existence alone is the signal that we hid the list");
    }

    [Fact]
    public void Begin_fails_gracefully_when_the_folder_cannot_be_created()
    {
        var blocker = Path.Combine(Path.GetTempPath(), "bw-test-file-" + Guid.NewGuid().ToString("N"));
        File.WriteAllText(blocker, "x");
        try
        {
            new DesktopIconsState(Path.Combine(blocker, "state")).TryBegin(new DesktopIconsSnapshot(1, true, 1, 1, DateTime.UtcNow)).Should().BeFalse();
        }
        finally
        {
            File.Delete(blocker);
        }
    }

    [Fact]
    public void Restoring_with_nothing_persisted_succeeds_without_touching_windows()
    {
        // No marker and no snapshot: the restorer must return before it looks for any window (so this is safe on any OS and on a machine where we never hid anything).
        DesktopIconsRestorer.Restore(new DesktopIconsState(_dir)).Should().BeTrue();
    }

    [Fact]
    public void RestoreNow_never_throws()
    {
        var act = () => DesktopIconsRestorer.RestoreNow();
        act.Should().NotThrow();
    }
}
