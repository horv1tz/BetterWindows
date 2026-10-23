using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: apps stream. Replace with the real implementation.</summary>
public sealed class ShellAppCatalogSource : IAppCatalogSource
{
    public Task<IReadOnlyList<AppEntry>> ScanAsync(CancellationToken ct = default) => Task.FromResult<IReadOnlyList<AppEntry>>(Array.Empty<AppEntry>());
    public event Action? Changed { add { } remove { } }
}
