using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: apps stream. Replace with the real implementation.</summary>
public sealed class ShellIconSource : IIconSource
{
    public Task<byte[]?> GetIconPngAsync(IconKey key, int size, CancellationToken ct = default) => Task.FromResult<byte[]?>(null);
}
