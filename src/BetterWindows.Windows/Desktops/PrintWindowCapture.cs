using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: desktops stream. Replace with the real implementation.</summary>
public sealed class PrintWindowCapture : IWindowCapture
{
    public Task<byte[]?> CaptureAsync(long handle, int maxWidth, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
}
