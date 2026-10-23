using System.IO;

namespace BetterWindows.Host.Bridge;

/// <summary>A static file of the UI bundle ready to be served.</summary>
/// <param name="Data">File bytes.</param>
/// <param name="ContentType">MIME type (with charset for text).</param>
/// <param name="CacheControl">Cache-Control value: <c>no-cache</c> for index.html and unhashed files, immutable for hashed assets.</param>
/// <param name="IsIndex">True for index.html: the bridge adds the CSP header.</param>
public sealed record AppResource(byte[] Data, string ContentType, string CacheControl, bool IsIndex);

/// <summary>
/// Serves the bundled UI (<c>wwwroot</c> next to the exe) with strict path handling (docs/IPC.md section 1.1): the URL path is percent-decoded exactly once and then
/// rejected if it contains anything but plain relative segments (no <c>..</c>, no backslash, no drive letter / alternate stream colon, no control characters, no dot-files,
/// no reserved device names, no leftover '%'); the resolved file must lie inside the root and must not be reached through a link. Only known file types are served.
/// </summary>
public sealed class AppResourceServer
{
    /// <summary>Largest file served (source maps are the biggest legitimate files).</summary>
    public const long MaxFileBytes = 32L * 1024 * 1024;
    public const string NoCache = "no-cache";
    public const string Immutable = "public, max-age=31536000, immutable";

    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".html"] = "text/html; charset=utf-8",
        [".js"] = "text/javascript; charset=utf-8",
        [".mjs"] = "text/javascript; charset=utf-8",
        [".css"] = "text/css; charset=utf-8",
        [".json"] = "application/json; charset=utf-8",
        [".map"] = "application/json; charset=utf-8",
        [".svg"] = "image/svg+xml",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".webp"] = "image/webp",
        [".gif"] = "image/gif",
        [".avif"] = "image/avif",
        [".ico"] = "image/x-icon",
        [".woff2"] = "font/woff2",
        [".woff"] = "font/woff",
        [".ttf"] = "font/ttf",
        [".txt"] = "text/plain; charset=utf-8",
    };

    private static readonly HashSet<string> ReservedNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "CON", "PRN", "AUX", "NUL", "COM1", "COM2", "COM3", "COM4", "COM5", "COM6", "COM7", "COM8", "COM9", "LPT1", "LPT2", "LPT3", "LPT4", "LPT5", "LPT6", "LPT7", "LPT8", "LPT9",
    };

    private readonly string _root;
    private readonly string _rootWithSeparator;
    private readonly StringComparison _pathComparison;

    public AppResourceServer(string wwwrootDirectory)
    {
        _root = Path.GetFullPath(wwwrootDirectory).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        _rootWithSeparator = _root + Path.DirectorySeparatorChar;
        _pathComparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
    }

    /// <summary>The root directory being served.</summary>
    public string Root => _root;

    /// <summary>True if the bundle has an index.html (the smoke test and --diagnose report this).</summary>
    public bool HasIndex() => File.Exists(Path.Combine(_root, "index.html"));

    /// <summary>Content type for a file name, null for unknown/unsafe extensions (those are 404).</summary>
    public static string? ContentTypeFor(string fileName) => ContentTypes.TryGetValue(Path.GetExtension(fileName), out var t) ? t : null;

    /// <summary>
    /// URL path (the part after the host, query already removed, still percent-encoded) to a forward-slash relative file path. "/" maps to index.html.
    /// Returns false for anything that is not a plain relative path inside the bundle.
    /// </summary>
    public static bool TryNormalize(string? urlPath, out string relativePath)
    {
        relativePath = "";
        if (string.IsNullOrEmpty(urlPath) || urlPath.Length > 512 || urlPath[0] != '/') return false;
        if (urlPath == "/") { relativePath = "index.html"; return true; }
        if (HasForbiddenChar(urlPath)) return false;

        string decoded;
        try { decoded = Uri.UnescapeDataString(urlPath); }
        catch (UriFormatException) { return false; }
        // Decode exactly once: a '%' that survives is a double-encoding attempt (or a file name we never ship).
        if (decoded.Contains('%') || HasForbiddenChar(decoded)) return false;

        var segments = decoded.Split('/');
        var kept = new List<string>(segments.Length);
        for (var i = 1; i < segments.Length; i++) // segments[0] is the empty string before the leading '/'
        {
            var seg = segments[i];
            if (seg.Length == 0) return false;                                                   // "//" or a trailing '/'
            if (seg[0] == '.') return false;                                                     // "." ".." and dot-files
            if (seg[^1] == '.' || seg[^1] == ' ') return false;                                  // Windows strips these: "index.html." == "index.html"
            var dot = seg.IndexOf('.');
            if (ReservedNames.Contains(dot >= 0 ? seg[..dot] : seg)) return false;
            kept.Add(seg);
        }
        if (kept.Count == 0) return false;
        relativePath = string.Join('/', kept);
        return true;
    }

    private static bool HasForbiddenChar(string s)
    {
        foreach (var c in s)
        {
            if (c < 0x20 || c == 0x7f) return true;
            if (c is '\\' or ':' or '*' or '?' or '<' or '>' or '|' or '"') return true;
        }
        return false;
    }

    /// <summary>Loads the file for a URL path; null = 404 (bad path, unknown type, missing file, link, outside the root, too large).</summary>
    public async Task<AppResource?> TryGetAsync(string? urlPath, CancellationToken ct = default)
    {
        if (!TryNormalize(urlPath, out var relative)) return null;
        var contentType = ContentTypeFor(relative);
        if (contentType is null) return null;

        var full = Path.GetFullPath(Path.Combine(_root, relative.Replace('/', Path.DirectorySeparatorChar)));
        if (!full.StartsWith(_rootWithSeparator, _pathComparison)) return null;
        try
        {
            var info = new FileInfo(full);
            if (!info.Exists || info.Length > MaxFileBytes || IsLink(info, full)) return null;
            var data = await File.ReadAllBytesAsync(full, ct).ConfigureAwait(false);
            var isIndex = string.Equals(relative, "index.html", StringComparison.OrdinalIgnoreCase);
            return new AppResource(data, contentType, isIndex || !IsHashedAsset(relative) ? NoCache : Immutable, isIndex);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or OperationCanceledException) { return null; }
    }

    /// <summary>Vite emits <c>assets/name-HASH.ext</c>: the hash makes the URL content-addressed, so it may be cached forever.</summary>
    internal static bool IsHashedAsset(string relative)
    {
        if (!relative.StartsWith("assets/", StringComparison.Ordinal)) return false;
        var name = Path.GetFileNameWithoutExtension(relative);
        var dash = name.LastIndexOf('-');
        return dash > 0 && name.Length - dash - 1 >= 8 && !relative.EndsWith(".map", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A reparse point (symlink/junction) on the file or on any directory between it and the root: refuse (a link could point outside the bundle).</summary>
    private bool IsLink(FileInfo file, string full)
    {
        if (file.LinkTarget is not null || file.Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
        var dir = Path.GetDirectoryName(full);
        while (!string.IsNullOrEmpty(dir) && dir.Length > _root.Length)
        {
            if (new DirectoryInfo(dir).Attributes.HasFlag(FileAttributes.ReparsePoint)) return true;
            dir = Path.GetDirectoryName(dir);
        }
        return false;
    }
}
