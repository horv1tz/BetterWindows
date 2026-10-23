using System.Text.Json.Nodes;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Services;

// Shared Core services. Interfaces are the contract between parallel work streams; implementations are owned
// by the stream named in the doc comment.

/// <summary>
/// Owner: settings stream. Immutable <see cref="Settings"/> snapshots persisted to <see cref="IAppPaths.ConfigFile"/>. Normative behaviour (docs/IPC.md section 12):
/// load = size cap 2 MB, tolerant parse (comments/trailing commas), explicit nulls normalised to defaults, each top-level section bound separately (one bad value resets
/// only that field/section), corrupt file -> quarantine to config.corrupt-&lt;utc&gt;.json (keep 5), fall back to config.json.bak, else defaults, and NEVER save over an
/// unquarantined corrupt file; the raw root JsonObject is kept so unknown keys survive saves; version newer than known -> no migration, keep unknown parts; older -> sequential
/// migrations with config.v{n}.bak written once. One SemaphoreSlim serialises Patch/Update/Reload; saves are debounced 300-500 ms and flushed on Stop; every write is validated
/// by SettingsValidator (limits from protocol/settings.json -> SettingsLimits). config.json is shared by all sessions of the user (last writer wins).
/// </summary>
public interface ISettingsService
{
    Settings Current { get; }
    /// <summary>
    /// Raised AFTER the new value is stored, after the write gate is released, on the thread that completed the write. Subscribers must not wait on UpdateAsync synchronously
    /// and must converge (a write that re-triggers Changed must reach a fixed point). Coalesced to >= 16 ms.
    /// </summary>
    event Action<Settings>? Changed;
    /// <summary>Deep-merge a JSON patch, validate/clamp, persist, raise Changed. Returns the stored value.</summary>
    Task<Settings> PatchAsync(JsonNode patch, CancellationToken ct = default);
    /// <summary>Atomic read-modify-write for controllers (e.g. pin an app).</summary>
    Task<Settings> UpdateAsync(Func<Settings, Settings> mutate, CancellationToken ct = default);
    /// <summary>Re-read config.json from disk (file watcher / manual edit).</summary>
    void Reload();
}

/// <summary>Owner: settings stream. Effective theme = settings.appearance + Windows personalisation (IThemeSource).</summary>
public interface IThemeService
{
    ThemeState Current { get; }
    event Action<ThemeState>? Changed;
}

/// <summary>Owner: settings stream. Installed apps (start menu) with stable ids.</summary>
public interface IAppCatalog
{
    IReadOnlyList<AppEntry> Apps { get; }
    AppEntry? FindById(string id);
    AppEntry? FindByAumid(string aumid);
    /// <summary>Case-insensitive full-path match against AppEntry.Path.</summary>
    AppEntry? FindByExePath(string exePath);
    Task RefreshAsync(CancellationToken ct = default);
    event Action? Changed;
}

public sealed record AppIdentity(string AppId, string Name, AppEntry? Entry);

/// <summary>Owner: windows/dock stream. Maps a window to a stable app id + display name (see AppEntry.Id rules).</summary>
public interface IAppIdentityResolver
{
    AppIdentity Resolve(RawWindow window);
}

public sealed record TrackedWindow(RawWindow Raw, string AppId, string AppName)
{
    public long Handle => Raw.Handle;
}

/// <summary>One immutable, internally consistent view of the tracked windows (never combine Windows from one version with Foreground from another).</summary>
public sealed record WindowSnapshot(long Version, IReadOnlyList<TrackedWindow> Windows, TrackedWindow? Foreground)
{
    public static readonly WindowSnapshot Empty = new(0, Array.Empty<TrackedWindow>(), null);
}

/// <summary>
/// A Core service with a start/stop lifecycle owned by <c>ShellRuntime</c>: started BEFORE all controllers, stopped AFTER them. Idempotent; Start never throws (log and degrade).
/// </summary>
public interface IShellService
{
    string Name { get; }
    void Start();
    void Stop();
}

/// <summary>Owner: windows/dock stream. The taskbar-eligible windows with app identity, kept in sync from IWindowSystem events.</summary>
public interface IWindowTracker : IShellService
{
    /// <summary>The current immutable snapshot (replaced atomically). Use this when you need windows AND foreground together.</summary>
    WindowSnapshot Snapshot { get; }
    /// <summary>Eligible windows, most recently activated first. Convenience for display only: <c>Snapshot.Windows</c>.</summary>
    IReadOnlyList<TrackedWindow> Windows { get; }
    /// <summary>Last foreground eligible window that is not one of ours (null if none). <c>Snapshot.Foreground</c>.</summary>
    TrackedWindow? Foreground { get; }
    /// <summary>The tracked window for a handle (null if unknown, or if it is one of OUR windows: such handles must never be acted upon).</summary>
    TrackedWindow? Find(long handle);
    /// <summary>Any membership/title/state/flash/foreground change; coalesced (>= 30 ms apart). Any thread. Not raised while holding locks.</summary>
    event Action<WindowSnapshot>? Changed;
    event Action<TrackedWindow?>? ForegroundChanged;
    /// <summary>Start() starts IWindowSystem and takes the first snapshot; Stop() stops it. Nobody else calls IWindowSystem.Start/Stop.</summary>
}

public sealed record DynResource(byte[] Data, string ContentType, TimeSpan? MaxAge = null);

/// <summary>Owner: settings stream. Resolves "/dyn/..." URLs (see <see cref="DynUrls"/>) for the Host's WebResourceRequested handler.</summary>
public interface IDynamicResourceProvider
{
    /// <param name="pathAndQuery">e.g. "/dyn/icon/app/aumid%3AMicrosoft.Edge?s=128"</param>
    Task<DynResource?> GetAsync(string pathAndQuery, CancellationToken ct = default);
    /// <summary>
    /// Capture and cache a window's thumbnail NOW (best effort, never throws). Mission Control shows other-desktop/minimised windows from the last good capture, because those
    /// cannot be captured later; core-desktop calls this for the window that just LOST the foreground.
    /// </summary>
    Task PrewarmWindowThumbnailAsync(long handle, CancellationToken ct = default);
}

/// <summary>Where Core keeps its files. One place, injectable, overridable (BETTERWINDOWS_DATA_DIR / --data-dir): tests use a temp directory.</summary>
public interface IAppPaths
{
    /// <summary>%LOCALAPPDATA%\BetterWindows by default.</summary>
    string Root { get; }
    string ConfigFile { get; }
    /// <summary>reminders.json, search usage, caches that must survive.</summary>
    string DataDir { get; }
    /// <summary>Restore journals (taskbar.&lt;session&gt;.json ...), startup.json, window state. Also read by the static restorers.</summary>
    string StateDir { get; }
    string LogsDir { get; }
    /// <summary>Icon/thumbnail disk cache; safe to delete.</summary>
    string CacheDir { get; }
    string WebView2Dir { get; }
}

/// <summary>Who owns the overlay right now. Implemented by OverlayController (wraps ISurfaceManager + OverlayHidden); every show and hide, whoever initiated it.</summary>
public sealed record OverlayState(bool Visible, OverlayMode? Mode, string? MonitorId, int Token, PopoverKind? Popover, string? PopoverItemId)
{
    public static readonly OverlayState Hidden = new(false, null, null, 0, null, null);
}

public interface IOverlayState
{
    OverlayState Current { get; }
    event Action<OverlayState>? Changed;
}

/// <summary>
/// A feature slice: registers its message handlers in <see cref="Start"/>, subscribes to ports/services, and pushes
/// state events (also to a surface that just completed surface.ready: use SurfaceRegistry.SurfaceReady).
/// Start must not block; a controller that throws in Start is logged and skipped (others keep running).
/// </summary>
public interface IShellController
{
    string Name { get; }
    void Start();
    void Stop();
}
