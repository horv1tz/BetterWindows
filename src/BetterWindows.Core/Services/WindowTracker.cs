using BetterWindows.Core.Platform;

namespace BetterWindows.Core.Services;

/// <summary>STUB — owner: windows/dock stream. Backed by IWindowSystem + WindowFilter + IAppIdentityResolver.</summary>
public sealed class WindowTracker : IWindowTracker
{
    public IReadOnlyList<TrackedWindow> Windows => Array.Empty<TrackedWindow>();
    public TrackedWindow? Foreground => null;
    public TrackedWindow? Find(long handle) => null;
    public event Action? Changed { add { } remove { } }
    public event Action<TrackedWindow?>? ForegroundChanged { add { } remove { } }
    public void Start() { }
    public void Stop() { }
}
