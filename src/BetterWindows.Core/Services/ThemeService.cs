using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Services;

/// <summary>STUB — owner: settings stream.</summary>
public sealed class ThemeService : IThemeService
{
    public ThemeState Current { get; } = new();
    public event Action<ThemeState>? Changed { add { } remove { } }
}
