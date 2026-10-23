using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: status stream. Replace with the real implementation.</summary>
public sealed class WmiBrightnessControl : IBrightnessControl
{
    public BrightnessState Current { get; } = new();
    public event Action? Changed { add { } remove { } }
    public void SetLevel(double level01) { }
}
