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

public interface IDesktopIconsController : IDisposable
{
    /// <summary>Show/hide Explorer's own desktop icons (so BetterWindows' desktop layer can draw its own). Restore() must undo.</summary>
    void SetNativeIconsVisible(bool visible);
    void Restore();
}
