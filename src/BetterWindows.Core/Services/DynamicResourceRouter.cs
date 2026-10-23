namespace BetterWindows.Core.Services;

/// <summary>STUB — owner: settings stream. Routes /dyn/* to IIconSource, IWindowCapture, IMediaSource, IWallpaperService, ITrayHost.</summary>
public sealed class DynamicResourceRouter : IDynamicResourceProvider
{
    public Task<DynResource?> GetAsync(string pathAndQuery, CancellationToken ct = default) => Task.FromResult<DynResource?>(null);
}
