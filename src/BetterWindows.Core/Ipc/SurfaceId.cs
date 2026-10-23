using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Ipc;

/// <summary>Identifies one WebView surface instance: kind + monitor (null for the single dock/overlay).</summary>
public readonly record struct SurfaceId(SurfaceKind Kind, string? MonitorId = null)
{
    public override string ToString() => MonitorId is null ? Kind.ToString().ToLowerInvariant() : $"{Kind.ToString().ToLowerInvariant()}:{MonitorId}";
}

/// <summary>Transport to one WebView. Implemented by the Host (PostWebMessageAsString on the UI thread).</summary>
public interface ISurfaceChannel
{
    SurfaceId Id { get; }
    /// <summary>Queue a JSON envelope for delivery to the page. Must be non-blocking and thread-safe.</summary>
    void Post(string json);
}
