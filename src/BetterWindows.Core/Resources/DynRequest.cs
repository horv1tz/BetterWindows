using System.Globalization;
using System.Text.RegularExpressions;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Resources;

/// <summary>A parsed <c>/dyn/...</c> URL (docs/IPC.md section 8). Sizes are already bucketed; keys are decoded but NOT yet validated against live state.</summary>
public abstract record DynRequest
{
    /// <summary>Optional content-hash / counter in <c>rev</c> (letters, digits, '_' and '-', at most 64 chars); null if absent or malformed.</summary>
    public string? Rev { get; init; }
}

public sealed record AppIconRequest(string AppId, int Size) : DynRequest;
public sealed record WindowIconRequest(long Handle, int Size) : DynRequest;
public sealed record PathIconRequest(string Path, int Size) : DynRequest;
public sealed record ThumbRequest(long Handle, int Width) : DynRequest;
public sealed record ArtRequest(int Serial) : DynRequest;
public sealed record WallpaperRequest(string MonitorId) : DynRequest;
public sealed record TrayIconRequest(string Id) : DynRequest;

/// <summary>
/// Strict parser for the dynamic-resource URL space: only the seven documented shapes, exact segment counts, percent-decoded once, no control characters, no fragments,
/// no empty or dot segments, bounded length. Anything else is "unknown" (null) and ends up as a 404. Unknown query keys are ignored; numeric query values are tolerant
/// (garbage = default, huge = largest bucket) because the URL builders in <see cref="DynUrls"/> are the only legitimate producers.
/// </summary>
public static partial class DynRequestParser
{
    private const int MaxUrlChars = 8192, MaxKeyChars = 4200;
    private const int DefaultIconSize = 128, DefaultThumbWidth = 480;

    [GeneratedRegex(@"^[A-Za-z0-9_-]{1,64}\z", RegexOptions.CultureInvariant)]
    private static partial Regex RevPattern();

    public static bool TryParse(string? pathAndQuery, out DynRequest request)
    {
        request = null!;
        if (string.IsNullOrEmpty(pathAndQuery) || pathAndQuery.Length > MaxUrlChars) return false;
        if (!pathAndQuery.StartsWith(DynUrls.Prefix, StringComparison.Ordinal)) return false;
        foreach (var c in pathAndQuery) if (c < 0x21 || c == 0x7f || c == '#' || c == '\\') return false; // raw whitespace/control/fragment/backslash: builders always escape these

        var q = pathAndQuery.IndexOf('?');
        var path = q >= 0 ? pathAndQuery[..q] : pathAndQuery;
        var query = q >= 0 ? pathAndQuery[(q + 1)..] : "";
        var rawSegments = path[DynUrls.Prefix.Length..].Split('/');
        if (rawSegments.Length < 2) return false;
        var segments = new string[rawSegments.Length];
        for (var i = 0; i < rawSegments.Length; i++)
        {
            if (rawSegments[i].Length == 0) return false;
            if (!TryDecode(rawSegments[i], out segments[i])) return false;
            if (segments[i] is "." or "..") return false;
        }

        var parameters = ParseQuery(query);
        var rev = parameters.TryGetValue("rev", out var r) && RevPattern().IsMatch(r) ? r : null;

        DynRequest? parsed = segments[0] switch
        {
            "icon" => ParseIcon(segments, parameters),
            "thumb" when segments.Length == 2 && TryHandle(segments[1], out var h) => new ThumbRequest(h, ImageFormats.Bucket(NumberOr(parameters, "w", DefaultThumbWidth), ImageFormats.ThumbWidths)),
            "art" when segments.Length == 2 && TryUInt(segments[1], out var serial) => new ArtRequest(serial),
            "wallpaper" when segments.Length == 2 && IsKey(segments[1], 64) => new WallpaperRequest(segments[1]),
            "tray" when segments.Length == 2 && IsKey(segments[1], 512) => new TrayIconRequest(segments[1]),
            _ => null,
        };
        if (parsed is null) return false;
        request = parsed with { Rev = rev };
        return true;
    }

    private static DynRequest? ParseIcon(string[] s, Dictionary<string, string> p)
    {
        if (s.Length != 3) return null;
        var size = ImageFormats.Bucket(NumberOr(p, "s", DefaultIconSize), ImageFormats.IconSizes);
        return s[1] switch
        {
            "app" when IsKey(s[2], MaxKeyChars) => new AppIconRequest(s[2], size),
            "window" when TryHandle(s[2], out var h) => new WindowIconRequest(h, size),
            "path" when IsKey(s[2], MaxKeyChars) => new PathIconRequest(s[2], size),
            _ => null,
        };
    }

    private static bool IsKey(string s, int max) => s.Length is > 0 && s.Length <= max && !s.Any(c => c < 0x20 || c == 0x7f);

    /// <summary>Decimal digits only (no sign, no whitespace), positive, fits a long.</summary>
    private static bool TryHandle(string s, out long handle)
    {
        handle = 0;
        if (s.Length is 0 or > 19 || s.Any(c => c is < '0' or > '9')) return false;
        return long.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out handle) && handle > 0;
    }

    private static bool TryUInt(string s, out int value)
    {
        value = 0;
        return s.Length is > 0 and <= 10 && s.All(c => c is >= '0' and <= '9') && int.TryParse(s, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static long NumberOr(Dictionary<string, string> p, string key, long fallback)
    {
        if (!p.TryGetValue(key, out var v) || v.Length == 0) return fallback;
        var digits = v.StartsWith('-') ? v[1..] : v;
        if (digits.Length == 0 || digits.Any(c => c is < '0' or > '9')) return fallback;
        if (v.StartsWith('-')) return 0;
        return long.TryParse(v, NumberStyles.None, CultureInfo.InvariantCulture, out var n) ? n : long.MaxValue; // too many digits: "huge" -> the largest bucket
    }

    private static Dictionary<string, string> ParseQuery(string query)
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        if (query.Length == 0) return d;
        foreach (var pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            var eq = pair.IndexOf('=');
            var rawKey = eq >= 0 ? pair[..eq] : pair;
            var rawValue = eq >= 0 ? pair[(eq + 1)..] : "";
            if (!TryDecode(rawKey, out var key) || !TryDecode(rawValue, out var value)) continue;
            d.TryAdd(key, value); // the first occurrence wins
        }
        return d;
    }

    /// <summary>Percent-decoding exactly once; rejects control characters in the result (a decoded %00 or %0A is never legitimate).</summary>
    private static bool TryDecode(string s, out string decoded)
    {
        try { decoded = Uri.UnescapeDataString(s); }
        catch (UriFormatException) { decoded = ""; return false; }
        foreach (var c in decoded) if (c < 0x20 || c == 0x7f) return false;
        return true;
    }
}
