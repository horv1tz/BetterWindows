using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Native;

/// <summary>STUB — owner: status stream. Replace with the real implementation.</summary>
public sealed class SmtcMediaSource : IMediaSource
{
    public MediaState Current { get; } = new();
    public event Action? Changed { add { } remove { } }
    public Task ControlAsync(MediaAction action, double? positionSec = null) => Task.CompletedTask;
    public Task<(byte[] Data, string ContentType)?> GetArtAsync(CancellationToken ct = default) => Task.FromResult<(byte[] Data, string ContentType)?>(null);
}
