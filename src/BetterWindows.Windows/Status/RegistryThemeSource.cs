using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: status stream. Replace with the real implementation.</summary>
public sealed class RegistryThemeSource : IThemeSource
{
    public bool AppsUseLightTheme => true;
    public string AccentColorHex => "#0a84ff";
    public bool ReduceMotion => false;
    public bool ReduceTransparency => false;
    public event Action? Changed { add { } remove { } }
    public void SetAppsUseLightTheme(bool light) { }
}
