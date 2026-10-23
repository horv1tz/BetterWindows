using BetterWindows.Core.Platform;

namespace BetterWindows.Core.Services;

/// <summary>STUB — owner: windows/dock stream. Backed by IWindowSystem + WindowFilter + IAppIdentityResolver.</summary>
public sealed class WindowTracker : IWindowTracker
{
    public string Name => "WindowTracker";
    public WindowSnapshot Snapshot => WindowSnapshot.Empty;
    public IReadOnlyList<TrackedWindow> Windows => Snapshot.Windows;
    public TrackedWindow? Foreground => Snapshot.Foreground;
    public TrackedWindow? Find(long handle) => null;
    public event Action<WindowSnapshot>? Changed { add { } remove { } }
    public event Action<TrackedWindow?>? ForegroundChanged { add { } remove { } }
    public void Start() { }
    public void Stop() { }
}
