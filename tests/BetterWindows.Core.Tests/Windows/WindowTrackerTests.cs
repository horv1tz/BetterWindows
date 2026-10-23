using BetterWindows.Core.Platform;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Dock;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;

namespace BetterWindows.Core.Tests.WindowTracking;

public class WindowTrackerTests : IDisposable
{
    private readonly FakeWindowSystem _ws = new();
    private readonly FakeAppCatalog _catalog = new();
    private readonly ManualTimeProvider _time = new();
    private readonly WindowTracker _tracker;
    private readonly List<(WindowSnapshot Snapshot, long At)> _raised = new();
    private readonly List<TrackedWindow?> _foregrounds = new();

    public WindowTrackerTests()
    {
        _tracker = new WindowTracker(_ws, new AppIdentityResolver(_catalog), _catalog, _time);
        _tracker.Changed += s => { lock (_raised) _raised.Add((s, _time.GetTimestamp())); };
        _tracker.ForegroundChanged += f => { lock (_foregrounds) _foregrounds.Add(f); };
    }

    public void Dispose() => _tracker.Dispose();

    private static RawWindow App(long handle, string title = "Window", string exe = @"C:\Apps\a\a.exe", int pid = 100) => FakeWindowSystem.Win(handle, title, exe, null, pid);
    private void Settle() => _time.Advance(TimeSpan.FromMilliseconds(250));
    private long[] Handles() => _tracker.Snapshot.Windows.Select(w => w.Handle).ToArray();

    // ---- start / stop ----------------------------------------------------------------------------------------------------------------

    [Fact]
    public void Start_starts_the_window_system_and_takes_the_first_snapshot_in_z_order()
    {
        _ws.Add(App(1, "top")); _ws.Add(App(2, "middle", @"C:\Apps\b\b.exe", 200)); _ws.Add(App(3, "bottom", @"C:\Apps\c\c.exe", 300));
        _tracker.Start();
        _ws.Started.Should().BeTrue();
        Handles().Should().Equal(1, 2, 3);
        _tracker.Snapshot.Version.Should().BeGreaterThan(0);
        _tracker.Windows.Should().HaveCount(3);
    }

    [Fact]
    public void Start_filters_windows_the_dock_must_not_show()
    {
        _ws.Add(App(1, "Real"));
        _ws.Add(App(2, "Tool") with { IsToolWindow = true });
        _ws.Add(App(3, "Desktop") with { ClassName = "Progman" });
        _ws.Add(App(4, "Ours") with { IsOwnProcess = true });
        _ws.Add(App(5, "Hidden") with { IsVisible = false });
        _tracker.Start();
        Handles().Should().Equal(1);
    }

    [Fact]
    public void Start_puts_the_current_foreground_window_first()
    {
        _ws.Add(App(1)); _ws.Add(App(2, exe: @"C:\Apps\b\b.exe", pid: 200));
        _ws.Foreground = 2;
        _tracker.Start();
        Handles().Should().Equal(2, 1);
        _tracker.Foreground!.Handle.Should().Be(2);
        _tracker.Snapshot.Foreground.Should().BeSameAs(_tracker.Snapshot.Windows[0], "Windows and Foreground come from one consistent snapshot");
    }

    [Fact]
    public void Start_and_Stop_are_idempotent()
    {
        _ws.Add(App(1));
        _tracker.Start(); _tracker.Start();
        Handles().Should().Equal(1);
        _tracker.Stop(); _tracker.Stop();
        _ws.Started.Should().BeFalse();
        _tracker.Start();
        _ws.Started.Should().BeTrue();
    }

    [Fact]
    public void Events_after_Stop_are_ignored()
    {
        _tracker.Start();
        _tracker.Stop();
        _ws.Add(App(1));
        _ws.Raise(WindowEventKind.Created, 1);
        Settle();
        Handles().Should().BeEmpty();
    }

    [Fact]
    public void Start_never_throws_when_the_window_system_fails()
    {
        var tracker = new WindowTracker(new ExplodingWindowSystem(), new AppIdentityResolver(_catalog), _catalog, _time);
        var act = () => tracker.Start();
        act.Should().NotThrow();
        tracker.Snapshot.Windows.Should().BeEmpty();
        tracker.Stop();
    }

    [Fact]
    public void The_service_name_is_stable() => _tracker.Name.Should().Be("WindowTracker");

    // ---- membership ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_created_window_appears_and_a_destroyed_one_disappears()
    {
        _tracker.Start();
        _ws.Add(App(7, "New"));
        _ws.Raise(WindowEventKind.Created, 7);
        Settle();
        Handles().Should().Equal(7);
        _tracker.Find(7)!.Raw.Title.Should().Be("New");

        _ws.Remove(7);
        _ws.Raise(WindowEventKind.Destroyed, 7);
        Settle();
        Handles().Should().BeEmpty();
        _tracker.Find(7).Should().BeNull();
    }

    [Fact]
    public void Destroyed_removes_the_window_even_if_the_port_still_describes_it()
    {
        _ws.Add(App(7));
        _tracker.Start();
        _ws.Raise(WindowEventKind.Destroyed, 7); // the Remove has not happened yet
        Settle();
        Handles().Should().BeEmpty();
    }

    [Fact]
    public void A_title_change_updates_the_tracked_window()
    {
        _ws.Add(App(1, "Old"));
        _tracker.Start();
        _ws.Update(1, w => w with { Title = "New title" });
        _ws.Raise(WindowEventKind.TitleChanged, 1);
        Settle();
        _tracker.Find(1)!.Raw.Title.Should().Be("New title");
    }

    [Fact]
    public void Minimize_and_restore_are_reflected()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _ws.Update(1, w => w with { IsMinimized = true });
        _ws.Raise(WindowEventKind.Minimized, 1);
        Settle();
        _tracker.Find(1)!.Raw.IsMinimized.Should().BeTrue();
        _ws.Update(1, w => w with { IsMinimized = false });
        _ws.Raise(WindowEventKind.Restored, 1);
        Settle();
        _tracker.Find(1)!.Raw.IsMinimized.Should().BeFalse();
    }

    [Fact]
    public void A_window_cloaked_by_the_app_is_a_ghost_and_comes_back_when_uncloaked()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _ws.Update(1, w => w with { IsCloaked = true, CloakedFlags = 1 });
        _ws.Raise(WindowEventKind.Cloaked, 1);
        Settle();
        Handles().Should().BeEmpty();
        _ws.Update(1, w => w with { IsCloaked = false, CloakedFlags = 0 });
        _ws.Raise(WindowEventKind.Uncloaked, 1);
        Settle();
        Handles().Should().Equal(1);
    }

    [Fact]
    public void A_window_cloaked_only_by_the_shell_lives_on_another_virtual_desktop_and_stays_tracked()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _ws.Update(1, w => w with { IsCloaked = true, CloakedFlags = 2, IsCloakedByShell = true });
        _ws.Raise(WindowEventKind.Cloaked, 1);
        Settle();
        Handles().Should().Equal(1);
        _tracker.Find(1)!.Raw.IsCloakedByShell.Should().BeTrue();
    }

    [Fact]
    public void Hiding_a_window_removes_it_and_showing_it_adds_it_back()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _ws.Update(1, w => w with { IsVisible = false });
        _ws.Raise(WindowEventKind.Hidden, 1);
        Settle();
        Handles().Should().BeEmpty();
        _ws.Update(1, w => w with { IsVisible = true });
        _ws.Raise(WindowEventKind.Shown, 1);
        Settle();
        Handles().Should().Equal(1);
    }

    [Fact]
    public void A_window_that_becomes_ineligible_is_dropped()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _ws.Update(1, w => w with { IsToolWindow = true });
        _ws.Raise(WindowEventKind.Redraw, 1);
        Settle();
        Handles().Should().BeEmpty();
    }

    [Fact]
    public void Flash_requests_set_and_clear_IsFlashing()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _ws.Update(1, w => w with { IsFlashing = true });
        _ws.Raise(WindowEventKind.Flashed, 1);
        Settle();
        _tracker.Find(1)!.Raw.IsFlashing.Should().BeTrue();
        _ws.Update(1, w => w with { IsFlashing = false });
        _ws.Raise(WindowEventKind.Flashed, 1);
        Settle();
        _tracker.Find(1)!.Raw.IsFlashing.Should().BeFalse();
    }

    [Fact]
    public void Moves_of_tracked_windows_refresh_the_bounds_but_moves_of_unknown_windows_cost_nothing()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _ws.Update(1, w => w with { Bounds = new NativeRect(0, 0, 50, 50) });
        _ws.Raise(WindowEventKind.Moved, 1);
        _ws.Raise(WindowEventKind.Moved, 999);
        _time.Advance(TimeSpan.FromMilliseconds(120));
        _tracker.Find(1)!.Raw.Bounds.Should().Be(new NativeRect(0, 0, 50, 50));
        Handles().Should().Equal(1);
    }

    [Fact]
    public void Elevated_windows_are_tracked_with_their_flag()
    {
        _ws.Add(App(1) with { IsElevated = true });
        _tracker.Start();
        _tracker.Find(1)!.Raw.IsElevated.Should().BeTrue();
    }

    [Fact]
    public void Identity_is_attached_to_every_tracked_window()
    {
        var entry = _catalog.Add("Alpha", path: @"C:\Apps\a\a.exe");
        _ws.Add(App(1)); _ws.Add(App(2, exe: @"C:\Apps\z\zed_tool.exe", pid: 5));
        _tracker.Start();
        _tracker.Find(1)!.AppId.Should().Be(entry.Id);
        _tracker.Find(1)!.AppName.Should().Be("Alpha");
        _tracker.Find(2)!.AppId.Should().Be(@"exe:c:\apps\z\zed_tool.exe");
        _tracker.Find(2)!.AppName.Should().Be("Zed Tool");
    }

    [Fact]
    public void A_catalog_change_re_resolves_the_identity_of_tracked_windows()
    {
        _ws.Add(App(1));
        _tracker.Start();
        _tracker.Find(1)!.AppId.Should().Be(@"exe:c:\apps\a\a.exe");
        var entry = _catalog.Add("Alpha", aumid: "Vendor.Alpha!App", path: @"C:\Apps\a\a.exe");
        _catalog.RaiseChanged();
        Settle();
        _tracker.Find(1)!.AppId.Should().Be(entry.Id);
        _tracker.Find(1)!.AppName.Should().Be("Alpha");
    }

    // ---- foreground and MRU ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_foreground_event_moves_the_window_to_the_front_and_raises_ForegroundChanged()
    {
        _ws.Add(App(1)); _ws.Add(App(2, exe: @"C:\Apps\b\b.exe", pid: 2)); _ws.Add(App(3, exe: @"C:\Apps\c\c.exe", pid: 3));
        _tracker.Start();
        _foregrounds.Clear();
        _ws.Foreground = 3;
        _ws.Raise(WindowEventKind.Foreground, 3);
        Settle();
        Handles().Should().Equal(3, 1, 2);
        _tracker.Foreground!.Handle.Should().Be(3);
        _foregrounds.Select(f => f?.Handle).Should().Equal(3L);
    }

    [Fact]
    public void MRU_order_follows_the_activation_history()
    {
        _ws.Add(App(1)); _ws.Add(App(2, pid: 2)); _ws.Add(App(3, pid: 3));
        _tracker.Start();
        foreach (var h in new long[] { 3, 2, 3, 1 })
        {
            _ws.Raise(WindowEventKind.Foreground, h);
            Settle();
        }
        Handles().Should().Equal(1, 3, 2);
    }

    [Fact]
    public void Several_foreground_events_in_one_batch_keep_their_order()
    {
        _ws.Add(App(1)); _ws.Add(App(2, pid: 2)); _ws.Add(App(3, pid: 3));
        _tracker.Start();
        _ws.Raise(WindowEventKind.Foreground, 2);
        _ws.Raise(WindowEventKind.Foreground, 3);
        _ws.Raise(WindowEventKind.Foreground, 2);
        Settle();
        Handles().Should().Equal(2, 3, 1);
        _tracker.Foreground!.Handle.Should().Be(2);
    }

    [Fact]
    public void Activating_our_own_window_keeps_the_previous_foreground()
    {
        _ws.Add(App(1)); _ws.Add(App(99, "Overlay", @"C:\bw\bw.exe", pid: 9) with { IsOwnProcess = true });
        _ws.Foreground = 1;
        _tracker.Start();
        _ws.Raise(WindowEventKind.Foreground, 99);
        Settle();
        _tracker.Foreground!.Handle.Should().Be(1);
        _tracker.Find(99).Should().BeNull("handles of our own windows are never exposed");
    }

    [Fact]
    public void Activating_a_non_eligible_window_keeps_the_previous_foreground()
    {
        _ws.Add(App(1)); _ws.Add(App(50, "Start") with { ClassName = "Windows.UI.Core.CoreWindow" });
        _ws.Foreground = 1;
        _tracker.Start();
        _ws.Raise(WindowEventKind.Foreground, 50);
        Settle();
        _tracker.Foreground!.Handle.Should().Be(1);
    }

    [Fact]
    public void A_dialog_owned_by_a_tracked_window_counts_as_its_owner_being_active()
    {
        _ws.Add(App(1)); _ws.Add(App(2, exe: @"C:\Apps\b\b.exe", pid: 2));
        _ws.Add(App(20, "Save as") with { OwnerHandle = 2, IsToolWindow = true });
        _ws.Foreground = 1;
        _tracker.Start();
        _ws.Raise(WindowEventKind.Foreground, 20);
        Settle();
        _tracker.Foreground!.Handle.Should().Be(2);
        Handles().Should().Equal(2, 1);
    }

    [Fact]
    public void When_the_foreground_window_is_destroyed_the_foreground_becomes_null_until_the_next_event()
    {
        _ws.Add(App(1)); _ws.Add(App(2, pid: 2));
        _ws.Foreground = 1;
        _tracker.Start();
        _foregrounds.Clear();
        _ws.Remove(1);
        _ws.Raise(WindowEventKind.Destroyed, 1);
        Settle();
        _tracker.Foreground.Should().BeNull();
        _foregrounds.Should().ContainSingle().Which.Should().BeNull();
        _ws.Raise(WindowEventKind.Foreground, 2);
        Settle();
        _tracker.Foreground!.Handle.Should().Be(2);
    }

    [Fact]
    public void A_new_window_that_does_not_take_the_foreground_joins_at_the_end()
    {
        _ws.Add(App(1)); _ws.Foreground = 1;
        _tracker.Start();
        _ws.Add(App(2, pid: 2));
        _ws.Raise(WindowEventKind.Created, 2);
        Settle();
        Handles().Should().Equal(1, 2);
        _tracker.Foreground!.Handle.Should().Be(1);
    }

    [Fact]
    public void A_new_window_that_already_holds_the_foreground_goes_to_the_front()
    {
        _ws.Add(App(1)); _ws.Foreground = 1;
        _tracker.Start();
        _ws.Add(App(2, pid: 2));
        _ws.Foreground = 2;
        _ws.Raise(WindowEventKind.Created, 2);
        Settle();
        Handles().Should().Equal(2, 1);
        _tracker.Foreground!.Handle.Should().Be(2);
    }

    [Fact]
    public void A_foreground_event_that_arrives_before_the_window_is_eligible_still_activates_it_once_it_is_known()
    {
        _tracker.Start();
        _ws.Add(App(5, pid: 5));
        _ws.Raise(WindowEventKind.Foreground, 5); // no Created event was ever seen
        Settle();
        Handles().Should().Equal(5);
        _tracker.Foreground!.Handle.Should().Be(5);
    }

    // ---- snapshot semantics ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void Every_change_publishes_a_new_immutable_snapshot_with_a_higher_version()
    {
        _ws.Add(App(1));
        _tracker.Start();
        var first = _tracker.Snapshot;
        _ws.Add(App(2, pid: 2));
        _ws.Raise(WindowEventKind.Created, 2);
        Settle();
        var second = _tracker.Snapshot;
        second.Version.Should().BeGreaterThan(first.Version);
        first.Windows.Should().HaveCount(1, "published snapshots are never mutated");
        second.Windows.Should().HaveCount(2);
    }

    [Fact]
    public void An_event_that_changes_nothing_publishes_nothing()
    {
        _ws.Add(App(1));
        _tracker.Start();
        Settle();
        var v = _tracker.Snapshot.Version;
        var raises = _raised.Count;
        _ws.Raise(WindowEventKind.Redraw, 1);
        Settle();
        _tracker.Snapshot.Version.Should().Be(v);
        _raised.Count.Should().Be(raises);
    }

    [Fact]
    public void Find_returns_null_for_unknown_handles_and_ours()
    {
        _ws.Add(App(1)); _ws.Add(App(2, "Ours") with { IsOwnProcess = true });
        _tracker.Start();
        _tracker.Find(1).Should().NotBeNull();
        _tracker.Find(2).Should().BeNull();
        _tracker.Find(12345).Should().BeNull();
        _tracker.Find(0).Should().BeNull();
    }

    // ---- Changed coalescing ----------------------------------------------------------------------------------------------------------

    [Fact]
    public void A_burst_of_events_in_one_batch_raises_Changed_once()
    {
        _tracker.Start();
        _raised.Clear();
        for (long h = 1; h <= 50; h++) { _ws.Add(App(h, "w" + h, pid: (int)h)); _ws.Raise(WindowEventKind.Created, h); }
        Settle();
        _tracker.Windows.Should().HaveCount(50);
        _raised.Should().HaveCount(1);
        _raised[0].Snapshot.Windows.Should().HaveCount(50);
    }

    [Fact]
    public void Changed_is_never_raised_twice_within_30_ms_and_the_last_change_is_never_lost()
    {
        _tracker.Start();
        Settle();
        _raised.Clear();
        for (long h = 1; h <= 6; h++)
        {
            _ws.Add(App(h, "w" + h, pid: (int)h));
            _ws.Raise(WindowEventKind.Created, h);
            _time.Advance(TimeSpan.FromMilliseconds(8)); // one batch per event
        }
        Settle();
        _raised.Count.Should().BeGreaterThan(1).And.BeLessThan(6, "events inside one 30 ms window are coalesced");
        for (var i = 1; i < _raised.Count; i++)
            _time.GetElapsedTime(_raised[i - 1].At, _raised[i].At).Should().BeGreaterOrEqualTo(WindowTracker.ChangedInterval);
        _raised[^1].Snapshot.Windows.Should().HaveCount(6, "the trailing raise delivers the newest snapshot");
        _tracker.Windows.Should().HaveCount(6);
    }

    [Fact]
    public void Changed_is_raised_immediately_after_a_quiet_period()
    {
        _tracker.Start();
        Settle();
        _raised.Clear();
        _ws.Add(App(1));
        _ws.Raise(WindowEventKind.Created, 1);
        _time.Advance(WindowTracker.UrgentDelay);
        _raised.Should().ContainSingle("no 30 ms wait for the first change after a quiet period");
    }

    [Fact]
    public void A_throwing_subscriber_does_not_break_the_tracker_or_other_subscribers()
    {
        var other = 0;
        _tracker.Changed += _ => throw new InvalidOperationException("boom");
        _tracker.ForegroundChanged += _ => throw new InvalidOperationException("boom");
        _tracker.Changed += _ => other++;
        _tracker.Start();
        _ws.Add(App(1));
        _ws.Raise(WindowEventKind.Created, 1);
        _ws.Raise(WindowEventKind.Foreground, 1);
        Settle();
        Handles().Should().Equal(1);
        other.Should().BeGreaterThan(0);
    }

    // ---- reconcile ------------------------------------------------------------------------------------------------------------------

    [Fact]
    public void The_periodic_reconcile_heals_missed_creates_and_destroys()
    {
        _ws.Add(App(1)); _ws.Add(App(2, pid: 2));
        _tracker.Start();
        _ws.Add(App(3, pid: 3));   // no event
        _ws.Remove(1);             // no event
        _time.Advance(WindowTracker.ReconcileInterval + TimeSpan.FromMilliseconds(100));
        Handles().Should().BeEquivalentTo(new long[] { 2, 3 });
    }

    [Fact]
    public void Reconcile_keeps_the_MRU_order_of_known_windows()
    {
        _ws.Add(App(1)); _ws.Add(App(2, pid: 2)); _ws.Add(App(3, pid: 3));
        _tracker.Start();
        _ws.Raise(WindowEventKind.Foreground, 3);
        Settle();
        Handles().Should().Equal(3, 1, 2);
        _time.Advance(WindowTracker.ReconcileInterval + TimeSpan.FromMilliseconds(100));
        Handles().Should().Equal(3, 1, 2);
    }

    [Fact]
    public void Reconcile_also_heals_a_missed_foreground_event()
    {
        _ws.Add(App(1)); _ws.Add(App(2, pid: 2));
        _ws.Foreground = 1;
        _tracker.Start();
        _ws.Foreground = 2; // the Foreground event was lost
        _time.Advance(WindowTracker.ReconcileInterval + TimeSpan.FromMilliseconds(100));
        _tracker.Foreground!.Handle.Should().Be(2);
    }

    [Fact]
    public void Reconcile_picks_up_a_window_that_became_eligible_without_an_event()
    {
        _ws.Add(App(1, "") );
        _tracker.Start();
        Handles().Should().BeEmpty("no title yet");
        _ws.Update(1, w => w with { Title = "Now it has one" });
        _time.Advance(WindowTracker.ReconcileInterval + TimeSpan.FromMilliseconds(100));
        Handles().Should().Equal(1);
    }

    [Fact]
    public void A_failing_enumeration_keeps_the_current_list()
    {
        var ws = new FlakyWindowSystem();
        ws.Windows.Add(App(1));
        var tracker = new WindowTracker(ws, new AppIdentityResolver(_catalog), _catalog, _time);
        tracker.Start();
        ws.FailEnumeration = true;
        _time.Advance(WindowTracker.ReconcileInterval + TimeSpan.FromMilliseconds(100));
        tracker.Windows.Should().HaveCount(1);
        tracker.Dispose();
    }

    [Fact]
    public void A_describe_that_throws_does_not_drop_the_window()
    {
        var ws = new FlakyWindowSystem();
        ws.Windows.Add(App(1));
        var tracker = new WindowTracker(ws, new AppIdentityResolver(_catalog), _catalog, _time);
        tracker.Start();
        ws.FailDescribe = true;
        ws.Raise(WindowEventKind.TitleChanged, 1);
        _time.Advance(TimeSpan.FromMilliseconds(250));
        tracker.Windows.Should().HaveCount(1, "a port failure is not proof that the window is gone");
        tracker.Dispose();
    }

    [Fact]
    public void An_event_storm_falls_back_to_one_full_enumeration()
    {
        _tracker.Start();
        for (long h = 1; h <= 5000; h++) { _ws.Add(App(h, "w", pid: 1)); }
        for (long h = 1; h <= 5000; h++) _ws.Raise(WindowEventKind.Created, h);
        _time.Advance(TimeSpan.FromMilliseconds(300));
        _tracker.Windows.Should().HaveCount(5000);
    }

    [Fact]
    public void Events_for_handle_zero_are_ignored()
    {
        _tracker.Start();
        _ws.Raise(WindowEventKind.Created, 0);
        Settle();
        Handles().Should().BeEmpty();
    }

    [Fact]
    public async Task Concurrent_events_from_many_threads_leave_a_consistent_snapshot()
    {
        for (long h = 1; h <= 200; h++) _ws.Add(App(h, "w" + h, pid: (int)h));
        _tracker.Start();
        _ws.Foreground = 17;
        var kinds = new[] { WindowEventKind.Created, WindowEventKind.TitleChanged, WindowEventKind.Foreground, WindowEventKind.Redraw, WindowEventKind.Shown };
        var tasks = Enumerable.Range(0, 8).Select(t => Task.Run(() =>
        {
            var rnd = new Random(t);
            for (var i = 0; i < 2000; i++) _ws.Raise(kinds[rnd.Next(kinds.Length)], 1 + rnd.Next(200));
        })).ToArray();
        while (!tasks.All(t => t.IsCompleted)) { _time.Advance(TimeSpan.FromMilliseconds(5)); await Task.Delay(1); }
        await Task.WhenAll(tasks);
        Settle();

        var snap = _tracker.Snapshot;
        snap.Windows.Select(w => w.Handle).Should().OnlyHaveUniqueItems().And.HaveCount(200);
        snap.Foreground.Should().NotBeNull();
        snap.Windows.Should().Contain(snap.Foreground!);
        snap.Windows[0].Should().BeSameAs(snap.Foreground, "the foreground window is the most recently activated one");
    }

    // ---- helpers ---------------------------------------------------------------------------------------------------------------------

    /// <summary>FakeWindowSystem whose enumeration / describe can be made to throw (re-implements the interface because the fake's members are not virtual).</summary>
    private sealed class FlakyWindowSystem : FakeWindowSystem, IWindowSystem
    {
        public bool FailEnumeration { get; set; }
        public bool FailDescribe { get; set; }
        public new IReadOnlyList<RawWindow> EnumerateTopLevel() => FailEnumeration ? throw new NotSupportedException("enumeration failed") : base.EnumerateTopLevel();
        public new RawWindow? TryDescribe(long handle) => FailDescribe ? throw new NotSupportedException("describe failed") : base.TryDescribe(handle);
    }

    private sealed class ExplodingWindowSystem : IWindowSystem
    {
        public IReadOnlyList<RawWindow> EnumerateTopLevel() => throw new InvalidOperationException("enumerate");
        public RawWindow? TryDescribe(long handle) => throw new InvalidOperationException("describe");
        public long GetForegroundWindow() => throw new InvalidOperationException("foreground");
        public event Action<WindowEvent>? WindowChanged { add { } remove { } }
        public void Start() => throw new InvalidOperationException("start");
        public void Stop() => throw new InvalidOperationException("stop");
        public bool Activate(long handle) => false;
        public bool Minimize(long handle) => false;
        public bool Restore(long handle) => false;
        public bool Maximize(long handle) => false;
        public bool Close(long handle) => false;
        public bool IsFullscreenWindowActive(BetterWindows.Core.Protocol.MonitorInfo monitor) => false;
        public event Action? FullscreenChanged { add { } remove { } }
        public void Dispose() { }
    }
}
