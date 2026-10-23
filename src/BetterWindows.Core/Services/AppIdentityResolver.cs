using BetterWindows.Core.Platform;

namespace BetterWindows.Core.Services;

/// <summary>STUB — owner: windows/dock stream.</summary>
public sealed class AppIdentityResolver : IAppIdentityResolver
{
    public AppIdentity Resolve(RawWindow window) => new(window.ExePath is null ? "unknown" : "exe:" + window.ExePath.ToLowerInvariant(), window.Title, null);
}
