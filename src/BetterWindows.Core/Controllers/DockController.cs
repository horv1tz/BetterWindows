using BetterWindows.Core.Dock;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.WindowTracking;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Owns every <c>dock.*</c> call, the <c>dock.state</c> state event and the transient <c>dock.bounce</c> event.
/// <para>
/// State: <c>dock.state</c> is declared with <see cref="IStatePublisher"/> (coalesced to 50 ms; the publisher pushes it to a dock / overlay right after its
/// <c>surface.ready</c>); the tracker, the catalog, the settings and the virtual desktops only <c>Invalidate()</c> it. The snapshot function is
/// <see cref="DockModel.Build"/> over (settings.dock.pins, tracker snapshot, catalog, first-seen order, launches in flight). With <c>dock.monitor = primary</c>
/// a dock surface that belongs to a non-primary monitor gets nothing.
/// </para>
/// <para>
/// Security (docs/IPC.md section 14): the UI is untrusted. Window handles go through <see cref="IWindowTracker.Find"/> and the process id is compared with a fresh
/// <see cref="IWindowSystem.TryDescribe"/> before anything is done to the window; app ids are resolved through the pins, the catalog, tracked windows or
/// <see cref="IPathGrants"/>; only <see cref="LaunchTarget"/> values derived from those resolved entries are launched (never UI arguments); paths pass
/// <see cref="InputPolicy"/>. Unknown things answer notFound.
/// </para>
/// </summary>
public sealed class DockController : IShellController
{
    private static readonly TimeSpan StateCoalesce = TimeSpan.FromMilliseconds(50);
    /// <summary>How long a launch without a window is shown as "launching" (docs: DockItem.launching).</summary>
    public static readonly TimeSpan LaunchingTimeout = TimeSpan.FromSeconds(20);
    private const int MaxIdChars = 4200;
    private const int MaxAumidChars = 512;

    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly IEventHub _hub;
    private readonly IWindowTracker _tracker;
    private readonly IWindowSystem _windows;
    private readonly IAppCatalog _catalog;
    private readonly ISettingsService _settings;
    private readonly IAppLauncher _launcher;
    private readonly IPathGrants _grants;
    private readonly IMonitorProvider _monitors;
    private readonly IVirtualDesktopService _desktops;
    private readonly TimeProvider _time;
    private readonly IPathProbe _probe;
    private readonly ILogger _log;

    private readonly RunningOrder _order = new();
    private readonly object _gate = new();
    private readonly Dictionary<string, DateTimeOffset> _launching = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ITimer> _launchTimers = new();
    private HashSet<long> _flashing = new();
    private IStateTopic? _topic;
    private volatile bool _started;

    public DockController(
        IMessageRouter router, IStatePublisher state, IEventHub hub, IWindowTracker tracker, IWindowSystem windows, IAppCatalog catalog, ISettingsService settings,
        IAppLauncher launcher, IPathGrants grants, IMonitorProvider monitors, IVirtualDesktopService desktops, TimeProvider time,
        ILogger<DockController>? log = null, IPathProbe? probe = null)
    {
        _router = router;
        _state = state;
        _hub = hub;
        _tracker = tracker;
        _windows = windows;
        _catalog = catalog;
        _settings = settings;
        _launcher = launcher;
        _grants = grants;
        _monitors = monitors;
        _desktops = desktops;
        _time = time;
        _probe = probe ?? new FileSystemPathProbe();
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "DockController";

    // =====================================================================================================================================
    //  Lifecycle
    // =====================================================================================================================================

    public void Start()
    {
        _router.Handle<DockItemParams>(Msg.DockActivate, (ctx, p) => Run(() => ActivateAsync(p.ItemId)));
        _router.Handle<WindowHandleParams>(Msg.DockActivateWindow, (ctx, p) => Run(() => ActivateWindowAsync(p.Handle)));
        _router.Handle<WindowHandleParams>(Msg.DockCloseWindow, (ctx, p) => Run(() => CloseWindowAsync(p.Handle)));
        _router.Handle<DockPinParams>(Msg.DockPin, (ctx, p) => Run(() => PinAsync(p, ctx.Cancellation)));
        _router.Handle<DockItemParams>(Msg.DockUnpin, (ctx, p) => Run(() => UnpinAsync(p.ItemId, ctx.Cancellation)));
        _router.Handle<DockReorderParams>(Msg.DockReorder, (ctx, p) => Run(() => ReorderAsync(p.ItemIds, ctx.Cancellation)));
        _router.Handle<DockItemParams>(Msg.DockNewWindow, (ctx, p) => Run(() => NewWindowAsync(p.ItemId)));
        _router.Handle<DockItemParams>(Msg.DockQuit, (ctx, p) => Run(() => QuitAsync(p.ItemId)));
        _router.Handle<DockItemParams>(Msg.DockHide, (ctx, p) => Run(() => HideAsync(p.ItemId)));
        _router.Handle<DockItemParams>(Msg.DockRevealInExplorer, (ctx, p) => Run(() => RevealInExplorerAsync(p.ItemId)));
        _router.Handle<DockPinPathParams>(Msg.DockPinPath, (ctx, p) => Run(() => PinPathAsync(p, ctx.Cancellation)));

        _topic = _state.Declare<DockState>(Msg.DockState, SnapshotFor, StateCoalesce);
        _started = true;
        _tracker.Changed += OnTrackerChanged;
        _catalog.Changed += OnCatalogChanged;
        _settings.Changed += OnSettingsChanged;
        _desktops.Changed += Invalidate;

        lock (_gate) _flashing = new HashSet<long>(_tracker.Snapshot.Windows.Where(w => w.Raw.IsFlashing).Select(w => w.Handle));
        _ = SeedDefaultPinsAsync();
    }

    public void Stop()
    {
        _started = false;
        _tracker.Changed -= OnTrackerChanged;
        _catalog.Changed -= OnCatalogChanged;
        _settings.Changed -= OnSettingsChanged;
        _desktops.Changed -= Invalidate;
        lock (_gate)
        {
            foreach (var t in _launchTimers) t.Dispose();
            _launchTimers.Clear();
            _launching.Clear();
        }
    }

    private Task Run(Func<Task> work)
    {
        if (!_started) throw ProtocolException.Failed("the dock is shutting down");
        return work();
    }

    // =====================================================================================================================================
    //  Change notifications (< 1 ms: invalidate, diff, publish a transient event)
    // =====================================================================================================================================

    private void Invalidate() => _topic?.Invalidate();

    private void OnCatalogChanged()
    {
        Invalidate();
        _ = SeedDefaultPinsAsync();
    }

    private void OnSettingsChanged(Settings settings)
    {
        Invalidate();
        _ = SeedDefaultPinsAsync();
    }

    private void OnTrackerChanged(WindowSnapshot snapshot)
    {
        try
        {
            PruneLaunching(snapshot);
            PublishAttention(snapshot);
        }
        catch (Exception ex) { _log.LogError(ex, "Dock change handling failed"); }
        Invalidate();
    }

    /// <summary>A window that starts flashing (and is not the foreground window) makes its dock item bounce for attention - once per flash start.</summary>
    private void PublishAttention(WindowSnapshot snapshot)
    {
        var foreground = snapshot.Foreground?.Handle ?? 0;
        var flashing = snapshot.Windows.Where(w => w.Raw.IsFlashing && w.Handle != foreground).ToList();
        List<TrackedWindow> started;
        lock (_gate)
        {
            started = flashing.Where(w => !_flashing.Contains(w.Handle)).ToList();
            _flashing = new HashSet<long>(flashing.Select(w => w.Handle));
        }
        if (started.Count == 0) return;
        var matcher = new PinMatcher(_settings.Current.Dock.Pins);
        foreach (var itemId in started.Select(matcher.KeyFor).Distinct(StringComparer.OrdinalIgnoreCase))
            _hub.PublishToKind(SurfaceKind.Dock, Msg.DockBounce, new DockBounce { ItemId = itemId, Kind = DockBounceKind.Attention });
    }

    // =====================================================================================================================================
    //  dock.state
    // =====================================================================================================================================

    private DockState? SnapshotFor(SurfaceId surface)
    {
        var settings = _settings.Current;
        if (surface.Kind == SurfaceKind.Dock && surface.MonitorId is not null && settings.Dock.Monitor == DockMonitor.Primary && !IsPrimaryMonitor(surface.MonitorId)) return null;

        var snapshot = _tracker.Snapshot;
        var launching = LaunchingIds(snapshot);
        return DockModel.Build(new DockModelInput
        {
            Pins = settings.Dock.Pins,
            Windows = snapshot,
            FindApp = id => _catalog.FindById(id),
            RunningOrder = _order.Update(snapshot.Windows.Select(w => w.AppId)),
            Launching = launching,
            DesktopOf = _desktops.IsSupported ? _desktops.GetWindowDesktopId : null,
        });
    }

    private bool IsPrimaryMonitor(string monitorId)
    {
        try
        {
            var primary = _monitors.GetMonitors().FirstOrDefault(m => m.Primary);
            return primary is null || string.Equals(primary.Id, monitorId, StringComparison.Ordinal);
        }
        catch (Exception ex)
        {
            _log.LogDebug(ex, "Monitor lookup failed; treating the dock as primary");
            return true;
        }
    }

    // =====================================================================================================================================
    //  Launching bookkeeping
    // =====================================================================================================================================

    private IReadOnlyList<string> LaunchingIds(WindowSnapshot snapshot)
    {
        var now = _time.GetUtcNow();
        lock (_gate)
        {
            if (_launching.Count == 0) return Array.Empty<string>();
            return _launching.Where(kv => kv.Value > now).OrderBy(kv => kv.Value).ThenBy(kv => kv.Key, StringComparer.OrdinalIgnoreCase).Select(kv => kv.Key).ToList();
        }
    }

    private void MarkLaunching(string itemId)
    {
        var deadline = _time.GetUtcNow() + LaunchingTimeout;
        ITimer? timer = null;
        // The callback removes its own timer; the lock makes "created" and "fired" ordering irrelevant.
        timer = _time.CreateTimer(_ => ExpireLaunching(() => timer), null, LaunchingTimeout, Timeout.InfiniteTimeSpan);
        lock (_gate)
        {
            if (!_started) { timer.Dispose(); return; }
            _launching[itemId] = deadline;
            _launchTimers.Add(timer);
        }
        Invalidate();
    }

    private void ExpireLaunching(Func<ITimer?> self)
    {
        try
        {
            var now = _time.GetUtcNow();
            lock (_gate)
            {
                foreach (var key in _launching.Where(kv => kv.Value <= now).Select(kv => kv.Key).ToList()) _launching.Remove(key);
                if (self() is { } t) { _launchTimers.Remove(t); t.Dispose(); }
            }
            Invalidate();
        }
        catch (Exception ex) { _log.LogError(ex, "Expiring launches failed"); }
    }

    private void PruneLaunching(WindowSnapshot snapshot)
    {
        lock (_gate) { if (_launching.Count == 0) return; }
        var matcher = new PinMatcher(_settings.Current.Dock.Pins);
        var running = new HashSet<string>(snapshot.Windows.Select(matcher.KeyFor), StringComparer.OrdinalIgnoreCase);
        lock (_gate)
        {
            foreach (var id in _launching.Keys.Where(running.Contains).ToList()) _launching.Remove(id);
        }
    }

    // =====================================================================================================================================
    //  Resolving what the UI named
    // =====================================================================================================================================

    private sealed record ItemRef(string Id, PinnedItem? Pin, AppEntry? Entry, IReadOnlyList<TrackedWindow> Windows)
    {
        public bool IsFolder => Pin?.Kind == PinnedKind.Folder;
    }

    private static string RequireId(string? id, string field)
    {
        if (string.IsNullOrEmpty(id)) throw ProtocolException.BadRequest($"{field} is required");
        if (id.Length > MaxIdChars) throw ProtocolException.BadRequest($"{field} is too long");
        foreach (var c in id) if (c < 0x20 || c == 0x7f) throw ProtocolException.BadRequest($"{field} contains control characters");
        return id;
    }

    /// <summary>A dock item or any running app: a pin, a catalog app or an app with tracked windows. null = unknown.</summary>
    private ItemRef? Resolve(string id)
    {
        var pins = PinsEditor.Normalize(_settings.Current.Dock.Pins);
        var pin = PinsEditor.Find(pins, id);
        if (pin?.Kind == PinnedKind.Folder) return new ItemRef(pin.Id, pin, null, Array.Empty<TrackedWindow>());
        var canonical = pin?.Id ?? id;
        var windows = new PinMatcher(pins).WindowsOf(canonical, _tracker.Snapshot.Windows);
        var entry = _catalog.FindById(canonical);
        if (pin is null && windows.Count == 0 && entry is null) return null;
        return new ItemRef(pin?.Id ?? entry?.Id ?? windows.FirstOrDefault()?.AppId ?? id, pin, entry, windows);
    }

    private ItemRef ResolveOrThrow(string? itemId)
    {
        var id = RequireId(itemId, "itemId");
        if (id.StartsWith("separator:", StringComparison.Ordinal)) throw ProtocolException.BadRequest("separators have no actions");
        return Resolve(id) ?? throw ProtocolException.NotFound("unknown dock item");
    }

    private TrackedWindow RequireWindow(string? wire, out RawWindow fresh)
    {
        if (!Hwnd.TryParse(wire, out var handle) || handle == 0) throw ProtocolException.BadRequest("invalid window handle");
        var tracked = _tracker.Find(handle) ?? throw ProtocolException.NotFound("unknown window");
        fresh = Verify(tracked);
        return tracked;
    }

    /// <summary>The handle may have been recycled for a window of another process since the snapshot: compare the process id with the live window.</summary>
    private RawWindow Verify(TrackedWindow tracked)
    {
        RawWindow? fresh;
        try { fresh = _windows.TryDescribe(tracked.Handle); }
        catch (Exception ex) { _log.LogDebug(ex, "TryDescribe failed"); fresh = null; }
        if (fresh is null || fresh.IsOwnProcess || fresh.ProcessId != tracked.Raw.ProcessId) throw ProtocolException.NotFound("window is gone");
        return fresh;
    }

    private static LaunchTarget? AumidTarget(string? aumid) =>
        string.IsNullOrWhiteSpace(aumid) || aumid.Length > MaxAumidChars || aumid.Any(c => c < 0x20) ? null : LaunchTarget.FromAumid(aumid.Trim());

    private static LaunchTarget? PathTarget(string? path) =>
        InputPolicy.TryNormalizeLocalPath(AppNames.CleanPath(path), out var p) ? LaunchTarget.FromPath(p) : null;

    /// <summary>Launch target from RESOLVED data only: catalog entry first, then the pin, then a running window of the app. Never from the request.</summary>
    private static LaunchTarget? LaunchTargetOf(ItemRef item)
    {
        if (item.Entry is { } e)
        {
            var t = AumidTarget(e.Aumid) ?? PathTarget(e.Path);
            if (t is not null) return t;
        }
        if (item.Pin is { Kind: PinnedKind.App } pin)
        {
            var t = pin.Id.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase)
                ? AumidTarget(string.IsNullOrWhiteSpace(pin.Target) ? pin.Id[6..] : pin.Target)
                : PathTarget(string.IsNullOrWhiteSpace(pin.Target) ? (pin.Id.StartsWith("exe:", StringComparison.OrdinalIgnoreCase) ? pin.Id[4..] : null) : pin.Target);
            if (t is not null) return t;
        }
        foreach (var w in item.Windows)
        {
            var t = item.Id.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase) ? AumidTarget(w.Raw.Aumid) ?? PathTarget(w.Raw.ExePath) : PathTarget(w.Raw.ExePath) ?? AumidTarget(w.Raw.Aumid);
            if (t is not null) return t;
        }
        return null;
    }

    // =====================================================================================================================================
    //  Window commands
    // =====================================================================================================================================

    private Task ActivateWindowAsync(string handle)
    {
        var tracked = RequireWindow(handle, out var fresh);
        ActivateCore(tracked, fresh);
        return Task.CompletedTask;
    }

    private void ActivateCore(TrackedWindow tracked, RawWindow fresh)
    {
        if (fresh.IsMinimized) _windows.Restore(tracked.Handle);
        if (!_windows.Activate(tracked.Handle))
            throw fresh.IsElevated ? ProtocolException.Unsupported("the window belongs to an elevated process") : ProtocolException.Failed("could not activate the window");
    }

    private Task CloseWindowAsync(string handle)
    {
        var tracked = RequireWindow(handle, out var fresh);
        if (!_windows.Close(tracked.Handle))
            throw fresh.IsElevated ? ProtocolException.Unsupported("the window belongs to an elevated process") : ProtocolException.Failed("could not close the window");
        return Task.CompletedTask;
    }

    // =====================================================================================================================================
    //  Item commands
    // =====================================================================================================================================

    /// <summary>Launch if not running; else focus the most recent window (restoring it); if the app already has the foreground and several windows, cycle to the next one.</summary>
    private Task ActivateAsync(string itemId)
    {
        var item = ResolveOrThrow(itemId);
        if (item.IsFolder) { OpenFolder(item); return Task.CompletedTask; }
        if (item.Windows.Count == 0) { Launch(item, bounce: true); return Task.CompletedTask; }

        var foreground = _tracker.Snapshot.Foreground?.Handle ?? 0;
        var target = item.Windows[0];
        if (item.Windows.Count > 1 && item.Windows.Any(w => w.Handle == foreground))
        {
            // Stable order (handles), not MRU: MRU would just toggle between the two most recent windows.
            var ordered = item.Windows.OrderBy(w => w.Handle).ToList();
            var at = ordered.FindIndex(w => w.Handle == foreground);
            target = ordered[(at + 1) % ordered.Count];
        }
        ActivateCore(target, Verify(target));
        return Task.CompletedTask;
    }

    /// <summary>"New window" = launch again (the launcher starts a new instance / window); a folder opens another Explorer window.</summary>
    private Task NewWindowAsync(string itemId)
    {
        var item = ResolveOrThrow(itemId);
        if (item.IsFolder) OpenFolder(item);
        else Launch(item, bounce: item.Windows.Count == 0);
        return Task.CompletedTask;
    }

    private void Launch(ItemRef item, bool bounce)
    {
        var target = LaunchTargetOf(item) ?? throw ProtocolException.NotFound("nothing to launch for this item");
        switch (_launcher.Launch(target))
        {
            case LaunchResult.Started: break;
            case LaunchResult.NotFound: throw ProtocolException.NotFound("the app could not be found");
            default: throw ProtocolException.Failed("the app could not be started");
        }
        if (item.Windows.Count == 0) MarkLaunching(item.Id);
        if (bounce && _settings.Current.Dock.BounceOnLaunch)
            _hub.PublishToKind(SurfaceKind.Dock, Msg.DockBounce, new DockBounce { ItemId = item.Id, Kind = DockBounceKind.Launch });
    }

    private void OpenFolder(ItemRef item)
    {
        var path = FolderPathOf(item.Pin!) ?? throw ProtocolException.BadRequest("the pinned folder path is not valid");
        if (!_launcher.Open(path)) throw ProtocolException.Failed("the folder could not be opened");
    }

    private static string? FolderPathOf(PinnedItem pin)
    {
        if (InputPolicy.TryNormalizeLocalPath(AppNames.CleanPath(pin.Target), out var p)) return p;
        return pin.Id.StartsWith("dir:", StringComparison.OrdinalIgnoreCase) && InputPolicy.TryNormalizeLocalPath(pin.Id[4..], out p) ? p : null;
    }

    /// <summary>Close (WM_CLOSE) every window of the app. Works for ANY running app id, not only visible dock items (the app menu uses it).</summary>
    private Task QuitAsync(string itemId)
    {
        var item = ResolveOrThrow(itemId);
        if (item.IsFolder) throw ProtocolException.BadRequest("a folder cannot be quit");
        var failed = 0;
        foreach (var w in item.Windows)
        {
            try { Verify(w); } catch (ProtocolException) { continue; } // already gone
            if (!_windows.Close(w.Handle)) failed++;
        }
        if (failed > 0 && failed == item.Windows.Count) throw ProtocolException.Failed("the windows could not be closed");
        return Task.CompletedTask;
    }

    /// <summary>Minimise every window of the app. Works for ANY running app id.</summary>
    private Task HideAsync(string itemId)
    {
        var item = ResolveOrThrow(itemId);
        if (item.IsFolder) throw ProtocolException.BadRequest("a folder cannot be hidden");
        foreach (var w in item.Windows)
        {
            RawWindow fresh;
            try { fresh = Verify(w); } catch (ProtocolException) { continue; }
            if (!fresh.IsMinimized) _windows.Minimize(w.Handle);
        }
        return Task.CompletedTask;
    }

    private Task RevealInExplorerAsync(string itemId)
    {
        var item = ResolveOrThrow(itemId);
        string? path;
        if (item.IsFolder) path = FolderPathOf(item.Pin!);
        else
        {
            path = null;
            foreach (var candidate in new[] { item.Entry?.Path, item.Pin is { Kind: PinnedKind.App } p && !p.Id.StartsWith("aumid:", StringComparison.OrdinalIgnoreCase) ? p.Target : null, item.Windows.FirstOrDefault()?.Raw.ExePath })
            {
                if (InputPolicy.TryNormalizeLocalPath(AppNames.CleanPath(candidate), out var normalized)) { path = normalized; break; }
            }
        }
        if (path is null) throw ProtocolException.NotFound("this item has no file location");
        if (!_launcher.RevealInExplorer(path)) throw ProtocolException.Failed("Explorer could not be opened");
        return Task.CompletedTask;
    }

    // =====================================================================================================================================
    //  Pins
    // =====================================================================================================================================

    private async Task PinAsync(DockPinParams p, CancellationToken ct)
    {
        var id = RequireId(p.AppId, "appId");
        if (!InputPolicy.TryParseAppId(id, out var kind, out var value)) throw ProtocolException.BadRequest("this item cannot be pinned");
        if (kind == AppIdKind.Directory) throw ProtocolException.BadRequest("folders are pinned with dock.pinPath");
        var pin = PinFor(id, kind, value) ?? throw ProtocolException.NotFound("unknown app");
        await AddPinAsync(pin, p.Index, ct).ConfigureAwait(false);
    }

    /// <summary>The pin for an app id: the catalog entry, else a running window of the app, else an executable the user picked in a native dialog.</summary>
    private PinnedItem? PinFor(string id, AppIdKind kind, string value)
    {
        var entry = _catalog.FindById(id) ?? (kind == AppIdKind.Aumid ? _catalog.FindByAumid(value) : _catalog.FindByExePath(value));
        if (entry is not null)
        {
            var target = !string.IsNullOrWhiteSpace(entry.Aumid) ? entry.Aumid! : entry.Path;
            if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(entry.Id)) return null;
            return new PinnedItem { Id = entry.Id, Kind = PinnedKind.App, Target = target, Name = entry.Name };
        }

        var window = _tracker.Snapshot.Windows.FirstOrDefault(w => string.Equals(w.AppId, id, StringComparison.OrdinalIgnoreCase));
        if (window is not null)
        {
            string? target;
            if (kind == AppIdKind.Aumid) target = AumidTarget(window.Raw.Aumid)?.Aumid ?? value;
            else target = InputPolicy.TryNormalizeLocalPath(AppNames.CleanPath(window.Raw.ExePath), out var exe) ? exe : null;
            if (string.IsNullOrWhiteSpace(target)) return null;
            return new PinnedItem { Id = window.AppId, Kind = PinnedKind.App, Target = target, Name = window.AppName };
        }

        if (kind == AppIdKind.ExePath && _grants.IsGranted(value) && _probe.Probe(value) == PathKind.File)
            return new PinnedItem { Id = "exe:" + value.ToLowerInvariant(), Kind = PinnedKind.App, Target = value, Name = AppNames.FromExePath(value) };
        return null;
    }

    /// <summary>Pin a folder / .exe / .lnk the user picked with system.pickPath.</summary>
    private async Task PinPathAsync(DockPinPathParams p, CancellationToken ct)
    {
        if (!InputPolicy.TryNormalizeLocalPath(p.Path, out var path)) throw ProtocolException.BadRequest("the path is not a valid local absolute path");
        if (!_grants.IsGranted(path)) throw ProtocolException.NotFound("this path was not chosen with the file dialog");

        PinnedItem pin;
        switch (_probe.Probe(path))
        {
            case PathKind.Missing:
                throw ProtocolException.NotFound("the path does not exist");
            case PathKind.Directory:
            {
                var folder = path.Length > 3 ? path.TrimEnd('\\') : path;
                pin = new PinnedItem
                {
                    Id = "dir:" + folder.ToLowerInvariant(),
                    Kind = PinnedKind.Folder,
                    Target = folder,
                    Name = path.Length > 3 ? AppNames.FileName(folder) : folder,
                };
                break;
            }
            default:
            {
                if (!(path.EndsWith(".exe", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".lnk", StringComparison.OrdinalIgnoreCase)))
                    throw ProtocolException.BadRequest("only folders, .exe and .lnk files can be pinned");
                var entry = _catalog.FindByExePath(path);
                var target = entry is null ? null : (!string.IsNullOrWhiteSpace(entry.Aumid) ? entry.Aumid : entry.Path);
                pin = entry is not null && !string.IsNullOrWhiteSpace(target)
                    ? new PinnedItem { Id = entry.Id, Kind = PinnedKind.App, Target = target!, Name = entry.Name }
                    : new PinnedItem { Id = "exe:" + path.ToLowerInvariant(), Kind = PinnedKind.App, Target = path, Name = AppNames.FromExePath(path) };
                break;
            }
        }
        await AddPinAsync(pin, p.Index, ct).ConfigureAwait(false);
    }

    private async Task AddPinAsync(PinnedItem pin, int? index, CancellationToken ct)
    {
        var current = _settings.Current.Dock.Pins;
        if (PinsEditor.Contains(current, pin.Id)) return; // idempotent: pinning twice keeps one pin
        if (current.Count >= PinsEditor.MaxPins) throw ProtocolException.BadRequest("too many pinned items");
        await _settings.UpdateAsync(s => s with { Dock = s.Dock with { Pins = PinsEditor.Insert(s.Dock.Pins, pin, index) } }, ct).ConfigureAwait(false);
    }

    private async Task UnpinAsync(string itemId, CancellationToken ct)
    {
        var id = RequireId(itemId, "itemId");
        if (!PinsEditor.Contains(_settings.Current.Dock.Pins, id)) throw ProtocolException.NotFound("this item is not pinned");
        await _settings.UpdateAsync(s => s with { Dock = s.Dock with { Pins = PinsEditor.Remove(s.Dock.Pins, id) } }, ct).ConfigureAwait(false);
    }

    private async Task ReorderAsync(IReadOnlyList<string> itemIds, CancellationToken ct)
    {
        if (itemIds.Count > PinsEditor.MaxPins) throw ProtocolException.BadRequest("too many ids");
        foreach (var id in itemIds) RequireId(id, "itemIds");
        if (!PinsEditor.TryReorder(_settings.Current.Dock.Pins, itemIds, out _, out var unknown)) throw ProtocolException.NotFound($"'{InputPolicy.Truncate(unknown, 64)}' is not pinned");
        await _settings.UpdateAsync(s =>
        {
            // Re-evaluated against the settings the write actually sees: a concurrent unpin makes this a no-op instead of an error.
            return PinsEditor.TryReorder(s.Dock.Pins, itemIds, out var reordered, out _) ? s with { Dock = s.Dock with { Pins = reordered } } : s;
        }, ct).ConfigureAwait(false);
    }

    // =====================================================================================================================================
    //  First-run seeding
    // =====================================================================================================================================

    /// <summary>
    /// Fresh profile (no pins, general.firstRunDone false) and a loaded catalog -> pin the default apps and mark the first run done, in ONE atomic update, so that a
    /// user who later removes every pin is never re-seeded. Runs again on every catalog / settings change until it succeeds (the catalog loads asynchronously).
    /// </summary>
    private async Task SeedDefaultPinsAsync()
    {
        try
        {
            if (!_started || !DefaultPins.ShouldSeed(_settings.Current)) return;
            var apps = _catalog.Apps;
            if (apps.Count == 0) return;
            var seed = DefaultPins.Seed(apps);
            await _settings.UpdateAsync(s => DefaultPins.ShouldSeed(s)
                ? s with { Dock = s.Dock with { Pins = seed }, General = s.General with { FirstRunDone = true } }
                : s).ConfigureAwait(false);
        }
        catch (Exception ex) { _log.LogError(ex, "Seeding the default dock pins failed"); }
    }
}
