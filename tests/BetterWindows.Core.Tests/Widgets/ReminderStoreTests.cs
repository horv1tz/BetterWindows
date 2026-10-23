using System.Text.Json;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Widgets;

namespace BetterWindows.Core.Tests.Widgets;

/// <summary>The reminders file: round trip, tolerant loading, quarantine of corrupt files, limits and the notified flag.</summary>
public sealed class ReminderStoreTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "bw-reminders-" + Guid.NewGuid().ToString("N"));
    private readonly ManualTimeProvider _time = new();
    private string Path_ => System.IO.Path.Combine(_dir, "reminders.json");

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, true); } catch { /* best effort */ }
    }

    public static StoredReminder Item(string? id = null, string title = "Pay rent", string? due = "2026-10-23T14:30") => new()
    {
        Id = id ?? Guid.NewGuid().ToString("D"), Title = title, Due = due, CreatedAt = "2026-10-23T10:00:00Z",
    };

    [Fact]
    public void A_missing_file_is_an_empty_store_and_the_first_add_creates_it()
    {
        var store = new ReminderStore(Path_, _time);
        store.Load();
        store.Count.Should().Be(0);
        File.Exists(Path_).Should().BeFalse();

        store.Add(Item(title: "First")).Should().NotBeNull();
        File.Exists(Path_).Should().BeTrue();
        JsonDocument.Parse(File.ReadAllText(Path_)).RootElement.GetProperty("version").GetInt32().Should().Be(1);
    }

    [Fact]
    public void Everything_survives_a_reload()
    {
        var store = new ReminderStore(Path_, _time);
        store.Load();
        var a = Item(title: "A", due: "2026-10-23T09:05");
        store.Add(a with { Notes = "line 1\nline 2", List = "Home" });
        store.Update(a.Id, r => r with { Done = true, Notified = true });

        var reloaded = new ReminderStore(Path_, _time);
        reloaded.Load();
        var item = reloaded.Snapshot().Should().ContainSingle().Subject;
        item.Title.Should().Be("A");
        item.Due.Should().Be("2026-10-23T09:05");
        item.Notes.Should().Be("line 1\nline 2");
        item.List.Should().Be("Home");
        item.Done.Should().BeTrue();
        item.Notified.Should().BeTrue();
        item.CreatedAt.Should().Be("2026-10-23T10:00:00Z");
    }

    [Fact]
    public void A_corrupt_file_is_quarantined_and_never_overwritten_in_place()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, "{ this is not json");
        var store = new ReminderStore(Path_, _time);
        store.Load();

        store.Count.Should().Be(0);
        File.Exists(Path_).Should().BeFalse("the corrupt file was moved aside");
        Directory.GetFiles(_dir, "reminders.corrupt-*.json").Should().ContainSingle();
    }

    [Fact]
    public void Bad_items_are_dropped_and_bad_fields_repaired_one_by_one()
    {
        Directory.CreateDirectory(_dir);
        var good = Guid.NewGuid().ToString("D");
        var json = $$"""
        {
          "version": 1,
          "items": [
            "not an object",
            {"id": "nope", "title": "bad id"},
            {"id": "{{Guid.NewGuid():D}}", "title": "   "},
            {"id": "{{good}}", "title": "  Keep me  ", "due": "2026-13-45T99:99", "notes": 5, "list": "  ", "done": "yes"},
            {"id": "{{good.ToUpperInvariant()}}", "title": "duplicate by case"},
            {"id": "{{Guid.NewGuid():D}}", "title": "Trailing comma survives", "due": "2026-10-23T14:30:45.1234"},
          ]
        }
        """;
        File.WriteAllText(Path_, json);

        var store = new ReminderStore(Path_, _time);
        store.Load();
        var items = store.Snapshot();
        items.Select(i => i.Title).Should().Equal("Keep me", "Trailing comma survives");
        items[0].Due.Should().BeNull("an unparseable due is dropped, the reminder is kept");
        items[0].List.Should().BeNull();
        items[0].Done.Should().BeFalse("a non-boolean done reads as false");
        items[1].Due.Should().Be("2026-10-23T14:30:45", "fractions are dropped when canonicalising");
    }

    [Fact]
    public void A_bare_array_of_items_is_accepted()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path_, $"[{{\"id\":\"{Guid.NewGuid():D}\",\"title\":\"Bare\"}}]");
        var store = new ReminderStore(Path_, _time);
        store.Load();
        store.Snapshot().Should().ContainSingle().Which.Title.Should().Be("Bare");
    }

    [Fact]
    public void Over_long_text_is_cut_to_the_limits_on_load()
    {
        Directory.CreateDirectory(_dir);
        var id = Guid.NewGuid().ToString("D");
        var json = $"{{\"items\":[{{\"id\":\"{id}\",\"title\":\"{new string('t', ReminderRules.MaxTitleChars + 50)}\",\"notes\":\"{new string('n', ReminderRules.MaxNotesChars + 50)}\"}}]}}";
        File.WriteAllText(Path_, json);
        var store = new ReminderStore(Path_, _time);
        store.Load();
        var r = store.Snapshot().Single();
        r.Title.Length.Should().Be(ReminderRules.MaxTitleChars);
        r.Notes!.Length.Should().Be(ReminderRules.MaxNotesChars);
    }

    [Fact]
    public void Loading_keeps_at_most_MaxItems()
    {
        Directory.CreateDirectory(_dir);
        var items = Enumerable.Range(0, ReminderRules.MaxItems + 5)
            .Select(i => new { id = Guid.NewGuid().ToString("D"), title = "t" + i });
        File.WriteAllText(Path_, JsonSerializer.Serialize(new { version = 1, items }));
        var store = new ReminderStore(Path_, _time);
        store.Load();
        store.Count.Should().Be(ReminderRules.MaxItems);
        store.Add(Item()).Should().BeNull("the store is full");
    }

    [Fact]
    public void Update_and_remove_of_unknown_ids_change_nothing()
    {
        var store = new ReminderStore(Path_, _time);
        store.Load();
        store.Add(Item(title: "Only"));
        store.Update(Guid.NewGuid().ToString("D"), r => r with { Title = "x" }).Should().BeNull();
        store.Remove(Guid.NewGuid().ToString("D")).Should().BeFalse();
        store.Snapshot().Select(r => r.Title).Should().Equal("Only");
    }

    [Fact]
    public void Remove_deletes_and_persists()
    {
        var store = new ReminderStore(Path_, _time);
        store.Load();
        var a = Item();
        store.Add(a);
        store.Remove(a.Id).Should().BeTrue();
        TestLoad(new ReminderStore(Path_, _time)).Should().BeEmpty();
    }

    [Fact]
    public void MarkNotified_flips_each_reminder_once_and_persists_the_flag()
    {
        var store = new ReminderStore(Path_, _time);
        store.Load();
        var a = Item(title: "A");
        var b = Item(title: "B");
        store.Add(a);
        store.Add(b);

        var first = store.MarkNotified(new[] { a.Id, b.Id });
        first.Select(r => r.Title).Should().BeEquivalentTo("A", "B");
        store.MarkNotified(new[] { a.Id, b.Id }).Should().BeEmpty("a second pass must not fire them again");
        TestLoad(new ReminderStore(Path_, _time)).Should().OnlyContain(r => r.Notified);
    }

    [Fact]
    public void MarkNotified_ignores_unknown_ids()
    {
        var store = new ReminderStore(Path_, _time);
        store.Load();
        store.MarkNotified(new[] { Guid.NewGuid().ToString("D") }).Should().BeEmpty();
        File.Exists(Path_).Should().BeFalse("nothing changed, so nothing was written");
    }

    private static IReadOnlyList<StoredReminder> TestLoad(ReminderStore s)
    {
        s.Load();
        return s.Snapshot();
    }
}
