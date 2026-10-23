using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Services;

/// <summary>Default <see cref="IPathGrants"/>: thread-safe, in-memory, session-scoped.</summary>
public sealed class PathGrants : IPathGrants
{
    private readonly object _gate = new();
    private readonly HashSet<string> _granted = new(StringComparer.OrdinalIgnoreCase);

    public void Grant(string path)
    {
        if (!InputPolicy.TryNormalizeLocalPath(path, out var p)) return;
        lock (_gate) _granted.Add(p.TrimEnd('\\'));
    }

    public bool IsGranted(string path)
    {
        if (!InputPolicy.TryNormalizeLocalPath(path, out var p)) return false;
        p = p.TrimEnd('\\');
        lock (_gate)
        {
            if (_granted.Contains(p)) return true;
            foreach (var g in _granted) if (p.StartsWith(g + "\\", StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }
    }
}

/// <summary>Default <see cref="IHotkeyStatus"/>.</summary>
public sealed class HotkeyStatusService : IHotkeyStatus
{
    private readonly object _gate = new();
    private readonly Dictionary<string, HotkeyRegistration> _items = new();
    public event Action? Changed;

    public IReadOnlyList<HotkeyRegistration> Items { get { lock (_gate) return _items.Values.ToArray(); } }
    public bool EscapeHotkeyRegistered { get { lock (_gate) return (_items.TryGetValue("exit", out var e) && e.Registered) || (_items.TryGetValue("toggleTaskbar", out var t) && t.Registered); } }

    public void Report(HotkeyRegistration registration) { lock (_gate) _items[registration.Id] = registration; Changed?.Invoke(); }
    public void Clear(string id) { bool removed; lock (_gate) removed = _items.Remove(id); if (removed) Changed?.Invoke(); }
}

/// <summary>Default <see cref="IOverlayState"/>; OverlayController calls <see cref="Set"/> on every show and hide, whoever initiated it.</summary>
public sealed class OverlayStateService : IOverlayState
{
    private readonly object _gate = new();
    private OverlayState _current = OverlayState.Hidden;
    public event Action<OverlayState>? Changed;
    public OverlayState Current { get { lock (_gate) return _current; } }

    public void Set(OverlayState state)
    {
        lock (_gate)
        {
            if (_current == state) return;
            _current = state;
        }
        Changed?.Invoke(state);
    }
}
