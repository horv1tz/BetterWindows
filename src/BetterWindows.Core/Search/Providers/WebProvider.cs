using System.Text.RegularExpressions;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;

namespace BetterWindows.Core.Search.Providers;

/// <summary>
/// Turns the query into web results without any I/O: an "Open" result when the query is a web address (http/https only, validated by
/// <see cref="InputPolicy.IsSafeWebUrl"/>), and always-last "Search the web for ..." using the settings template (the {query} placeholder is URL-encoded and
/// the resulting address is validated the same way). Runs immediately; it has no timeout of its own.
/// </summary>
public sealed partial class WebProvider : ISearchProvider
{
    public const string Placeholder = "{query}";

    private readonly Func<string> _template;

    /// <param name="template">Returns the current settings.spotlight.webSearchUrl (read per query, so changes apply at once).</param>
    public WebProvider(Func<string> template) => _template = template;

    public SearchProviderId Id => SearchProviderId.Web;
    public TimeSpan Timeout => TimeSpan.FromMilliseconds(200);

    [GeneratedRegex(@"^(?:[a-z0-9](?:[a-z0-9-]{0,61}[a-z0-9])?\.)+[a-z]{2,63}(?::\d{1,5})?(?:[/?#]\S*)?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex BareHostRegex();

    public Task<IReadOnlyList<SearchHit>> QueryAsync(SearchQuery query, CancellationToken ct)
    {
        var text = query.Text.Trim();
        var hits = new List<SearchHit>();
        if (text.Length == 0) return Task.FromResult<IReadOnlyList<SearchHit>>(hits);

        if (TryAddress(text, out var address))
        {
            hits.Add(new SearchHit(
                Id,
                Title: Loc.Pick(query.Locale, "Open ", "Открыть ") + InputPolicy.Truncate(DisplayAddress(address), 180),
                Subtitle: Loc.Pick(query.Locale, "Website", "Веб-сайт"),
                IconUrl: null,
                Score: 0.9,
                Actions: new[] { new HitAction("open", Loc.Pick(query.Locale, "Open", "Открыть"), true, new OpenUriOperation(address.AbsoluteUri)) }));
        }

        var template = _template();
        if (template.Contains(Placeholder, StringComparison.Ordinal))
        {
            var url = template.Replace(Placeholder, Uri.EscapeDataString(text), StringComparison.Ordinal);
            if (InputPolicy.IsSafeWebUrl(url, out var searchUri))
            {
                hits.Add(new SearchHit(
                    Id,
                    Title: Loc.Pick(query.Locale, "Search the web for ", "Искать в Интернете: ") + InputPolicy.Truncate(text, 160),
                    Subtitle: searchUri.Host,
                    IconUrl: null,
                    Score: 0.1,
                    Actions: new[] { new HitAction("search", Loc.Pick(query.Locale, "Search", "Искать"), true, new OpenUriOperation(searchUri.AbsoluteUri)) }));
            }
        }
        return Task.FromResult<IReadOnlyList<SearchHit>>(hits);
    }

    /// <summary>Host and path without the scheme; a bare root shows as the host only.</summary>
    internal static string DisplayAddress(Uri address) =>
        address.PathAndQuery == "/" ? address.Host : address.Host + address.PathAndQuery;

    /// <summary>A typed address: an explicit http(s) URL, or a bare host such as "example.com/docs" (which gets https://).</summary>
    internal static bool TryAddress(string text, out Uri address)
    {
        address = null!;
        if (text.Contains(' ') || text.Length > 2048) return false;
        if (text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
        {
            return InputPolicy.IsSafeWebUrl(text, out address);
        }
        if (!BareHostRegex().IsMatch(text)) return false;
        // "report.pdf" is a file name, not a host: reject bare names whose last label is a common file extension.
        var host = text.Split('/', '?', '#')[0];
        var tld = host[(host.LastIndexOf('.') + 1)..];
        if (FileExtensions.Contains(tld.ToLowerInvariant())) return false;
        return InputPolicy.IsSafeWebUrl("https://" + text, out address);
    }

    private static readonly HashSet<string> FileExtensions = new(StringComparer.Ordinal)
    {
        "txt", "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx", "csv", "json", "xml", "md", "exe", "msi", "bat", "ps1", "zip", "rar", "7z",
        "png", "jpg", "jpeg", "gif", "bmp", "mp3", "mp4", "mov", "wav", "cs", "py", "js", "ts", "css", "html", "log", "ini", "cfg", "lnk",
    };
}
