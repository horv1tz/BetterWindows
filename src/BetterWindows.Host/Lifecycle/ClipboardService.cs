using BetterWindows.Core.Platform;

namespace BetterWindows.Host.Lifecycle;

/// <summary>STUB — owner: host-lifecycle. Must marshal to the WPF dispatcher (STA) and retry on CLIPBRD_E_CANT_OPEN.</summary>
public sealed class ClipboardService : IClipboard
{
    public void SetText(string text) { }
}
