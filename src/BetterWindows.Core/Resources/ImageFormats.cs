namespace BetterWindows.Core.Resources;

/// <summary>
/// The content-type allowlist of <c>/dyn/*</c> (docs/IPC.md section 8): only png, jpeg, webp, gif and bmp are ever served, decided from the BYTES (magic numbers), never from a
/// declared type or a file extension, and never svg/html (media apps control cover-art bytes). Pure functions.
/// </summary>
public static class ImageFormats
{
    public const string Png = "image/png", Jpeg = "image/jpeg", Webp = "image/webp", Gif = "image/gif", Bmp = "image/bmp";

    /// <summary>The allowlisted content type for these bytes, or null if they are not (the start of) an allowed image.</summary>
    public static string? Sniff(ReadOnlySpan<byte> d)
    {
        if (d.Length >= 4 && d[0] == 0x89 && d[1] == 0x50 && d[2] == 0x4E && d[3] == 0x47) return Png;
        if (d.Length >= 3 && d[0] == 0xFF && d[1] == 0xD8 && d[2] == 0xFF) return Jpeg;
        if (d.Length >= 4 && d[0] == (byte)'G' && d[1] == (byte)'I' && d[2] == (byte)'F' && d[3] == (byte)'8') return Gif;
        if (d.Length >= 12 && d[0] == (byte)'R' && d[1] == (byte)'I' && d[2] == (byte)'F' && d[3] == (byte)'F' && d[8] == (byte)'W' && d[9] == (byte)'E' && d[10] == (byte)'B' && d[11] == (byte)'P') return Webp;
        if (d.Length >= 2 && d[0] == (byte)'B' && d[1] == (byte)'M') return Bmp;
        return null;
    }

    /// <summary>Extensions the wallpaper endpoint accepts (matched against the magic bytes too).</summary>
    public static string? ContentTypeForExtension(string path) => Path.GetExtension(path).ToLowerInvariant() switch
    {
        ".png" => Png,
        ".jpg" or ".jpeg" => Jpeg,
        ".bmp" => Bmp,
        ".webp" => Webp,
        ".gif" => Gif,
        _ => null,
    };

    /// <summary>Image files whose shell "icon" is a thumbnail of their content (IIconSource returns that for them at large sizes).</summary>
    public static bool IsImageFileName(string path) => ContentTypeForExtension(path) is not null;

    /// <summary>Round UP to the next bucket; above the last bucket the last one.</summary>
    public static int Bucket(long requested, IReadOnlyList<int> buckets)
    {
        foreach (var b in buckets) if (requested <= b) return b;
        return buckets[^1];
    }

    public static readonly IReadOnlyList<int> IconSizes = new[] { 32, 64, 128, 256, 512 };
    public static readonly IReadOnlyList<int> ThumbWidths = new[] { 160, 240, 320, 480, 640, 960 };
}
