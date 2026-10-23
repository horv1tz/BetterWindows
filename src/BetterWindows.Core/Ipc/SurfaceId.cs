using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Ipc;

/// <summary>Identifies one WebView surface instance: kind + monitor (null for the single dock/overlay/settings).</summary>
public readonly record struct SurfaceId(SurfaceKind Kind, string? MonitorId = null)
{
    public override string ToString() => MonitorId is null ? Kind.ToString().ToLowerInvariant() : $"{Kind.ToString().ToLowerInvariant()}:{MonitorId}";
}

/// <summary>
/// Transport to one WebView. Implemented by the Host (PostWebMessageAsString on the UI thread). The Host assigns <see cref="Id"/> when it attaches the
/// WebView and it is the ONLY source of a message's sender identity (the page cannot claim another one).
/// Post must be non-blocking, thread-safe, preserve call order (FIFO) and be bounded: when the renderer is stuck, state events coalesce to the latest
/// per type, transient events drop the oldest beyond 64, and nothing is queued before surface.ready or while the surface is failed.
/// </summary>
public interface ISurfaceChannel
{
    SurfaceId Id { get; }
    void Post(string json);
}
