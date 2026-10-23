using BetterWindows.Core.Protocol;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Widgets;

namespace BetterWindows.Core.Tests.Widgets;

/// <summary>The due scheduler: exactly-once firing, catch-up window, re-arming on store changes and the retry back-off on write failures.</summary>
public sealed class ReminderSchedulerTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-sched-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private readonly List<Reminder> _fired = new();
    private readonly ReminderStore _store;
    private readonly ReminderScheduler _sched;

    public ReminderSchedulerTests()
    {
        _store = new ReminderStore(Path.Combine(_dir, "reminders.json"), _time);
        _store.Load();
        _sched = new ReminderScheduler(_store, _time, r => _fired.Add(r));
    }

    public void Dispose()
    {
        _sched.Dispose();
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    /// <summary>A local due time relative to the manual clock, in the form the UI sends.</summary>
    private string DueIn(TimeSpan offset) =>
        ReminderRules.FormatDue(TimeZoneInfo.ConvertTime(_time.GetUtcNow().Add(offset), _time.LocalTimeZone).DateTime);

    private StoredReminder Add(string title, TimeSpan? offset, bool done = false)
    {
        var item = new StoredReminder { Id = Guid.NewGuid().ToString("D"), Title = title, Due = offset is null ? null : DueIn(offset.Value), Done = done };
        return _store.Add(item)!;
    }

    [Fact]
    public void A_reminder_fires_once_when_it_is_due()
    {
        Add("Standup", TimeSpan.FromMinutes(5));
        _sched.Start();

        _time.Advance(TimeSpan.FromMinutes(4));
        _fired.Should().BeEmpty();
        _time.Advance(TimeSpan.FromMinutes(1));
        _fired.Select(r => r.Title).Should().Equal("Standup");
        _time.Advance(TimeSpan.FromHours(2));
        _fired.Should().HaveCount(1, "it fires exactly once");
    }

    [Fact]
    public void Completed_and_undated_reminders_never_fire()
    {
        Add("Done already", TimeSpan.FromMinutes(1), done: true);
        Add("No time", null);
        _sched.Start();
        _time.Advance(TimeSpan.FromDays(2));
        _fired.Should().BeEmpty();
    }

    [Fact]
    public void A_reminder_missed_while_the_app_was_closed_fires_at_start_within_24_hours()
    {
        Add("Missed", TimeSpan.FromHours(-2));
        _sched.Start();
        _fired.Select(r => r.Title).Should().Equal("Missed");
        _time.Advance(TimeSpan.FromHours(1));
        _fired.Should().HaveCount(1);
    }

    [Fact]
    public void A_reminder_older_than_the_catch_up_window_is_flagged_silently()
    {
        var old = Add("Ancient", TimeSpan.FromHours(-30));
        _sched.Start();
        _fired.Should().BeEmpty();
        _store.Snapshot().Single(r => r.Id == old.Id).Notified.Should().BeTrue();
    }

    [Fact]
    public void Adding_a_reminder_and_calling_Rearm_schedules_it()
    {
        _sched.Start();
        _time.Advance(TimeSpan.FromMinutes(10));
        Add("Later", TimeSpan.FromMinutes(3));
        _sched.Rearm();
        _time.Advance(TimeSpan.FromMinutes(2));
        _fired.Should().BeEmpty();
        _time.Advance(TimeSpan.FromMinutes(1));
        _fired.Select(r => r.Title).Should().Equal("Later");
    }

    [Fact]
    public void Rescheduling_a_reminder_moves_its_firing()
    {
        var r = Add("Moved", TimeSpan.FromMinutes(2));
        _sched.Start();
        _store.Update(r.Id, x => x with { Due = DueIn(TimeSpan.FromMinutes(30)), Notified = false });
        _sched.Rearm();
        _time.Advance(TimeSpan.FromMinutes(10));
        _fired.Should().BeEmpty("the old time no longer counts");
        _time.Advance(TimeSpan.FromMinutes(20));
        _fired.Should().HaveCount(1);
    }

    [Fact]
    public void Disposing_stops_all_firing()
    {
        Add("Silent", TimeSpan.FromMinutes(1));
        _sched.Start();
        _sched.Dispose();
        _time.Advance(TimeSpan.FromMinutes(5));
        _fired.Should().BeEmpty();
    }

    [Fact]
    public void A_reminder_whose_flag_cannot_be_saved_is_retried_with_back_off_and_never_spins()
    {
        // Replace the file by a directory: the store can no longer write it (the rename onto a directory fails), so the flag cannot be persisted.
        Add("Stuck", TimeSpan.FromMinutes(-1));
        var path = Path.Combine(_dir, "reminders.json");
        File.Delete(path);
        Directory.CreateDirectory(path);

        _sched.Start(); // the failing pass
        // Without a back-off this advance would never return (the due reminder would be re-armed at once, forever).
        _time.Advance(TimeSpan.FromMinutes(1));
        _fired.Should().BeEmpty("nothing fires before the flag is persisted");

        // The file is writable again: the next retry persists the flag and fires exactly once.
        Directory.Delete(path);
        _time.Advance(TimeSpan.FromMinutes(1));
        _fired.Select(r => r.Title).Should().Equal("Stuck");
        _time.Advance(TimeSpan.FromHours(1));
        _fired.Should().HaveCount(1);
    }
}
