using System.Net;

namespace BetterWindows.Host.Bridge;

/// <summary>The static error document a failed surface shows (docs/IPC.md section 16). No script, no network, both languages.</summary>
public static class WebViewErrorPage
{
    /// <summary>HTML for <c>NavigateToString</c>. <paramref name="detail"/> is HTML-encoded.</summary>
    public static string Build(SurfaceFailureKind kind, string detail) =>
        "<!doctype html><html><head><meta charset=\"utf-8\"><title>BetterWindows</title>" +
        "<style>html,body{margin:0;height:100%;background:#1c1c1e;color:#f2f2f7;font:13px 'Segoe UI',sans-serif;display:flex;align-items:center;justify-content:center}" +
        "div{max-width:460px;padding:12px;text-align:center}h3{margin:0 0 8px;font-weight:600}p{margin:4px 0}code{display:block;margin-top:8px;opacity:.6;word-break:break-word}</style></head>" +
        "<body><div><h3>BetterWindows</h3><p>This panel could not be loaded.</p><p>Эту панель не удалось загрузить.</p>" +
        "<code>" + WebUtility.HtmlEncode(kind.ToString()) + (string.IsNullOrEmpty(detail) ? "" : ": " + WebUtility.HtmlEncode(Clip(detail, 300))) + "</code></div></body></html>";

    /// <summary>True for the data: document NavigateToString produces (the only non-origin navigation ever allowed, and only while armed).</summary>
    public static bool IsErrorDocument(string? uri) => uri is not null && uri.StartsWith("data:text/html", StringComparison.OrdinalIgnoreCase);

    private static string Clip(string s, int max) => s.Length <= max ? s : s[..max];
}
