using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Platform;

public interface IAppCatalogSource
{
    /// <summary>Enumerate all start-menu apps (shell:AppsFolder + .lnk files). Entries need Id, Name, Aumid/Path, Packaged; IconUrl is set by Core.</summary>
    Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default);
    /// <summary>Raised when the start-menu folders change.</summary>
    event Action? Changed;
}

public interface IIconSource
{
    /// <summary>PNG (square, transparent background) at roughly <paramref name="size"/> px; null if unavailable.</summary>
    Task<byte[]?> GetIconPngAsync(IconKey key, int size, CancellationToken ct = default);
}

public interface IAppLauncher
{
    /// <summary>Start an app by AUMID (shell:AppsFolder\X) or by exe path. New window if already running.</summary>
    LaunchResult Launch(LaunchTarget target, string? arguments = null);
    /// <summary>ShellExecute a URL / file / folder / ms-settings: URI.</summary>
    bool Open(string target);
    /// <summary>Open Explorer with the item selected.</summary>
    bool RevealInExplorer(string path);
}

public interface IFileSearch
{
    bool IsAvailable { get; }
    /// <summary>Windows Search index query (SYSTEMINDEX). Streams hits; honours cancellation quickly.</summary>
    IAsyncEnumerable<FileHit> SearchAsync(string query, int limit, CancellationToken ct = default);
}

public interface IAutostartService
{
    bool IsEnabled { get; }
    void SetEnabled(bool enabled);
}

public interface IWallpaperService
{
    /// <summary>Current wallpaper file for the monitor id (MonitorInfo.id), null if none/solid colour.</summary>
    string? GetWallpaperPath(string monitorId);
    void SetWallpaper(string path, string? monitorId);
}

/// <summary>
/// Everything that needs Explorer's desktop window structure (Progman / WorkerW / SHELLDLL_DefView / SysListView32) lives HERE, so there is exactly one copy of that
/// fragile logic. Before the first mutation the original state is persisted to %LOCALAPPDATA%\BetterWindows\state\desktopicons.json; Restore() works in a fresh process
/// (reads the snapshot, undoes, deletes it) and is idempotent. Prefer ShowWindow(SW_HIDE/SW_SHOW) on the SysListView32 over the persistent HideIcons toggle.
/// </summary>
public interface IDesktopIconsController : IDisposable
{
    /// <summary>Show/hide Explorer's own desktop icons (so BetterWindows' desktop layer can draw its own). Restore() must undo.</summary>
    void SetNativeIconsVisible(bool visible);
    /// <summary>
    /// Parent/order the surface window so it is above the wallpaper and below normal windows (WorkerW technique; Windows 11 24H2 layout aware), receiving input when native icons
    /// are hidden. Returns false on an unrecognised shell layout: the Host then falls back to a HWND_BOTTOM + NOACTIVATE window kept at the bottom of the z-order.
    /// </summary>
    bool TryAttachDesktopLayer(nint hwnd, NativeRect boundsPx);
    void DetachDesktopLayer(nint hwnd);
    /// <summary>Explorer restarted or the desktop window structure changed (TaskbarCreated / lost WorkerW parent): the Host must re-attach its desktop windows.</summary>
    event Action? ShellRestarted;
    void Restore();
}

/// <summary>One item of the user's desktop (files/folders of %USERPROFILE%\Desktop and the public desktop, plus specials).</summary>
public sealed record DesktopEntry(string Id, string Name, string Target, bool IsFolder, bool IsSpecial);

/// <summary>
/// Enumerates what Explorer would show on the desktop, so enabling our desktop layer (which hides Explorer's icons) does not make those items unreachable.
/// Ids: "fs:<lower-cased full path>", specials "special:thisPc" and "special:recycleBin" (Target = shell:MyComputerFolder / shell:RecycleBinFolder).
/// </summary>
public interface IDesktopFolderSource
{
    IReadOnlyList<DesktopEntry> Enumerate();
    /// <summary>FileSystemWatcher on both desktop folders, debounced 300 ms.</summary>
    event Action? Changed;
}
