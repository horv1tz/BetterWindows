using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Services;

/// <summary>STUB — owner: settings stream. Backed by IAppCatalogSource.</summary>
public sealed class AppCatalog : IAppCatalog
{
    public IReadOnlyList<AppEntry> Apps => Array.Empty<AppEntry>();
    public AppEntry? FindById(string id) => null;
    public AppEntry? FindByAumid(string aumid) => null;
    public AppEntry? FindByExePath(string exePath) => null;
    public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
    public event Action? Changed { add { } remove { } }
}
