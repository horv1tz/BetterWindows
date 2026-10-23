using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: apps stream. Replace with the real implementation.</summary>
public sealed class DesktopWallpaperService : IWallpaperService
{
    public string? GetWallpaperPath(string monitorId) => null;
    public void SetWallpaper(string path, string? monitorId) { }
}
