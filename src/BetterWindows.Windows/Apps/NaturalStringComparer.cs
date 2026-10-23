namespace BetterWindows.Native;

/// <summary>
/// "Natural" file-name order like Explorer's (StrCmpLogicalW): case-insensitive, and runs of digits compare by numeric value, so "file2" sorts before "file10".
/// Pure managed code so the order is identical in tests on any OS.
/// </summary>
internal sealed class NaturalStringComparer : IComparer<string>
{
    public static readonly NaturalStringComparer Instance = new();

    public int Compare(string? x, string? y)
    {
        if (ReferenceEquals(x, y)) return 0;
        if (x == null) return -1;
        if (y == null) return 1;

        int i = 0, j = 0;
        while (i < x.Length && j < y.Length)
        {
            if (char.IsAsciiDigit(x[i]) && char.IsAsciiDigit(y[j]))
            {
                int si = i, sj = j;
                while (i < x.Length && char.IsAsciiDigit(x[i])) i++;
                while (j < y.Length && char.IsAsciiDigit(y[j])) j++;
                var a = x.AsSpan(si, i - si).TrimStart('0');
                var b = y.AsSpan(sj, j - sj).TrimStart('0');
                if (a.Length != b.Length) return a.Length < b.Length ? -1 : 1; // more significant digits = larger number
                int c = a.CompareTo(b, StringComparison.Ordinal);
                if (c != 0) return c;
                continue;
            }
            char cx = char.ToLowerInvariant(x[i]), cy = char.ToLowerInvariant(y[j]);
            if (cx != cy) return cx < cy ? -1 : 1;
            i++;
            j++;
        }
        if (i < x.Length) return 1;
        if (j < y.Length) return -1;
        // Equal ignoring case: make the order total and stable.
        return string.CompareOrdinal(x, y);
    }
}
