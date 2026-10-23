using System.Collections.Concurrent;
using System.Text.Json.Nodes;
using BetterWindows.Core.Desktop;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Widgets;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Owner of every <c>desktop.*</c> call and of the <c>desktop.state</c> state event. The desktop layer is one surface PER MONITOR; each gets its own snapshot (its own
/// wallpaper URL, the icons and widgets that live on that monitor; desktop-folder items live on the primary monitor). All persistent state is in
/// <c>settings.desktop</c> and changes only through <see cref="ISettingsService.UpdateAsync"/> (pure operations in <see cref="DesktopSettingsOps"/>/<see cref="WidgetOps"/>).
/// While the layer is enabled with <c>hideNativeIcons</c> Explorer's own icons are hidden through <see cref="IDesktopIconsController"/>; <see cref="Stop"/> restores them.
/// </summary>
public sealed class DesktopController : IShellController
{
    private const int MaxIdChars = 4200;

    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly ISettingsService _settings;
    private readonly IMonitorProvider _monitorPort;
    private readonly IWallpaperService _wallpapers;
    private readonly IDesktopIconsController _nativeIcons;
    private readonly IDesktopFolderSource _folder;
    private readonly IAppLauncher _launcher;
    private readonly IPathGrants _grants;
    private readonly IPathProbe _probe;
    private readonly ILogger _log;

    private IStateTopic? _topic;
    private volatile IReadOnlyList<MonitorInfo> _monitors = Array.Empty<MonitorInfo>();
    private volatile IReadOnlyList<DesktopEntry>? _entries;
    private DesktopSettings _lastDesktop = new();
    private volatile bool _stopped;

    // Wallpaper bookkeeping: the system wallpaper per monitor (read off-thread, snapshots only read the cache) and a per-monitor revision that busts /dyn/wallpaper.
    private readonly object _wallpaperGate = new();
    private readonly Dictionary<string, string?> _systemWallpaper = new(StringComparer.Ordinal);
    private readonly Dictionary<string, int> _wallpaperRev = new(StringComparer.Ordinal);

    // Native icon visibility is applied by one worker at a time (the port call can block on a hung Explorer, so never on the settings-changed thread).
    private readonly object _applyGate = new();
    private bool? _appliedNativeVisible;

    private readonly SemaphoreSlim _reconcileGate = new(1, 1);

    public DesktopController(IMessageRouter router, IStatePublisher state, ISettingsService settings, IMonitorProvider monitors, IWallpaperService wallpapers,
        IDesktopIconsController nativeIcons, IDesktopFolderSource folder, IAppLauncher launcher, IPathGrants grants, IPathProbe? probe = null, ILogger<DesktopController>? log = null)
    {
        _router = router;
        _state = state;
        _settings = settings;
        _monitorPort = monitors;
        _wallpapers = wallpapers;
        _nativeIcons = nativeIcons;
        _folder = folder;
        _launcher = launcher;
        _grants = grants;
        _probe = probe ?? new SystemPathProbe();
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "DesktopController";

    /// <summary>Completes when the first background refresh (monitors, desktop folder, system wallpapers, native icon state) is done. Tests await it.</summary>
    internal Task Initialized { get; private set; } = Task.CompletedTask;

    public void Start()
    {
        _topic = _state.Declare<DesktopState>(Msg.DesktopState, BuildState);

        _router.Handle<DesktopMoveParams>(Msg.DesktopMoveIcon, (ctx, p) => MoveIconAsync(p, ctx.Cancellation));
        _router.Handle<IdParams>(Msg.DesktopOpen, (ctx, p) => { Open(p); return Task.CompletedTask; });
        _router.Handle<AddShortcutParams, DesktopIcon>(Msg.DesktopAddShortcut, (ctx, p) => AddShortcutAsync(p, ctx.Cancellation));
        _router.Handle<IdParams>(Msg.DesktopRemoveShortcut, (ctx, p) => RemoveShortcutAsync(p, ctx.Cancellation));
        _router.Handle<SetWallpaperParams>(Msg.DesktopSetWallpaper, (ctx, p) => SetWallpaperAsync(p, ctx.Cancellation));
        _router.Handle<MoveWidgetParams>(Msg.DesktopMoveWidget, (ctx, p) => MoveWidgetAsync(p, ctx.Cancellation));
        _router.Handle<AddWidgetParams, WidgetInstance>(Msg.DesktopAddWidget, (ctx, p) => AddWidgetAsync(p, ctx.Cancellation));
        _router.Handle<IdParams>(Msg.DesktopRemoveWidget, (ctx, p) => RemoveWidgetAsync(p, ctx.Cancellation));
        _router.Handle<UpdateWidgetParams, WidgetInstance>(Msg.DesktopUpdateWidget, (ctx, p) => UpdateWidgetAsync(p, ctx.Cancellation));

        _lastDesktop = _settings.Current.Desktop;
        _settings.Changed += OnSettingsChanged;
        _monitorPort.MonitorsChanged += OnMonitorsChanged;
        _folder.Changed += OnFolderChanged;
        Initialized = Task.Run(InitialRefreshAsync);
    }

    public void Stop()
    {
        _stopped = true;
        _settings.Changed -= OnSettingsChanged;
        _monitorPort.MonitorsChanged -= OnMonitorsChanged;
        _folder.Changed -= OnFolderChanged;
        // The native desktop icons are the user's: whatever happened, they come back when the shell stops.
        lock (_applyGate)
        {
            try { _nativeIcons.Restore(); }
            catch (Exception ex) { _log.LogError(ex, "Restoring the native desktop icons failed"); }
            _appliedNativeVisible = true;
        }
    }

    // ------------------------------------------------------------------ background refresh

    private async Task InitialRefreshAsync()
    {
        try
        {
            RefreshMonitors();
            RefreshEntries();
            RefreshSystemWallpapers();
            ApplyNativeIcons();
            _topic?.Invalidate();
            await ReconcileAsync().ConfigureAwait(false);
        }
        catch (Exception ex) { _log.LogError(ex, "Initial desktop refresh failed"); }
    }

    private void Background(Func<Task> work, string what) =>
        _ = Task.Run(async () =>
        {
            try { if (!_stopped) await work().ConfigureAwait(false); }
            catch (Exception ex) { _log.LogError(ex, "Desktop background work '{What}' failed", what); }
        });

    private void OnSettingsChanged(Settings s)
    {
        var previous = Interlocked.Exchange(ref _lastDesktop, s.Desktop);
        BumpChangedWallpapers(previous, s.Desktop);
        _topic?.Invalidate();
        Background(() => { ApplyNativeIcons(); return ReconcileAsync(); }, "settings changed");
    }

    private void OnMonitorsChanged() =>
        Background(() => { RefreshMonitors(); RefreshSystemWallpapers(); _topic?.Invalidate(); return ReconcileAsync(); }, "monitors changed");

    private void OnFolderChanged() =>
        Background(() => { RefreshEntries(); _topic?.Invalidate(); return ReconcileAsync(); }, "desktop folder changed");

    private void RefreshMonitors()
    {
        IReadOnlyList<MonitorInfo> list;
        try { list = _monitorPort.GetMonitors(); }
        catch (Exception ex) { _log.LogError(ex, "Enumerating monitors failed"); return; }
        _monitors = list.ToArray();
    }

    private void RefreshEntries()
    {
        try { _entries = _folder.Enumerate().ToArray(); }
        catch (Exception ex) { _log.LogError(ex, "Enumerating the desktop folder failed"); _entries ??= Array.Empty<DesktopEntry>(); }
    }

    private void RefreshSystemWallpapers()
    {
        foreach (var m in _monitors)
        {
            string? path = null;
            try { path = _wallpapers.GetWallpaperPath(m.Id); }
            catch (Exception ex) { _log.LogDebug(ex, "Reading the wallpaper of {Monitor} failed", m.Id); }
            lock (_wallpaperGate)
            {
                _systemWallpaper.TryGetValue(m.Id, out var old);
                _systemWallpaper[m.Id] = path;
                if (!string.Equals(old, path, StringComparison.OrdinalIgnoreCase)) BumpRev(m.Id);
            }
        }
    }

    /// <summary>Re-evaluates which native-icon visibility the settings ask for and applies it if it differs from what is applied (the first call always applies).</summary>
    private void ApplyNativeIcons()
    {
        lock (_applyGate)
        {
            if (_stopped) return;
            var d = _settings.Current.Desktop;
            var visible = !(d.Enabled && d.HideNativeIcons);
            if (_appliedNativeVisible == visible) return;
            try
            {
                _nativeIcons.SetNativeIconsVisible(visible);
                _appliedNativeVisible = visible;
            }
            catch (Exception ex) { _log.LogError(ex, "Changing the native desktop icon visibility failed"); }
        }
    }

    /// <summary>Persists placements of new desktop-folder items and prunes stale ones (no-op unless something would change).</summary>
    private async Task ReconcileAsync()
    {
        if (_stopped) return;
        await _reconcileGate.WaitAsync().ConfigureAwait(false);
        try
        {
            var entries = EntriesForHandler();
            var monitors = CurrentMonitors();
            var current = _settings.Current;
            if (ReferenceEquals(DesktopSettingsOps.ReconcilePlacements(current, entries, monitors), current)) return;
            await _settings.UpdateAsync(s => DesktopSettingsOps.ReconcilePlacements(s, entries, monitors)).ConfigureAwait(false);
        }
        finally { _reconcileGate.Release(); }
    }

    // ------------------------------------------------------------------ wallpaper revisions

    private int RevOf(string monitorId) { lock (_wallpaperGate) return _wallpaperRev.GetValueOrDefault(monitorId, 1); }
    private void BumpRev(string monitorId) { _wallpaperRev[monitorId] = _wallpaperRev.GetValueOrDefault(monitorId, 1) + 1; }

    private void BumpChangedWallpapers(DesktopSettings before, DesktopSettings after)
    {
        lock (_wallpaperGate)
            foreach (var m in _monitors)
                if (!string.Equals(DesktopSettingsOps.WallpaperOverride(before, m.Id), DesktopSettingsOps.WallpaperOverride(after, m.Id), StringComparison.OrdinalIgnoreCase)) BumpRev(m.Id);
    }

    private string? EffectiveWallpaper(DesktopSettings d, string monitorId)
    {
        var fromSettings = DesktopSettingsOps.WallpaperOverride(d, monitorId);
        if (fromSettings is not null) return fromSettings;
        lock (_wallpaperGate) return _systemWallpaper.GetValueOrDefault(monitorId);
    }

    // ------------------------------------------------------------------ state

    /// <summary>Cheap and pure (reads caches only): the snapshot of ONE desktop surface. null = no such monitor, send nothing.</summary>
    private DesktopState? BuildState(SurfaceId surface)
    {
        if (surface.Kind != SurfaceKind.Desktop) return null;
        var monitors = _monitors;
        var monitor = MonitorGeometry.Find(monitors, surface.MonitorId);
        if (monitor is null) return null;

        var s = _settings.Current;
        var d = s.Desktop;
        if (!d.Enabled)
            return new DesktopState { Enabled = false, WallpaperFit = d.WallpaperFit, IconSize = d.IconSize, MonitorId = monitor.Id };

        var wallpaper = EffectiveWallpaper(d, monitor.Id);
        var layout = DesktopLayout.Resolve(s, _entries ?? Array.Empty<DesktopEntry>(), monitors, monitor);
        return new DesktopState
        {
            Enabled = true,
            WallpaperUrl = wallpaper is null ? null : DynUrls.Wallpaper(monitor.Id, RevOf(monitor.Id)),
            WallpaperFit = d.WallpaperFit,
            IconSize = d.IconSize,
            Icons = layout.Icons.Select(i => new DesktopIcon
            {
                Id = i.Id, Name = i.Name, Target = i.Target, Kind = i.Kind, X = i.Cell.Column, Y = i.Cell.Row,
                IconUrl = DynUrls.PathIcon(i.Target, d.IconSize),
            }).ToArray(),
            Widgets = WidgetOps.On(s, monitors, monitor).Select(w => WidgetOps.ForDisplay(w, monitor)).ToArray(),
            MonitorId = monitor.Id,
        };
    }

    // ------------------------------------------------------------------ helpers

    private IReadOnlyList<MonitorInfo> CurrentMonitors()
    {
        var m = _monitors;
        if (m.Count > 0) return m;
        RefreshMonitors();
        return _monitors;
    }

    private IReadOnlyList<DesktopEntry> EntriesForHandler()
    {
        var e = _entries;
        if (e is not null) return e;
        RefreshEntries();
        return _entries ?? Array.Empty<DesktopEntry>();
    }

    private static string ValidateId(string? id, int max = MaxIdChars)
    {
        if (string.IsNullOrEmpty(id) || id.Length > max) throw ProtocolException.BadRequest("invalid id");
        foreach (var c in id) if (c < 0x20 || c == 0x7f) throw ProtocolException.BadRequest("invalid id");
        return id;
    }

    /// <summary>Monitor ids from the UI must be a known monitor (docs/IPC.md section 14); null = the primary monitor.</summary>
    private static MonitorInfo ResolveMonitor(IReadOnlyList<MonitorInfo> monitors, string? requested)
    {
        if (requested is not null && monitors.All(m => m.Id != requested)) throw ProtocolException.BadRequest("unknown monitor");
        return MonitorGeometry.Find(monitors, requested) ?? throw ProtocolException.Failed("no monitor is available");
    }

    /// <summary>A UI path must be a clean local absolute path AND one the user picked in a native dialog this session.</summary>
    private string ValidateUserPath(string? raw)
    {
        if (!InputPolicy.TryNormalizeLocalPath(raw, out var path) || path.Length > DesktopPathRules.MaxPathChars)
            throw ProtocolException.BadRequest("path must be an absolute local path");
        if (!_grants.IsGranted(path)) throw ProtocolException.Forbidden("the path was not chosen in a file dialog");
        return path;
    }

    // ------------------------------------------------------------------ icons

    private async Task MoveIconAsync(DesktopMoveParams p, CancellationToken ct)
    {
        var id = ValidateId(p.Id);
        var entries = EntriesForHandler();
        var monitors = CurrentMonitors();
        if (DesktopSettingsOps.MonitorOfIcon(_settings.Current, id, entries, monitors) is null) throw ProtocolException.NotFound("no such icon");
        await _settings.UpdateAsync(s => DesktopSettingsOps.MoveIcon(s, id, new GridCell(p.X, p.Y), entries, monitors).Settings, ct).ConfigureAwait(false);
    }

    private void Open(IdParams p)
    {
        var id = ValidateId(p.Id);
        var s = _settings.Current;
        string path;
        if (id.StartsWith(DesktopLayout.ShortcutPrefix, StringComparison.Ordinal))
        {
            var sc = s.Desktop.Shortcuts.FirstOrDefault(x => DesktopLayout.ShortcutIconId(x) == id) ?? throw ProtocolException.NotFound("no such shortcut");
            // Re-validated at use time: config.json is user-editable and the file may be gone.
            if (!InputPolicy.TryNormalizeLocalPath(sc.Target, out path)) throw ProtocolException.Failed("the shortcut target is not a valid local path");
            if (!_probe.FileExists(path) && !_probe.DirectoryExists(path)) throw ProtocolException.NotFound("the shortcut target no longer exists");
        }
        else
        {
            var entry = EntriesForHandler().FirstOrDefault(e => e.Id == id) ?? throw ProtocolException.NotFound("no such icon");
            if (entry.IsSpecial)
            {
                // Only the two documented shell: literals may ever be opened, whatever the folder source reports.
                OpenOrThrow(DesktopLayout.SpecialTarget(id) ?? throw ProtocolException.NotFound("no such icon"));
                return;
            }
            if (!InputPolicy.TryNormalizeLocalPath(entry.Target, out path)) throw ProtocolException.Failed("the item is not a valid local path");
        }

        if (!DesktopPathRules.IsLaunchable(path)) { OpenOrThrow(path); return; }
        switch (_launcher.Launch(LaunchTarget.FromPath(path)))
        {
            case LaunchResult.Started: return;
            case LaunchResult.NotFound: throw ProtocolException.NotFound("the target no longer exists");
            default: throw ProtocolException.Failed("could not start the program");
        }
    }

    private void OpenOrThrow(string target)
    {
        if (!_launcher.Open(target)) throw ProtocolException.Failed("could not open the item");
    }

    private async Task<DesktopIcon> AddShortcutAsync(AddShortcutParams p, CancellationToken ct)
    {
        var path = ValidateUserPath(p.Path);
        var monitors = CurrentMonitors();
        var monitor = ResolveMonitor(monitors, p.MonitorId);
        var isDir = _probe.DirectoryExists(path);
        if (!isDir && !_probe.FileExists(path)) throw ProtocolException.NotFound("the path does not exist");

        var entries = EntriesForHandler();
        var id = DesktopLayout.ShortcutPrefix + Guid.NewGuid().ToString("N");
        var name = InputPolicy.Truncate(DesktopPathRules.DisplayName(path), 256);
        GridCell? wanted = p.X is { } x && p.Y is { } y ? new GridCell(x, y) : null;

        AddShortcutResult? result = null;
        await _settings.UpdateAsync(s => (result = DesktopSettingsOps.AddShortcut(s, entries, monitors, monitor, p.MonitorId, id, name, path, wanted)).Settings, ct).ConfigureAwait(false);
        return result switch
        {
            { Failure: AddShortcutFailure.TooMany } => throw ProtocolException.Failed("too many desktop shortcuts"),
            { Failure: AddShortcutFailure.DesktopFull } => throw ProtocolException.Failed("the desktop has no free space"),
            { Icon: { } icon } => icon,
            _ => throw ProtocolException.Failed("could not add the shortcut"),
        };
    }

    private async Task RemoveShortcutAsync(IdParams p, CancellationToken ct)
    {
        var id = ValidateId(p.Id);
        // Only the user's own shortcuts can be removed: desktop-folder items are real files that this call must never delete or hide.
        if (!id.StartsWith(DesktopLayout.ShortcutPrefix, StringComparison.Ordinal)) throw ProtocolException.BadRequest("only user shortcuts can be removed");
        var found = false;
        await _settings.UpdateAsync(s => { var r = DesktopSettingsOps.RemoveShortcut(s, id); found = r.Found; return r.Settings; }, ct).ConfigureAwait(false);
        if (!found) throw ProtocolException.NotFound("no such shortcut");
    }

    // ------------------------------------------------------------------ wallpaper

    private async Task SetWallpaperAsync(SetWallpaperParams p, CancellationToken ct)
    {
        var path = ValidateUserPath(p.Path);
        if (!DesktopPathRules.IsImage(path)) throw ProtocolException.BadRequest("the wallpaper must be an image file (png, jpg, bmp, webp or gif)");
        var monitors = CurrentMonitors();
        if (p.MonitorId is not null) ResolveMonitor(monitors, p.MonitorId);
        if (!_probe.FileExists(path)) throw ProtocolException.NotFound("the file does not exist");

        try { await Task.Run(() => _wallpapers.SetWallpaper(path, p.MonitorId), ct).ConfigureAwait(false); }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex)
        {
            _log.LogError(ex, "Setting the wallpaper failed");
            throw ProtocolException.Failed("Windows refused to change the wallpaper");
        }

        // Always bust the URL of the affected monitors, even if the path is unchanged (the file's content may differ).
        lock (_wallpaperGate)
            foreach (var m in monitors)
                if (p.MonitorId is null || m.Id == p.MonitorId) BumpRev(m.Id);
        await _settings.UpdateAsync(s => DesktopSettingsOps.SetWallpaper(s, path, p.MonitorId), ct).ConfigureAwait(false);
        _topic?.Invalidate();
    }

    // ------------------------------------------------------------------ widgets

    private async Task<WidgetInstance> AddWidgetAsync(AddWidgetParams p, CancellationToken ct)
    {
        var monitors = CurrentMonitors();
        var monitor = ResolveMonitor(monitors, p.MonitorId);
        var id = WidgetOps.NewId();
        WidgetInstance? created = null;
        var stored = await _settings.UpdateAsync(s =>
        {
            var r = WidgetOps.Add(s, monitors, monitor, p.MonitorId, p.Type, p.Size, p.X, p.Y, id);
            created = r.Widget;
            return r.Settings;
        }, ct).ConfigureAwait(false);
        if (created is null) throw ProtocolException.Failed("too many widgets");
        return stored.Desktop.Widgets.FirstOrDefault(w => w.Id == id) ?? created;
    }

    private async Task MoveWidgetAsync(MoveWidgetParams p, CancellationToken ct)
    {
        var id = ValidateId(p.Id, 128);
        var monitors = CurrentMonitors();
        var found = false;
        await _settings.UpdateAsync(s => { var r = WidgetOps.Move(s, monitors, id, p.X, p.Y); found = r.Widget is not null; return r.Settings; }, ct).ConfigureAwait(false);
        if (!found) throw ProtocolException.NotFound("no such widget");
    }

    private async Task RemoveWidgetAsync(IdParams p, CancellationToken ct)
    {
        var id = ValidateId(p.Id, 128);
        var found = false;
        await _settings.UpdateAsync(s => { var r = WidgetOps.Remove(s, id); found = r.Found; return r.Settings; }, ct).ConfigureAwait(false);
        if (!found) throw ProtocolException.NotFound("no such widget");
    }

    private async Task<WidgetInstance> UpdateWidgetAsync(UpdateWidgetParams p, CancellationToken ct)
    {
        var id = ValidateId(p.Id, 128);
        var config = p.Config is null ? null : WidgetMetrics.ValidateConfig(p.Config);
        var monitors = CurrentMonitors();
        WidgetInstance? updated = null;
        var stored = await _settings.UpdateAsync(s =>
        {
            var r = WidgetOps.Update(s, monitors, id, p.Size, config);
            updated = r.Widget;
            return r.Settings;
        }, ct).ConfigureAwait(false);
        if (updated is null) throw ProtocolException.NotFound("no such widget");
        return stored.Desktop.Widgets.FirstOrDefault(w => w.Id == id) ?? updated;
    }
}
