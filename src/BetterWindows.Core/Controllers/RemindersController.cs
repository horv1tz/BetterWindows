using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Widgets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Owner of <c>reminders.list/add/update/remove</c>, of the <c>reminders.changed</c> state event (full list, after every mutation and on surface ready for overlay and
/// desktop) and of the transient <c>reminders.due</c>. Persistence is <see cref="ReminderStore"/> (<c>IAppPaths.DataDir/reminders.json</c>), timing is
/// <see cref="ReminderScheduler"/>. Input is validated by <see cref="ReminderRules"/>; ids are GUIDs.
/// </summary>
public sealed class RemindersController : IShellController
{
    /// <summary>A reminder created or moved to a due time up to this far in the past still counts as "due now" (datetime-local inputs have minute resolution).</summary>
    public static readonly TimeSpan DueGrace = TimeSpan.FromMinutes(1);

    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly IEventHub _hub;
    private readonly IAppPaths _paths;
    private readonly TimeProvider _time;
    private readonly ILogger _log;
    private ReminderStore? _store;
    private ReminderScheduler? _scheduler;
    private IStateTopic? _topic;

    public RemindersController(IMessageRouter router, IStatePublisher state, IEventHub hub, IAppPaths paths, TimeProvider time, ILogger<RemindersController>? log = null)
    {
        _router = router;
        _state = state;
        _hub = hub;
        _paths = paths;
        _time = time;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "RemindersController";

    public void Start()
    {
        var store = new ReminderStore(System.IO.Path.Combine(_paths.DataDir, "reminders.json"), _time, _log);
        store.Load();
        _store = store;
        _topic = _state.Declare<ReminderList>(Msg.RemindersChanged, _ => ToList(store.Snapshot()));

        _router.HandleNoParams<ReminderList>(Msg.RemindersList, ctx => Task.FromResult(ToList(store.Snapshot())));
        _router.Handle<ReminderAddParams, Reminder>(Msg.RemindersAdd, (ctx, p) => Task.FromResult(Add(p)));
        _router.Handle<ReminderUpdateParams, Reminder>(Msg.RemindersUpdate, (ctx, p) => Task.FromResult(Update(p)));
        _router.Handle<IdParams>(Msg.RemindersRemove, (ctx, p) => { Remove(p); return Task.CompletedTask; });

        var scheduler = new ReminderScheduler(store, _time, r => _hub.Publish(Msg.RemindersDue, r), _log);
        _scheduler = scheduler;
        scheduler.Start();
    }

    public void Stop()
    {
        _scheduler?.Dispose();
        _scheduler = null;
    }

    private static ReminderList ToList(IReadOnlyList<StoredReminder> items) => new() { Items = items.Select(r => r.ToProtocol()).ToArray() };

    private ReminderStore Store => _store ?? throw ProtocolException.Failed("reminders are not available");

    /// <summary>A due time that is already in the past (by more than the grace) must not fire the moment it is saved.</summary>
    private bool AlreadyPast(string? due)
    {
        if (due is null || !ReminderRules.TryParseDue(due, out var local)) return false;
        return ReminderRules.ToUtc(local, _time.LocalTimeZone) < _time.GetUtcNow() - DueGrace;
    }

    private Reminder Add(ReminderAddParams p)
    {
        var item = new StoredReminder
        {
            Id = Guid.NewGuid().ToString("D"),
            Title = ReminderRules.RequireLine(p.Title, ReminderRules.MaxTitleChars, "title"),
            Due = ReminderRules.OptionalDue(p.Due),
            Notes = ReminderRules.OptionalNotes(p.Notes),
            List = ReminderRules.OptionalLine(p.List, ReminderRules.MaxListChars, "list"),
            CreatedAt = _time.GetUtcNow().UtcDateTime.ToString("yyyy-MM-dd'T'HH:mm:ss'Z'", System.Globalization.CultureInfo.InvariantCulture),
        };
        item = item with { Notified = AlreadyPast(item.Due) };
        var saved = Save(() => Store.Add(item)) ?? throw ProtocolException.Failed($"too many reminders (the limit is {ReminderRules.MaxItems})");
        Changed();
        return saved.ToProtocol();
    }

    private Reminder Update(ReminderUpdateParams p)
    {
        var id = RequireId(p.Id);
        // Validate everything first so a bad field cannot half-apply.
        var title = p.Title is null ? null : ReminderRules.RequireLine(p.Title, ReminderRules.MaxTitleChars, "title");
        var dueGiven = p.Due is not null;
        var due = ReminderRules.OptionalDue(p.Due);
        var notesGiven = p.Notes is not null;
        var notes = ReminderRules.OptionalNotes(p.Notes);
        var listGiven = p.List is not null;
        var list = ReminderRules.OptionalLine(p.List, ReminderRules.MaxListChars, "list");

        var saved = Save(() => Store.Update(id, r =>
        {
            var next = r with
            {
                Title = title ?? r.Title,
                Due = dueGiven ? due : r.Due,
                Notes = notesGiven ? notes : r.Notes,
                List = listGiven ? list : r.List,
                Done = p.Done ?? r.Done,
            };
            // A new due time starts a new life: it fires (once) when it arrives, unless it is already long past. Clearing it resets the flag.
            if (dueGiven && next.Due != r.Due) next = next with { Notified = AlreadyPast(next.Due) };
            return next;
        })) ?? throw ProtocolException.NotFound("no such reminder");
        Changed();
        return saved.ToProtocol();
    }

    private void Remove(IdParams p)
    {
        var id = RequireId(p.Id);
        var removed = Save(() => Store.Remove(id) ? true : (bool?)null);
        if (removed is null) throw ProtocolException.NotFound("no such reminder");
        Changed();
    }

    private static string RequireId(string? id) =>
        ReminderRules.IsValidId(id) ? id!.ToLowerInvariant() : throw ProtocolException.BadRequest("invalid reminder id");

    /// <summary>Runs a store mutation, turning a failed write into a protocol error (the store rolled the change back).</summary>
    private T? Save<T>(Func<T?> mutation)
    {
        try { return mutation(); }
        catch (IOException ex)
        {
            _log.LogError(ex, "Saving reminders failed");
            throw ProtocolException.Failed("the reminders could not be saved");
        }
        catch (UnauthorizedAccessException ex)
        {
            _log.LogError(ex, "Saving reminders failed");
            throw ProtocolException.Failed("the reminders could not be saved");
        }
    }

    private void Changed()
    {
        _topic?.Invalidate();
        _scheduler?.Rearm();
    }
}
