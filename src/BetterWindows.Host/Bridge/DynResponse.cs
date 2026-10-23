using System.Globalization;
using System.Text;
using BetterWindows.Core.Services;

namespace BetterWindows.Host.Bridge;

/// <summary>The HTTP answer for one <c>/dyn/*</c> request, ready for <c>CoreWebView2Environment.CreateWebResourceResponse</c>.</summary>
/// <param name="StatusCode">200 or 404.</param>
/// <param name="ReasonPhrase">"OK" / "Not Found".</param>
/// <param name="Headers">Header block ("Name: value" lines separated by CRLF).</param>
/// <param name="Body">Response bytes (empty for 404).</param>
public sealed record DynHttpResponse(int StatusCode, string ReasonPhrase, string Headers, byte[] Body);

/// <summary>
/// Pure mapping logic for <c>/dyn/*</c> (docs/IPC.md section 8): URL splitting for the provider and provider result to HTTP response with the mandated security headers.
/// Defence in depth: even though the providers already re-encode images, only raster image types are ever served and the body size is capped.
/// </summary>
public static class DynResponse
{
    /// <summary>Largest body served (providers cap thumbnails/art at far less).</summary>
    public const int MaxBodyBytes = 32 * 1024 * 1024;
    private const int MaxPathAndQueryChars = 4096;

    /// <summary>Content types that may be served from <c>/dyn/*</c>. Never svg or html: media apps control cover-art bytes.</summary>
    public static readonly IReadOnlySet<string> AllowedContentTypes = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
    {
        "image/png", "image/jpeg", "image/webp", "image/gif", "image/bmp",
    };

    /// <summary>
    /// Extracts <c>/dyn/...?query</c> from an absolute request URL exactly as the page built it (percent-escapes untouched, fragment dropped), the form
    /// <see cref="IDynamicResourceProvider.GetAsync"/> expects. False if the URL is not a <c>/dyn/</c> URL or looks hostile (control characters, backslash, too long).
    /// </summary>
    public static bool TrySplit(string? absoluteUrl, out string pathAndQuery)
    {
        pathAndQuery = "";
        if (string.IsNullOrEmpty(absoluteUrl) || absoluteUrl.Length > MaxPathAndQueryChars + 256) return false;
        var schemeEnd = absoluteUrl.IndexOf("://", StringComparison.Ordinal);
        if (schemeEnd <= 0) return false;
        var pathStart = absoluteUrl.IndexOf('/', schemeEnd + 3);
        if (pathStart < 0) return false;
        var end = absoluteUrl.IndexOf('#', pathStart);
        var candidate = end < 0 ? absoluteUrl[pathStart..] : absoluteUrl[pathStart..end];
        if (candidate.Length > MaxPathAndQueryChars || !candidate.StartsWith("/dyn/", StringComparison.Ordinal)) return false;
        foreach (var c in candidate)
            if (c < 0x20 || c == 0x7f || c == '\\') return false;
        pathAndQuery = candidate;
        return true;
    }

    /// <summary>Maps a provider result (null = unknown / failed resource) to the HTTP response.</summary>
    public static DynHttpResponse Map(DynResource? resource)
    {
        if (resource is null || resource.Data.Length == 0 || resource.Data.Length > MaxBodyBytes) return NotFound();
        var type = NormalizeContentType(resource.ContentType);
        if (type is null || !AllowedContentTypes.Contains(type)) return NotFound();

        var headers = new StringBuilder(256);
        headers.Append("Content-Type: ").Append(type).Append("\r\n");
        headers.Append("Content-Length: ").Append(resource.Data.Length.ToString(CultureInfo.InvariantCulture)).Append("\r\n");
        headers.Append("Cache-Control: ").Append(CacheControlFor(resource.MaxAge)).Append("\r\n");
        AppendSecurityHeaders(headers);
        return new DynHttpResponse(200, "OK", headers.ToString(), resource.Data);
    }

    /// <summary>404 for unknown or failed resources (the UI shows a fallback tile). Never cached.</summary>
    public static DynHttpResponse NotFound()
    {
        var headers = new StringBuilder(192);
        headers.Append("Content-Type: text/plain; charset=utf-8\r\n");
        headers.Append("Content-Length: 0\r\n");
        headers.Append("Cache-Control: no-store\r\n");
        AppendSecurityHeaders(headers);
        return new DynHttpResponse(404, "Not Found", headers.ToString(), Array.Empty<byte>());
    }

    /// <summary><c>no-store</c> unless the provider declared a lifetime (content-hashed icon URLs): then <c>private, max-age=N</c>, plus <c>immutable</c> from one day up.</summary>
    public static string CacheControlFor(TimeSpan? maxAge)
    {
        if (maxAge is not { } age || age <= TimeSpan.Zero) return "no-store";
        var seconds = (long)Math.Min(age.TotalSeconds, 31_536_000);
        return age >= TimeSpan.FromDays(1) ? $"private, max-age={seconds}, immutable" : $"private, max-age={seconds}";
    }

    private static void AppendSecurityHeaders(StringBuilder h)
    {
        h.Append("X-Content-Type-Options: nosniff\r\n");
        h.Append("Content-Security-Policy: default-src 'none'; sandbox\r\n");
        h.Append("Cross-Origin-Resource-Policy: same-origin"); // no CORS headers at all: the page only embeds these as images
    }

    /// <summary>"image/PNG; charset=x" -> "image/png"; null for empty values.</summary>
    internal static string? NormalizeContentType(string? contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return null;
        var semi = contentType.IndexOf(';');
        var t = (semi >= 0 ? contentType[..semi] : contentType).Trim().ToLowerInvariant();
        return t.Length == 0 ? null : t;
    }
}
