using System.Text;

// NOTE: the namespace is Config (not Settings): a namespace BetterWindows.Core.Settings would shadow the protocol type Settings everywhere under BetterWindows.Core.*
namespace BetterWindows.Core.Config;

/// <summary>
/// Validation and canonicalisation of hotkey strings for the settings validator (docs/IPC.md section 8.1): <c>Mod+Mod+Key</c>, modifiers Ctrl/Control, Alt, Shift,
/// Win/Super (case-insensitive, any order), keys A-Z, 0-9, F1-F24, named keys and Num0-Num9, at least one modifier unless the key is F1-F24; canonical output order is
/// <c>Ctrl+Alt+Shift+Win+Key</c>. This is a self-contained copy of the grammar so the settings stream does not depend on the input stream's HotkeyParser; both implement the
/// same section 8.1 and the integrator may route this class through HotkeyParser later (the only call site is <see cref="SettingsValidator"/>).
/// </summary>
internal static class HotkeySyntax
{
    private static readonly string[] NamedKeys =
    {
        "Space", "Enter", "Tab", "Esc", "Backspace", "Delete", "Insert", "Home", "End", "PageUp", "PageDown", "Up", "Down", "Left", "Right",
        "Backtick", "Minus", "Equals", "LeftBracket", "RightBracket", "Backslash", "Semicolon", "Quote", "Comma", "Period", "Slash",
    };

    private static readonly Dictionary<string, string> KeyByLower = BuildKeys();

    private static Dictionary<string, string> BuildKeys()
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var c = 'A'; c <= 'Z'; c++) d[c.ToString()] = c.ToString();
        for (var c = '0'; c <= '9'; c++) d[c.ToString()] = c.ToString();
        for (var i = 1; i <= 24; i++) d["F" + i] = "F" + i;
        for (var i = 0; i <= 9; i++) d["Num" + i] = "Num" + i;
        foreach (var k in NamedKeys) d[k] = k;
        return d;
    }

    /// <summary>True and the canonical spelling for a valid, non-empty hotkey string. Empty/whitespace is NOT valid here (callers treat "" as "unassigned" themselves).</summary>
    public static bool TryCanonicalize(string? text, out string canonical)
    {
        canonical = "";
        if (string.IsNullOrWhiteSpace(text) || text.Length > 64) return false;
        var parts = text.Split('+');
        if (parts.Length is < 1 or > 5) return false;
        bool ctrl = false, alt = false, shift = false, win = false;
        for (var i = 0; i < parts.Length - 1; i++)
        {
            switch (parts[i].Trim().ToLowerInvariant())
            {
                case "ctrl" or "control": if (ctrl) return false; ctrl = true; break;
                case "alt": if (alt) return false; alt = true; break;
                case "shift": if (shift) return false; shift = true; break;
                case "win" or "super": if (win) return false; win = true; break;
                default: return false;
            }
        }
        if (!KeyByLower.TryGetValue(parts[^1].Trim(), out var key)) return false;
        var isFunctionKey = key.Length >= 2 && key[0] == 'F' && char.IsAsciiDigit(key[1]);
        if (!(ctrl || alt || shift || win) && !isFunctionKey) return false;
        var sb = new StringBuilder();
        if (ctrl) sb.Append("Ctrl+");
        if (alt) sb.Append("Alt+");
        if (shift) sb.Append("Shift+");
        if (win) sb.Append("Win+");
        sb.Append(key);
        canonical = sb.ToString();
        return true;
    }
}
