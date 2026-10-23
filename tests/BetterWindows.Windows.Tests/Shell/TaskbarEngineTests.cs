using BetterWindows.Core.Protocol;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Native.Tests.Shell;

/// <summary>Fake taskbar: windows with visibility, the ABS_* state, and a shared operation log.</summary>
internal sealed class FakeTaskbarSurface : ITaskbarSurface
{
    public readonly List<string> Log;
    public readonly List<TaskbarWindowInfo> Windows = new();
    public uint State = 2; // ABS_ALWAYSONTOP, the Windows 11 default
    public bool RefuseSetState;

    public FakeTaskbarSurface(List<string> log, int secondaries = 0)
    {
        Log = log;
        Windows.Add(new TaskbarWindowInfo(100, true, true));
        for (int i = 0; i < secondaries; i++)
        {
            Windows.Add(new TaskbarWindowInfo(200 + i, false, true));
        }
    }

    public IReadOnlyList<TaskbarWindowInfo> GetWindows() => Windows.ToArray();

    public bool TryGetState(out uint state)
    {
        state = State;
        return Windows.Count > 0;
    }

    public bool TrySetState(uint state)
    {
        Log.Add("set-state:" + state);
        if (RefuseSetState)
        {
            return false;
        }

        State = state;
        return true;
    }

    public bool SetVisible(nint handle, bool visible)
    {
        Log.Add((visible ? "show:" : "hide:") + handle);
        int i = Windows.FindIndex(w => w.Handle == handle);
        if (i < 0)
        {
            return false;
        }

        Windows[i] = Windows[i] with { IsVisible = visible };
        return true;
    }

    public bool IsVisible(nint handle) => Windows.First(w => w.Handle == handle).IsVisible;

    /// <summary>What Explorer does by itself: shows its windows again and may clear auto-hide.</summary>
    public void ShellReshows(bool clearAutoHide = false)
    {
        for (int i = 0; i < Windows.Count; i++)
        {
            Windows[i] = Windows[i] with { IsVisible = true };
        }

        if (clearAutoHide)
        {
            State &= ~1u;
        }
    }
}

internal sealed class FakeSnapshotStore : ITaskbarSnapshotStore
{
    private readonly List<string> _log;
    public TaskbarSnapshot? Current;
    public bool Corrupt;
    public bool FailWrite;

    public FakeSnapshotStore(List<string> log)
    {
        _log = log;
    }

    public bool Exists => Current != null || Corrupt;

    public TaskbarSnapshot? TryRead(out bool corrupt)
    {
        corrupt = Corrupt;
        return Corrupt ? null : Current;
    }

    public bool TryWrite(TaskbarSnapshot snapshot)
    {
        if (FailWrite)
        {
            return false;
        }

        _log.Add("write-marker");
        Current = snapshot;
        Corrupt = false;
        return true;
    }

    public void Delete()
    {
        _log.Add("delete-marker");
        Current = null;
        Corrupt = false;
    }
}

public class TaskbarEngineTests
{
    private readonly List<string> _log = new();

    private (FakeTaskbarSurface Surface, FakeSnapshotStore Store, TaskbarEngine Engine) Create(int secondaries = 0, uint state = 2)
    {
        var surface = new FakeTaskbarSurface(_log, secondaries) { State = state };
        var store = new FakeSnapshotStore(_log);
        return (surface, store, new TaskbarEngine(surface, store, NullLogger.Instance));
    }

    [Fact]
    public void Hidden_persists_the_original_state_BEFORE_the_first_mutation()
    {
        var (surface, store, engine) = Create();

        engine.Apply(TaskbarMode.Hidden).Should().BeTrue();

        _log.Should().Equal("write-marker", "set-state:3", "hide:100");
        store.Current!.State.Should().Be(2);
        store.Current.StateKnown.Should().BeTrue();
        store.Current.PrimaryVisible.Should().BeTrue();
        surface.State.Should().Be(3, "auto-hide on, always-on-top kept");
        surface.IsVisible(100).Should().BeFalse();
    }

    [Fact]
    public void Visible_restores_exactly_the_original_state_and_deletes_the_marker()
    {
        var (surface, store, engine) = Create(secondaries: 2, state: 2);
        engine.Apply(TaskbarMode.Hidden);
        _log.Clear();

        engine.Apply(TaskbarMode.Visible).Should().BeTrue();

        surface.State.Should().Be(2);
        surface.Windows.Should().OnlyContain(w => w.IsVisible);
        store.Exists.Should().BeFalse();
        engine.HasSnapshot.Should().BeFalse();
        _log.Should().Contain("delete-marker");
    }

    [Fact]
    public void A_users_own_auto_hide_preference_is_preserved_through_hide_and_restore()
    {
        var (surface, _, engine) = Create(state: 3); // user already had auto-hide ON
        engine.Apply(TaskbarMode.Hidden);
        surface.State.Should().Be(3);
        surface.IsVisible(100).Should().BeFalse();

        engine.Apply(TaskbarMode.Visible);

        surface.State.Should().Be(3);
        surface.IsVisible(100).Should().BeTrue();
    }

    [Fact]
    public void A_taskbar_that_was_not_always_on_top_keeps_that_bit_cleared()
    {
        var (surface, _, engine) = Create(state: 0);
        engine.Apply(TaskbarMode.Hidden);
        surface.State.Should().Be(1);
        engine.Apply(TaskbarMode.Visible);
        surface.State.Should().Be(0);
    }

    [Fact]
    public void AutoHide_sets_only_the_auto_hide_bit_and_leaves_windows_alone()
    {
        var (surface, store, engine) = Create();

        engine.Apply(TaskbarMode.AutoHide).Should().BeTrue();

        surface.State.Should().Be(3);
        surface.IsVisible(100).Should().BeTrue();
        _log.Should().NotContain(l => l.StartsWith("hide:"));
        store.Exists.Should().BeTrue();

        engine.Apply(TaskbarMode.Visible);
        surface.State.Should().Be(2);
        store.Exists.Should().BeFalse();
    }

    [Fact]
    public void Switching_between_Hidden_and_AutoHide_keeps_the_single_original_snapshot()
    {
        var (surface, store, engine) = Create();
        engine.Apply(TaskbarMode.Hidden);
        engine.Apply(TaskbarMode.AutoHide);
        surface.IsVisible(100).Should().BeTrue("AutoHide brings the hidden window back");
        engine.Apply(TaskbarMode.Hidden);
        surface.IsVisible(100).Should().BeFalse();

        _log.Count(l => l == "write-marker").Should().Be(1);
        store.Current!.State.Should().Be(2, "the snapshot still describes the user's original state");
    }

    [Fact]
    public void Secondary_taskbars_are_hidden_and_restored_too()
    {
        var (surface, _, engine) = Create(secondaries: 2);
        engine.Apply(TaskbarMode.Hidden);
        surface.Windows.Should().OnlyContain(w => !w.IsVisible);
        engine.Apply(TaskbarMode.Visible);
        surface.Windows.Should().OnlyContain(w => w.IsVisible);
    }

    [Fact]
    public void A_secondary_taskbar_that_was_hidden_originally_is_not_shown_by_restore()
    {
        var (surface, _, engine) = Create(secondaries: 1);
        surface.Windows[1] = surface.Windows[1] with { IsVisible = false };

        engine.Apply(TaskbarMode.Hidden);
        engine.Apply(TaskbarMode.Visible);

        surface.IsVisible(100).Should().BeTrue();
        surface.IsVisible(200).Should().BeFalse();
    }

    [Fact]
    public void If_the_marker_cannot_be_written_the_taskbar_is_left_untouched()
    {
        var (surface, store, engine) = Create();
        store.FailWrite = true;

        engine.Apply(TaskbarMode.Hidden).Should().BeFalse();

        _log.Should().BeEmpty();
        surface.State.Should().Be(2);
        surface.IsVisible(100).Should().BeTrue();
    }

    [Fact]
    public void Without_Explorer_nothing_happens_and_no_marker_is_written_until_it_appears()
    {
        var (surface, store, engine) = Create();
        surface.Windows.Clear();

        engine.Apply(TaskbarMode.Hidden).Should().BeFalse();
        store.Exists.Should().BeFalse();

        surface.Windows.Add(new TaskbarWindowInfo(100, true, true));
        engine.Apply(TaskbarMode.Hidden).Should().BeTrue();
        surface.IsVisible(100).Should().BeFalse();
    }

    [Fact]
    public void A_stale_marker_from_a_crashed_run_is_restored_first_so_the_new_snapshot_is_the_real_original()
    {
        var (surface, store, engine) = Create(state: 3); // currently: auto-hide ON because the crashed run set it, window hidden
        surface.Windows[0] = surface.Windows[0] with { IsVisible = false };
        store.Current = new TaskbarSnapshot { StateKnown = true, State = 2, PrimaryVisible = true, SecondaryVisible = true };

        engine.Apply(TaskbarMode.Hidden).Should().BeTrue();

        // restored (state 2, window shown) -> snapshot of that -> mutated again
        _log.Should().ContainInOrder("set-state:2", "show:100", "delete-marker", "write-marker", "set-state:3", "hide:100");
        store.Current!.State.Should().Be(2, "the user's original auto-hide=off, not the leftover 3");
    }

    [Fact]
    public void Restore_in_a_fresh_process_uses_the_marker_without_any_prior_Apply()
    {
        var surface = new FakeTaskbarSurface(_log) { State = 3 };
        surface.Windows[0] = surface.Windows[0] with { IsVisible = false };
        var store = new FakeSnapshotStore(_log) { Current = new TaskbarSnapshot { StateKnown = true, State = 2, PrimaryVisible = true } };

        TaskbarEngine.Restore(surface, store, NullLogger.Instance);

        surface.State.Should().Be(2);
        surface.IsVisible(100).Should().BeTrue();
        store.Exists.Should().BeFalse();
    }

    [Fact]
    public void Restore_is_idempotent()
    {
        var (surface, store, engine) = Create();
        engine.Apply(TaskbarMode.Hidden);
        TaskbarEngine.Restore(surface, store, NullLogger.Instance);
        _log.Clear();

        TaskbarEngine.Restore(surface, store, NullLogger.Instance);
        TaskbarEngine.Restore(surface, store, NullLogger.Instance);

        _log.Should().BeEmpty("a visible, restored taskbar must not be touched again");
        surface.State.Should().Be(2);
    }

    [Fact]
    public void Restore_keeps_the_marker_while_Explorer_is_down()
    {
        var (surface, store, engine) = Create();
        engine.Apply(TaskbarMode.Hidden);
        surface.Windows.Clear(); // Explorer crashed

        TaskbarEngine.Restore(surface, store, NullLogger.Instance);
        store.Exists.Should().BeTrue();

        surface.Windows.Add(new TaskbarWindowInfo(300, true, false)); // Explorer is back, new window handle, hidden
        surface.State = 3;
        TaskbarEngine.Restore(surface, store, NullLogger.Instance);

        store.Exists.Should().BeFalse();
        surface.State.Should().Be(2);
        surface.IsVisible(300).Should().BeTrue();
    }

    [Fact]
    public void Without_a_marker_a_visible_taskbar_is_never_touched_even_with_auto_hide_on()
    {
        var (surface, _, _) = Create(state: 3);
        TaskbarEngine.Restore(surface, new FakeSnapshotStore(_log), NullLogger.Instance);
        _log.Should().BeEmpty();
        surface.State.Should().Be(3, "the user's own auto-hide preference");
    }

    [Fact]
    public void Without_a_marker_a_hidden_taskbar_is_made_visible_and_auto_hide_cleared()
    {
        var (surface, _, _) = Create(secondaries: 1, state: 3);
        for (int i = 0; i < surface.Windows.Count; i++)
        {
            surface.Windows[i] = surface.Windows[i] with { IsVisible = false };
        }

        TaskbarEngine.Restore(surface, new FakeSnapshotStore(_log), NullLogger.Instance);

        surface.Windows.Should().OnlyContain(w => w.IsVisible);
        surface.State.Should().Be(2);
    }

    [Fact]
    public void A_corrupt_marker_falls_back_to_the_heuristic_and_is_removed()
    {
        var (surface, store, _) = Create(state: 3);
        surface.Windows[0] = surface.Windows[0] with { IsVisible = false };
        store.Corrupt = true;

        TaskbarEngine.Restore(surface, store, NullLogger.Instance);

        surface.IsVisible(100).Should().BeTrue();
        surface.State.Should().Be(2);
        store.Exists.Should().BeFalse();
    }

    [Fact]
    public void Enforce_hides_a_window_the_shell_showed_again_and_restores_auto_hide()
    {
        var (surface, _, engine) = Create();
        engine.Apply(TaskbarMode.Hidden);
        surface.ShellReshows(clearAutoHide: true);
        _log.Clear();

        engine.Enforce().Should().BeTrue();

        surface.IsVisible(100).Should().BeFalse();
        surface.State.Should().Be(3);
        _log.Should().NotContain("write-marker");
    }

    [Fact]
    public void Enforce_is_a_no_op_when_nothing_changed()
    {
        var (_, _, engine) = Create();
        engine.Apply(TaskbarMode.Hidden);
        _log.Clear();
        engine.Enforce().Should().BeFalse();
        _log.Should().BeEmpty();
    }

    [Fact]
    public void Enforce_after_an_Explorer_restart_hides_the_new_windows_without_a_second_snapshot()
    {
        var (surface, store, engine) = Create(secondaries: 1);
        engine.Apply(TaskbarMode.Hidden);
        // Explorer restarted: new handles, visible, but it loaded our persisted auto-hide state.
        surface.Windows.Clear();
        surface.Windows.Add(new TaskbarWindowInfo(500, true, true));
        surface.Windows.Add(new TaskbarWindowInfo(501, false, true));
        _log.Clear();

        engine.Enforce();

        surface.Windows.Should().OnlyContain(w => !w.IsVisible);
        _log.Should().NotContain("write-marker");
        store.Current!.State.Should().Be(2);
    }

    [Fact]
    public void Failed_state_change_is_logged_not_thrown_and_windows_are_still_handled()
    {
        var (surface, _, engine) = Create();
        surface.RefuseSetState = true;
        var act = () => engine.Apply(TaskbarMode.Hidden);
        act.Should().NotThrow();
    }
}

public class FileTaskbarSnapshotStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-taskbar-test-" + Guid.NewGuid().ToString("N"));

    private FileTaskbarSnapshotStore Store() => new(Path.Combine(_dir, "state", "taskbar.json"));

    public void Dispose()
    {
        try
        {
            Directory.Delete(_dir, recursive: true);
        }
        catch (IOException)
        {
        }
    }

    [Fact]
    public void Round_trip_preserves_every_field_and_leaves_no_temp_file()
    {
        var store = Store();
        var snap = new TaskbarSnapshot { StateKnown = true, State = 3, PrimaryVisible = false, SecondaryVisible = true, ProcessId = 42, CreatedUtc = new DateTime(2026, 1, 2, 3, 4, 5, DateTimeKind.Utc) };

        store.TryWrite(snap).Should().BeTrue();

        store.Exists.Should().BeTrue();
        var read = store.TryRead(out bool corrupt);
        corrupt.Should().BeFalse();
        read.Should().BeEquivalentTo(snap);
        Directory.GetFiles(Path.Combine(_dir, "state")).Should().HaveCount(1);
    }

    [Fact]
    public void Missing_file_reads_as_null_and_not_corrupt()
    {
        var store = Store();
        store.Exists.Should().BeFalse();
        store.TryRead(out bool corrupt).Should().BeNull();
        corrupt.Should().BeFalse();
    }

    [Fact]
    public void Garbage_and_unknown_versions_are_reported_as_corrupt()
    {
        var store = Store();
        Directory.CreateDirectory(Path.Combine(_dir, "state"));
        var path = Path.Combine(_dir, "state", "taskbar.json");

        File.WriteAllText(path, "{ not json");
        store.TryRead(out bool c1).Should().BeNull();
        c1.Should().BeTrue();

        File.WriteAllText(path, "{\"version\":99,\"state\":1}");
        store.TryRead(out bool c2).Should().BeNull();
        c2.Should().BeTrue();

        File.WriteAllText(path, "null");
        store.TryRead(out bool c3).Should().BeNull();
        c3.Should().BeTrue();
    }

    [Fact]
    public void Write_overwrites_and_delete_is_idempotent()
    {
        var store = Store();
        store.TryWrite(new TaskbarSnapshot { State = 1, StateKnown = true });
        store.TryWrite(new TaskbarSnapshot { State = 2, StateKnown = true });
        store.TryRead(out _)!.State.Should().Be(2);

        store.Delete();
        store.Delete();
        store.Exists.Should().BeFalse();
    }

    [Fact]
    public void Default_path_is_under_local_app_data_state_folder()
    {
        var path = FileTaskbarSnapshotStore.DefaultPath();
        path.Should().EndWith(Path.Combine("BetterWindows", "state", "taskbar.json"));
    }
}

public class TaskbarRestorerAndControllerTests
{
    private readonly List<string> _log = new();

    [Fact]
    public void RestoreNow_with_explicit_ports_restores_and_never_throws_on_a_broken_shell()
    {
        var surface = new FakeTaskbarSurface(_log) { State = 3 };
        surface.Windows[0] = surface.Windows[0] with { IsVisible = false };
        var store = new FakeSnapshotStore(_log) { Current = new TaskbarSnapshot { StateKnown = true, State = 2, PrimaryVisible = true } };

        TaskbarRestorer.RestoreNow(surface, store, NullLogger.Instance);

        surface.State.Should().Be(2);
        surface.IsVisible(100).Should().BeTrue();

        var act = () => TaskbarRestorer.RestoreNow(new ThrowingSurface(), store, NullLogger.Instance);
        act.Should().NotThrow();
    }

    [Fact]
    public void Controller_AutoHide_then_Visible_round_trips_without_a_watcher_thread()
    {
        var surface = new FakeTaskbarSurface(_log);
        var store = new FakeSnapshotStore(_log);
        using var controller = new Win32TaskbarController(NullLogger.Instance, surface, store);
        controller.Current.Should().Be(TaskbarMode.Visible);

        controller.Apply(TaskbarMode.AutoHide);
        controller.Current.Should().Be(TaskbarMode.AutoHide);
        surface.State.Should().Be(3);
        store.Exists.Should().BeTrue();

        controller.Apply(TaskbarMode.Visible);
        controller.Current.Should().Be(TaskbarMode.Visible);
        surface.State.Should().Be(2);
        store.Exists.Should().BeFalse();
    }

    [Fact]
    public void Controller_Restore_is_idempotent_and_Dispose_restores()
    {
        var surface = new FakeTaskbarSurface(_log);
        var store = new FakeSnapshotStore(_log);
        var controller = new Win32TaskbarController(NullLogger.Instance, surface, store);
        controller.Apply(TaskbarMode.AutoHide);

        controller.Restore();
        controller.Restore();
        surface.State.Should().Be(2);

        controller.Apply(TaskbarMode.AutoHide);
        controller.Dispose();
        surface.State.Should().Be(2);
        store.Exists.Should().BeFalse();
        controller.Apply(TaskbarMode.AutoHide); // after dispose: ignored
        surface.State.Should().Be(2);
    }

    [Fact]
    public void After_an_external_RestoreNow_the_next_Apply_writes_a_fresh_marker_instead_of_trusting_the_stale_snapshot()
    {
        var surface = new FakeTaskbarSurface(_log);
        var store = new FakeSnapshotStore(_log);
        using var controller = new Win32TaskbarController(NullLogger.Instance, surface, store);
        controller.Apply(TaskbarMode.AutoHide);
        store.Exists.Should().BeTrue();
        long stampBefore = TaskbarRestorer.Stamp;

        // A crash handler / rescue command restores behind the controller's back.
        TaskbarRestorer.RestoreNow(surface, store, NullLogger.Instance);
        TaskbarRestorer.Stamp.Should().BeGreaterThan(stampBefore);
        store.Exists.Should().BeFalse();
        surface.State.Should().Be(2);

        controller.Apply(TaskbarMode.AutoHide);

        surface.State.Should().Be(3);
        store.Exists.Should().BeTrue("a mutation must always be covered by a marker on disk");
        controller.Restore();
        store.Exists.Should().BeFalse();
        surface.State.Should().Be(2);
    }

    [Fact]
    public void Visible_without_any_change_does_not_touch_a_healthy_taskbar()
    {
        var surface = new FakeTaskbarSurface(_log);
        using var controller = new Win32TaskbarController(NullLogger.Instance, surface, new FakeSnapshotStore(_log));
        controller.Apply(TaskbarMode.Visible);
        _log.Should().BeEmpty();
    }

    private sealed class ThrowingSurface : ITaskbarSurface
    {
        public IReadOnlyList<TaskbarWindowInfo> GetWindows() => throw new InvalidOperationException("explorer is hung");
        public bool TryGetState(out uint state) => throw new InvalidOperationException();
        public bool TrySetState(uint state) => throw new InvalidOperationException();
        public bool SetVisible(nint handle, bool visible) => throw new InvalidOperationException();
    }
}
