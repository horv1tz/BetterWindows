using System.Text.Json.Nodes;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Services;

// Shared Core services. Interfaces are the contract between parallel work streams; implementations are owned
// by the stream named in the doc comment.

/// <summary>Owner: settings stream. Immutable <see cref="Settings"/> snapshots persisted to %LOCALAPPDATA%\BetterWindows\config.json.</summary>
public interface ISettingsService
{
    Settings Current { get; }
    /// <summary>Raised AFTER the new value is stored (and saved or queued for saving). Any thread.</summary>
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

/// <summary>Owner: windows/dock stream. The taskbar-eligible windows with app identity, kept in sync from IWindowSystem events.</summary>
public interface IWindowTracker
{
    /// <summary>Eligible windows, most recently activated first.</summary>
    IReadOnlyList<TrackedWindow> Windows { get; }
    /// <summary>Last foreground eligible window that is not one of ours (null if none).</summary>
    TrackedWindow? Foreground { get; }
    TrackedWindow? Find(long handle);
    /// <summary>Any membership/title/state/flash change; coalesced (>= 30 ms apart). Any thread.</summary>
    event Action? Changed;
    event Action<TrackedWindow?>? ForegroundChanged;
    void Start();
    void Stop();
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
