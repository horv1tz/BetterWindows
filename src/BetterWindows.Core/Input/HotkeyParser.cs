using BetterWindows.Core.Platform;

namespace BetterWindows.Core.Input;

/// <summary>
/// Parses and formats hotkey strings such as "Ctrl+Alt+Q", "Alt+Space", "Win+Shift+S", "F4" (docs/IPC.md section 8.1). Pure and culture-independent.
/// <para>Grammar (docs/IPC.md 8.1, mirrored exactly by the settings UI): <c>Mod+Mod+Key</c>. Modifiers: Ctrl/Control, Alt, Shift, Win/Super (case-insensitive, any order).
/// Keys (case-insensitive): A-Z, 0-9, F1-F24, Space, Enter, Tab, Esc, Backspace, Delete, Insert, Home, End, PageUp, PageDown, Up/Down/Left/Right, punctuation by name
/// (Backtick, Minus, Equals, LeftBracket, RightBracket, Backslash, Semicolon, Quote, Comma, Period, Slash) and Num0-Num9. At least one modifier is required unless the key is F1-F24.
/// Whitespace around each token is trimmed; whitespace inside a token is an error ("Page Up" is not "PageUp"). No other aliases exist: "Cmd", "Windows", "Escape", "Del", "," etc. are rejected.
/// Rejected: empty input, modifier-only, duplicate modifiers, more than one key, unknown keys, input longer than 64 characters.</para>
/// <para>Modifiers use the Win32 MOD_* values, the key is a Win32 virtual-key code, so a <see cref="HotkeyGesture"/> goes straight into RegisterHotKey.
/// Canonical output order is <c>Ctrl+Alt+Shift+Win+Key</c>.</para>
/// </summary>
public static class HotkeyParser
{
    /// <summary>Longest accepted input (matches the maxLength of the hotkey fields in protocol/settings.json).</summary>
    public const int MaxLength = 64;

    /// <summary>MOD_NOREPEAT: accepted (and ignored) by <see cref="TryFormat"/> because registrars add it themselves.</summary>
    private const uint ModNoRepeat = 0x4000;
    private const uint ModMask = HotkeyGesture.Alt | HotkeyGesture.Control | HotkeyGesture.Shift | HotkeyGesture.Win;

    private static readonly Dictionary<string, uint> Modifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        ["ctrl"] = HotkeyGesture.Control,
        ["control"] = HotkeyGesture.Control,
        ["alt"] = HotkeyGesture.Alt,
        ["shift"] = HotkeyGesture.Shift,
        ["win"] = HotkeyGesture.Win,
        ["super"] = HotkeyGesture.Win,
    };

    /// <summary>Canonical key names (VK_* codes). Letters, digits, F-keys and Num-keys are added programmatically.</summary>
    private static readonly (string Name, uint Vk)[] NamedKeys =
    {
        ("Space", 0x20), ("Enter", 0x0D), ("Tab", 0x09), ("Esc", 0x1B), ("Backspace", 0x08), ("Delete", 0x2E), ("Insert", 0x2D),
        ("Home", 0x24), ("End", 0x23), ("PageUp", 0x21), ("PageDown", 0x22),
        ("Left", 0x25), ("Up", 0x26), ("Right", 0x27), ("Down", 0x28),
        ("Comma", 0xBC), ("Period", 0xBE), ("Slash", 0xBF), ("Backslash", 0xDC), ("Semicolon", 0xBA), ("Quote", 0xDE),
        ("Minus", 0xBD), ("Equals", 0xBB), ("Backtick", 0xC0), ("LeftBracket", 0xDB), ("RightBracket", 0xDD),
    };

    private static readonly Dictionary<string, uint> KeyByName = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<uint, string> NameByKey = new();

    static HotkeyParser()
    {
        void Add(string name, uint vk) { KeyByName[name] = vk; NameByKey[vk] = name; }
        for (var c = 'A'; c <= 'Z'; c++) Add(c.ToString(), c);
        for (var c = '0'; c <= '9'; c++) Add(c.ToString(), c);
        for (var i = 1; i <= 24; i++) Add("F" + i, (uint)(0x70 + i - 1));
        for (var i = 0; i <= 9; i++) Add("Num" + i, (uint)(0x60 + i));
        foreach (var (name, vk) in NamedKeys) Add(name, vk);
    }

    /// <summary>Parses a hotkey string; false (and a default gesture) for anything the grammar rejects, including null/empty.</summary>
    public static bool TryParse(string? text, out HotkeyGesture gesture)
    {
        gesture = default;
        if (string.IsNullOrWhiteSpace(text) || text.Length > MaxLength) return false;

        uint modifiers = 0;
        uint? key = null;
        foreach (var raw in text.Split('+'))
        {
            var part = raw.Trim();
            if (part.Length == 0) return false; // "Ctrl++Q", "+Q", "Q+"
            if (Modifiers.TryGetValue(part, out var mod))
            {
                if ((modifiers & mod) != 0) return false; // duplicate, also via aliases ("Ctrl+Control+Q")
                modifiers |= mod;
                continue;
            }
            if (key is not null) return false; // two keys
            if (!KeyByName.TryGetValue(part, out var vk)) return false;
            key = vk;
        }

        if (key is null) return false; // modifier-only
        if (modifiers == 0 && !IsFunctionKey(key.Value)) return false;
        gesture = new HotkeyGesture(modifiers, key.Value);
        return true;
    }

    /// <summary>Parses a hotkey string; null when it is not valid.</summary>
    public static HotkeyGesture? TryParse(string? text) => TryParse(text, out var g) ? g : null;

    /// <summary>Parses a hotkey string or throws <see cref="FormatException"/>.</summary>
    public static HotkeyGesture Parse(string? text) =>
        TryParse(text, out var g) ? g : throw new FormatException($"'{text}' is not a valid hotkey");

    /// <summary>True if <paramref name="text"/> is a valid, non-empty hotkey.</summary>
    public static bool IsValid(string? text) => TryParse(text, out _);

    /// <summary>Parses and re-formats: the canonical spelling of a valid hotkey ("control + alt + q" -> "Ctrl+Alt+Q").</summary>
    public static bool TryNormalize(string? text, out string canonical)
    {
        canonical = "";
        return TryParse(text, out var g) && TryFormat(g, out canonical);
    }

    /// <summary>Canonical string of a gesture, or "" (unassigned) when the gesture cannot be expressed in the grammar (unknown key, stray modifier bits, no modifier for a non-F key).</summary>
    public static string Format(HotkeyGesture gesture) => TryFormat(gesture, out var s) ? s : "";

    /// <summary>Like <see cref="Format"/> but tells whether the gesture was representable. MOD_NOREPEAT is ignored.</summary>
    public static bool TryFormat(HotkeyGesture gesture, out string text)
    {
        text = "";
        var mods = gesture.Modifiers & ~ModNoRepeat;
        if ((mods & ~ModMask) != 0) return false;
        if (!NameByKey.TryGetValue(gesture.VirtualKey, out var name)) return false;
        if (mods == 0 && !IsFunctionKey(gesture.VirtualKey)) return false;

        var sb = new System.Text.StringBuilder(24);
        if ((mods & HotkeyGesture.Control) != 0) sb.Append("Ctrl+");
        if ((mods & HotkeyGesture.Alt) != 0) sb.Append("Alt+");
        if ((mods & HotkeyGesture.Shift) != 0) sb.Append("Shift+");
        if ((mods & HotkeyGesture.Win) != 0) sb.Append("Win+");
        sb.Append(name);
        text = sb.ToString();
        return true;
    }

    private static bool IsFunctionKey(uint vk) => vk is >= 0x70 and <= 0x87;
}
