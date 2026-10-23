using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Ipc;

/// <summary>Live surfaces. The Host registers a channel when a WebView is created and removes it on close.</summary>
public sealed class SurfaceRegistry
{
    private readonly object _gate = new();
    private readonly Dictionary<SurfaceId, ISurfaceChannel> _channels = new();

    /// <summary>Raised AFTER a surface completed surface.ready (controllers push their current state to it).</summary>
    public event Action<SurfaceId>? SurfaceReady;
    public event Action<SurfaceId>? SurfaceClosed;

    public void Register(ISurfaceChannel channel)
    {
        lock (_gate) _channels[channel.Id] = channel;
    }

    public void Unregister(SurfaceId id)
    {
        bool removed;
        lock (_gate) removed = _channels.Remove(id);
        if (removed) SurfaceClosed?.Invoke(id);
    }

    public ISurfaceChannel? Get(SurfaceId id)
    {
        lock (_gate) return _channels.GetValueOrDefault(id);
    }

    public IReadOnlyList<ISurfaceChannel> All()
    {
        lock (_gate) return _channels.Values.ToArray();
    }

    public IReadOnlyList<ISurfaceChannel> OfKind(SurfaceKind kind)
    {
        lock (_gate) return _channels.Values.Where(c => c.Id.Kind == kind).ToArray();
    }

    internal void RaiseReady(SurfaceId id) => SurfaceReady?.Invoke(id);
}
