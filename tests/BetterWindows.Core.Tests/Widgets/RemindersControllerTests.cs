using BetterWindows.Core.Controllers;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Desktop;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Widgets;

namespace BetterWindows.Core.Tests.Widgets;

/// <summary>reminders.* calls, the reminders.changed state event and reminders.due, through the router as the overlay and desktop surfaces use them.</summary>
public sealed class RemindersControllerTests : IDisposable
{
    private readonly CoreHarness _h = Kit.Harness<RemindersController>();
    private FakeSurface? _overlay;

    public void Dispose() => _h.Dispose();

    private string StorePath => Path.Combine(_h.Paths.DataDir, "reminders.json");

    /// <summary>Starts the controller (after the test arranged the data directory) and returns the overlay surface, ready.</summary>
    private void StartController()
    {
        _h.Get<RemindersController>().Start();
        _overlay = _h.AddSurface(SurfaceKind.Overlay);
    }

    private string LocalIn(TimeSpan offset) =>
        ReminderRules.FormatDue(TimeZoneInfo.ConvertTime(_h.Time.GetUtcNow().Add(offset), _h.Time.LocalTimeZone).DateTime);

    private async Task<string> ErrorOf(string type, object? payload)
    {
        var r = await _h.CallRawAsync(_overlay!, type, payload);
        Kit.Code(r).Should().NotBe("ok");
        return Kit.Code(r);
    }

    private async Task<Reminder> Add(string title, string? due = null, string? notes = null, string? list = null) =>
        (await _h.CallAsync<Reminder>(_overlay!, Msg.RemindersAdd, new ReminderAddParams { Title = title, Due = due, Notes = notes, List = list }))!;

    private async Task<IReadOnlyList<Reminder>> List() =>
        (await _h.CallAsync<ReminderList>(_overlay!, Msg.RemindersList))!.Items;

    [Fact]
    public async Task Add_returns_the_reminder_with_a_new_guid_and_persists_it()
    {
        StartController();
        var r = await Add("  Call the bank  ", due: "2026-10-30T09:00:00", notes: "bring card", list: " Errands ");
        Guid.TryParseExact(r.Id, "D", out _).Should().BeTrue();
        r.Title.Should().Be("Call the bank");
        r.Due.Should().Be("2026-10-30T09:00", "due times are stored canonically");
        r.Notes.Should().Be("bring card");
        r.List.Should().Be("Errands");
        r.Done.Should().BeFalse();

        (await List()).Should().ContainSingle().Which.Id.Should().Be(r.Id);
        var reloaded = new ReminderStore(StorePath, _h.Time);
        reloaded.Load();
        reloaded.Count.Should().Be(1);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("bad\u0001title")]
    public async Task Add_rejects_empty_or_control_character_titles(string title)
    {
        StartController();
        (await ErrorOf(Msg.RemindersAdd, new ReminderAddParams { Title = title })).Should().Be("badRequest");
        (await List()).Should().BeEmpty();
    }

    [Fact]
    public async Task Add_rejects_an_over_long_title_and_bad_fields()
    {
        StartController();
        (await ErrorOf(Msg.RemindersAdd, new ReminderAddParams { Title = new string('x', ReminderRules.MaxTitleChars + 1) })).Should().Be("badRequest");
        (await ErrorOf(Msg.RemindersAdd, new ReminderAddParams { Title = "ok", Due = "tomorrow" })).Should().Be("badRequest");
        (await ErrorOf(Msg.RemindersAdd, new ReminderAddParams { Title = "ok", List = new string('l', ReminderRules.MaxListChars + 1) })).Should().Be("badRequest");
        (await ErrorOf(Msg.RemindersAdd, new ReminderAddParams { Title = "ok", Notes = "a\u0007" })).Should().Be("badRequest");
        (await List()).Should().BeEmpty();
    }

    [Fact]
    public async Task The_list_is_pushed_as_reminders_changed_after_every_mutation()
    {
        var desktop = _h.AddSurface(SurfaceKind.Desktop, "m1");
        StartController();

        var r = await Add("One");
        _h.Time.Advance(TimeSpan.FromMilliseconds(60));
        desktop.LastEvent<ReminderList>(Msg.RemindersChanged)!.Items.Select(i => i.Id).Should().Equal(r.Id);

        await _h.CallRawAsync(_overlay!, Msg.RemindersRemove, new IdParams { Id = r.Id });
        _h.Time.Advance(TimeSpan.FromMilliseconds(60));
        desktop.LastEvent<ReminderList>(Msg.RemindersChanged)!.Items.Should().BeEmpty();
    }

    [Fact]
    public async Task Update_changes_only_the_given_fields_and_clears_on_empty_strings()
    {
        StartController();
        var r = await Add("Task", due: "2026-10-30T09:00", notes: "n", list: "L");

        var u = await _h.CallAsync<Reminder>(_overlay!, Msg.RemindersUpdate, new ReminderUpdateParams { Id = r.Id, Done = true });
        u!.Title.Should().Be("Task");
        u.Done.Should().BeTrue();
        u.Due.Should().Be("2026-10-30T09:00");

        u = await _h.CallAsync<Reminder>(_overlay!, Msg.RemindersUpdate, new ReminderUpdateParams { Id = r.Id, Due = "", Notes = "", List = "  " });
        u!.Due.Should().BeNull("an empty string clears the field");
        u.Notes.Should().BeNull();
        u.List.Should().BeNull();
        u.Title.Should().Be("Task");
    }

    [Fact]
    public async Task A_failing_update_applies_nothing()
    {
        StartController();
        var r = await Add("Keep");
        (await ErrorOf(Msg.RemindersUpdate, new ReminderUpdateParams { Id = r.Id, Title = "Changed", Due = "someday" })).Should().Be("badRequest");
        (await List()).Single().Title.Should().Be("Keep");
    }

    [Fact]
    public async Task Update_and_remove_reject_unknown_and_malformed_ids()
    {
        StartController();
        (await ErrorOf(Msg.RemindersUpdate, new ReminderUpdateParams { Id = Guid.NewGuid().ToString("D"), Title = "x" })).Should().Be("notFound");
        (await ErrorOf(Msg.RemindersUpdate, new ReminderUpdateParams { Id = "../reminders.json", Title = "x" })).Should().Be("badRequest");
        (await ErrorOf(Msg.RemindersRemove, new IdParams { Id = Guid.NewGuid().ToString("D") })).Should().Be("notFound");
        (await ErrorOf(Msg.RemindersRemove, new IdParams { Id = "nope" })).Should().Be("badRequest");
    }

    [Fact]
    public async Task Remove_deletes_the_reminder()
    {
        StartController();
        var r = await Add("Gone");
        Kit.Code(await _h.CallRawAsync(_overlay!, Msg.RemindersRemove, new IdParams { Id = r.Id })).Should().Be("ok");
        (await List()).Should().BeEmpty();
    }

    [Fact]
    public async Task A_due_reminder_fires_once_as_reminders_due()
    {
        var desktop = _h.AddSurface(SurfaceKind.Desktop, "m1");
        StartController();
        var r = await Add("Ping", due: LocalIn(TimeSpan.FromMinutes(3)));

        _h.Time.Advance(TimeSpan.FromMinutes(2));
        desktop.Events(Msg.RemindersDue).Should().BeEmpty();
        _h.Time.Advance(TimeSpan.FromMinutes(1));
        desktop.Events(Msg.RemindersDue).Should().HaveCount(1);
        desktop.LastEvent<Reminder>(Msg.RemindersDue)!.Id.Should().Be(r.Id);
        _overlay!.Events(Msg.RemindersDue).Should().HaveCount(1, "every reminders surface hears it");

        _h.Time.Advance(TimeSpan.FromHours(3));
        desktop.Events(Msg.RemindersDue).Should().HaveCount(1);
    }

    [Fact]
    public async Task A_new_reminder_already_in_the_past_does_not_fire_the_moment_it_is_saved()
    {
        var desktop = _h.AddSurface(SurfaceKind.Desktop, "m1");
        StartController();
        await Add("Yesterday", due: LocalIn(TimeSpan.FromHours(-5)));
        _h.Time.Advance(TimeSpan.FromHours(1));
        desktop.Events(Msg.RemindersDue).Should().BeEmpty();
    }

    [Fact]
    public async Task Moving_a_completed_reminder_into_the_future_rearms_it()
    {
        var desktop = _h.AddSurface(SurfaceKind.Desktop, "m1");
        StartController();
        var r = await Add("Later", due: LocalIn(TimeSpan.FromMinutes(1)));
        await _h.CallRawAsync(_overlay!, Msg.RemindersUpdate, new ReminderUpdateParams { Id = r.Id, Done = true });
        _h.Time.Advance(TimeSpan.FromMinutes(2));
        desktop.Events(Msg.RemindersDue).Should().BeEmpty("completed reminders never fire");

        await _h.CallRawAsync(_overlay!, Msg.RemindersUpdate, new ReminderUpdateParams { Id = r.Id, Done = false, Due = LocalIn(TimeSpan.FromMinutes(5)) });
        _h.Time.Advance(TimeSpan.FromMinutes(5));
        desktop.Events(Msg.RemindersDue).Should().HaveCount(1);
    }

    [Fact]
    public void Reminders_missed_while_closed_fire_at_start_within_the_catch_up_window()
    {
        // Written by an earlier run of the app: due two hours ago, never announced.
        Directory.CreateDirectory(_h.Paths.DataDir);
        new ReminderStore(StorePath, _h.Time).Add(new StoredReminder
        {
            Id = Guid.NewGuid().ToString("D"), Title = "Missed", Due = LocalIn(TimeSpan.FromHours(-2)),
        });

        var desktop = _h.AddSurface(SurfaceKind.Desktop, "m1");
        _h.Get<RemindersController>().Start();
        desktop.Events(Msg.RemindersDue).Should().ContainSingle();
        desktop.LastEvent<Reminder>(Msg.RemindersDue)!.Title.Should().Be("Missed");
    }
}
