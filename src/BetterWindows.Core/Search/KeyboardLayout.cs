namespace BetterWindows.Core.Search;

/// <summary>
/// Maps keys between the Russian (ЙЦУКЕН) and US (QWERTY) layouts, position by position, so a query typed with the wrong layout can still be matched.
/// Pure data; the mapping is a bijection on its 33 + 33 characters (both cases).
/// </summary>
public static class KeyboardLayout
{
    private const string Latin = "qwertyuiop[]asdfghjkl;'zxcvbnm,`.";
    private const string Russian = "йцукенгшщзхъфывапролджэячсмитьбёю";

    private static readonly Dictionary<char, char> Map = Build();

    /// <summary>Replaces every character that exists on the other layout by its counterpart; other characters are kept.</summary>
    public static string Swap(string? text)
    {
        if (string.IsNullOrEmpty(text)) return "";
        var chars = text.ToCharArray();
        for (int i = 0; i < chars.Length; i++)
        {
            if (Map.TryGetValue(chars[i], out var other)) chars[i] = other;
        }
        return new string(chars);
    }

    /// <summary>The counterpart of one character on the other layout, or null.</summary>
    public static char? Counterpart(char c) => Map.TryGetValue(c, out var other) ? other : null;

    private static Dictionary<char, char> Build()
    {
        var map = new Dictionary<char, char>();
        if (Latin.Length != Russian.Length) throw new InvalidOperationException("keyboard layout tables must be the same length");
        for (int i = 0; i < Latin.Length; i++)
        {
            Add(map, Latin[i], Russian[i]);
            // Only letters have an uppercase form; ',' '.' etc. must keep their single mapping.
            if (char.IsLetter(Latin[i])) Add(map, char.ToUpperInvariant(Latin[i]), char.ToUpperInvariant(Russian[i]));
        }
        return map;
    }

    private static void Add(Dictionary<char, char> map, char latin, char russian)
    {
        map[latin] = russian;
        map[russian] = latin;
    }
}
