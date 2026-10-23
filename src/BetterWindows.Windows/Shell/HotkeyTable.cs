using BetterWindows.Core.Platform;

namespace BetterWindows.Native;

/// <summary>
/// Pure bookkeeping for the hotkey registrar: string id &lt;-&gt; numeric RegisterHotKey id, duplicate-gesture detection and gesture
/// validation. Not thread-safe (it is only touched on the registrar's message-loop thread). Cross-platform and unit-tested.
/// </summary>
internal sealed class HotkeyTable
{
    /// <summary>RegisterHotKey ids for applications are 0x0000..0xBFFF.</summary>
    public const int MaxId = 0xBFFF;

    private const uint KnownModifiers = HotkeyGesture.Alt | HotkeyGesture.Control | HotkeyGesture.Shift | HotkeyGesture.Win;

    private readonly Dictionary<string, (int Id, HotkeyGesture Gesture)> _byName = new(StringComparer.Ordinal);
    private readonly Dictionary<int, string> _byId = new();
    private int _next = 1;

    /// <summary>Number of registered hotkeys.</summary>
    public int Count => _byName.Count;

    /// <summary>Basic sanity of a registration request (not the key-name grammar, that is the parser's job).</summary>
    public static bool IsValid(string id, HotkeyGesture gesture, out string reason)
    {
        if (string.IsNullOrWhiteSpace(id) || id.Length > 128)
        {
            reason = "invalid hotkey id";
            return false;
        }

        if (gesture.VirtualKey is 0 or > 254)
        {
            reason = "invalid virtual key";
            return false;
        }

        if ((gesture.Modifiers & ~KnownModifiers) != 0)
        {
            reason = "invalid modifiers";
            return false;
        }

        reason = "";
        return true;
    }

    /// <summary>Reserves a numeric id for <paramref name="name"/>; false if another name already uses the same gesture or the id is taken.</summary>
    public bool TryReserve(string name, HotkeyGesture gesture, out int id, out string? conflictWith)
    {
        id = 0;
        conflictWith = null;
        if (_byName.ContainsKey(name))
        {
            conflictWith = name;
            return false;
        }

        foreach (var (other, entry) in _byName)
        {
            if (entry.Gesture == gesture)
            {
                conflictWith = other;
                return false;
            }
        }

        id = AllocateId();
        _byName[name] = (id, gesture);
        _byId[id] = name;
        return true;
    }

    /// <summary>The current registration of a name.</summary>
    public bool TryGet(string name, out int id, out HotkeyGesture gesture)
    {
        if (_byName.TryGetValue(name, out var e))
        {
            id = e.Id;
            gesture = e.Gesture;
            return true;
        }

        id = 0;
        gesture = default;
        return false;
    }

    /// <summary>Name for a WM_HOTKEY wParam.</summary>
    public bool TryGetName(int id, out string name)
    {
        if (_byId.TryGetValue(id, out var n))
        {
            name = n;
            return true;
        }

        name = "";
        return false;
    }

    /// <summary>Forgets a name; returns its numeric id.</summary>
    public bool Release(string name, out int id)
    {
        if (_byName.Remove(name, out var e))
        {
            _byId.Remove(e.Id);
            id = e.Id;
            return true;
        }

        id = 0;
        return false;
    }

    /// <summary>Snapshot of all registrations.</summary>
    public IReadOnlyList<(string Name, int Id, HotkeyGesture Gesture)> All() =>
        _byName.Select(kv => (kv.Key, kv.Value.Id, kv.Value.Gesture)).ToList();

    /// <summary>Forgets everything.</summary>
    public void Clear()
    {
        _byName.Clear();
        _byId.Clear();
    }

    private int AllocateId()
    {
        for (int i = 0; i < MaxId; i++)
        {
            int candidate = _next;
            _next = _next >= MaxId ? 1 : _next + 1;
            if (!_byId.ContainsKey(candidate))
            {
                return candidate;
            }
        }

        throw new InvalidOperationException("No free hotkey ids");
    }
}
