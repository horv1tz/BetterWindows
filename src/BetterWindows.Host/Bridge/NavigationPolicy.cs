using System.Text.RegularExpressions;
using BetterWindows.Core.Protocol;

namespace BetterWindows.Host.Bridge;

/// <summary>How the bridge must treat one WebView2 resource request (docs/IPC.md sections 1.1 and 16).</summary>
public enum RequestDisposition
{
    /// <summary>Not ours (data:, blob:, devtools: ...): leave it alone.</summary>
    Ignore,
    /// <summary>https://app.local/* (not /dyn/): answer from the UI bundle in wwwroot.</summary>
    AppStatic,
    /// <summary>/dyn/* of the app origin or of the dev-server origin: answer from IDynamicResourceProvider.</summary>
    Dynamic,
    /// <summary>Dev-server request that is not /dyn/*: let the network handle it (Vite assets, HMR).</summary>
    DevPassThrough,
    /// <summary>Anything else on http(s): answer 403 (the UI has no network access).</summary>
    Forbidden,
}

/// <summary>
/// The origin rules of the WebView bridge as a pure value: which URLs a surface may navigate to, which page may post messages, how a surface route becomes a URL and how
/// requests are classified. In production the only origin is <c>https://app.local</c>; in dev mode (<c>--ui-url</c>) it is the dev-server origin and app.local is not served.
/// </summary>
public sealed class NavigationPolicy
{
    /// <summary>Virtual host of the bundled UI.</summary>
    public const string AppHost = "app.local";
    /// <summary>Origin of the bundled UI.</summary>
    public const string AppOrigin = "https://app.local";
    /// <summary>Prefix of in-memory resources.</summary>
    public const string DynPrefix = DynUrls.Prefix;
    /// <summary>The page that is allowed to post messages.</summary>
    public const string IndexPath = "/index.html";

    /// <summary>
    /// CSP of index.html. docs/IPC.md section 16 mandates <c>default-src 'self'; img-src 'self' data: blob:; connect-src 'none'</c>; two directives are added on purpose:
    /// <c>style-src 'unsafe-inline'</c> (the Svelte bundle sets inline <c>style</c> attributes, which default-src 'self' would block and the UI would render unstyled) and
    /// <c>font-src 'self' data:</c>; plus object/base/form lockdown. Scripts stay 'self' only: no inline script, no eval.
    /// </summary>
    public const string Csp = "default-src 'self'; img-src 'self' data: blob:; style-src 'self' 'unsafe-inline'; font-src 'self' data:; connect-src 'none'; object-src 'none'; base-uri 'none'; form-action 'none'";

    private static readonly Regex SegmentRegex = new("^[A-Za-z0-9._-]{1,64}$", RegexOptions.Compiled | RegexOptions.CultureInvariant);
    private static readonly HashSet<string> Surfaces = new(StringComparer.Ordinal) { "dock", "menubar", "overlay", "desktop", "settings" };

    private readonly Uri _origin;

    /// <param name="devOrigin">Dev-server origin (scheme://host[:port]) or null for the bundled UI.</param>
    public NavigationPolicy(string? devOrigin = null)
    {
        if (devOrigin is null)
        {
            _origin = new Uri(AppOrigin);
        }
        else
        {
            if (!Uri.TryCreate(devOrigin, UriKind.Absolute, out var u) || (u.Scheme != Uri.UriSchemeHttp && u.Scheme != Uri.UriSchemeHttps))
                throw new ArgumentException("dev origin must be an absolute http(s) URL", nameof(devOrigin));
            _origin = new Uri(u.GetLeftPart(UriPartial.Authority));
        }
        IsDevMode = devOrigin is not null;
    }

    /// <summary>True when surfaces load from the dev server instead of the bundle.</summary>
    public bool IsDevMode { get; }
    /// <summary>The single origin pages may navigate to, e.g. <c>https://app.local</c> or <c>http://localhost:5173</c> (no trailing slash).</summary>
    public string Origin => _origin.GetLeftPart(UriPartial.Authority);

    /// <summary>
    /// <c>{origin}/index.html#/{surface}[/{page}]</c>. <paramref name="route"/> is "dock", "settings" or "settings/dock"; a monitor id is appended for per-monitor surfaces.
    /// Throws <see cref="ArgumentException"/> for unknown surfaces or segments with unexpected characters (the URL is built from host data, but still validated).
    /// </summary>
    public string BuildUrl(string route, string? monitorId = null)
    {
        if (string.IsNullOrEmpty(route)) throw new ArgumentException("route is empty", nameof(route));
        var parts = route.Trim('/').Split('/');
        if (parts.Length > 2 || !Surfaces.Contains(parts[0])) throw new ArgumentException($"unknown surface route '{route}'", nameof(route));
        foreach (var p in parts)
            if (!SegmentRegex.IsMatch(p)) throw new ArgumentException($"invalid route segment '{p}'", nameof(route));
        var path = string.Join('/', parts);
        if (monitorId is not null && parts.Length == 1 && parts[0] != "settings")
        {
            if (!SegmentRegex.IsMatch(monitorId)) throw new ArgumentException("invalid monitor id", nameof(monitorId));
            path += "/" + Uri.EscapeDataString(monitorId);
        }
        return $"{Origin}{IndexPath}#/{path}";
    }

    /// <summary>NavigationStarting / FrameNavigationStarting: only the allowed origin (any path). Everything else (file:, about:, other hosts, userinfo tricks) is cancelled.</summary>
    public bool IsAllowedNavigation(string? uri) => TryParse(uri, out var u) && SameOrigin(u);

    /// <summary>WebMessageReceived: the sender must be exactly the origin's index.html (query and fragment ignored).</summary>
    public bool IsAllowedMessageSource(string? source) =>
        TryParse(source, out var u) && SameOrigin(u) && string.Equals(u.AbsolutePath, IndexPath, StringComparison.Ordinal);

    /// <summary>Classifies a WebResourceRequested URL.</summary>
    public RequestDisposition Classify(string? uri)
    {
        if (!TryParse(uri, out var u))
            return Uri.TryCreate(uri, UriKind.Absolute, out var other) && IsNetworkScheme(other.Scheme) ? RequestDisposition.Forbidden : RequestDisposition.Ignore;
        if (!IsNetworkScheme(u.Scheme)) return RequestDisposition.Ignore;
        var isDyn = u.AbsolutePath.StartsWith(DynPrefix, StringComparison.Ordinal);
        if (SameOrigin(u)) return isDyn ? RequestDisposition.Dynamic : IsDevMode ? RequestDisposition.DevPassThrough : RequestDisposition.AppStatic;
        return RequestDisposition.Forbidden;
    }

    /// <summary>WebResourceRequested filter URIs to register (the catch-all 403 filter is separate).</summary>
    public IReadOnlyList<string> ResourceFilters() => IsDevMode ? new[] { Origin + DynPrefix + "*" } : new[] { Origin + "/*" };

    private bool SameOrigin(Uri u) =>
        string.Equals(u.Scheme, _origin.Scheme, StringComparison.Ordinal) &&
        string.Equals(u.Host, _origin.Host, StringComparison.OrdinalIgnoreCase) &&
        u.Port == _origin.Port && string.IsNullOrEmpty(u.UserInfo);

    private static bool IsNetworkScheme(string scheme) => scheme is "http" or "https" or "ws" or "wss" or "ftp";

    private static bool TryParse(string? s, out Uri uri)
    {
        uri = null!;
        if (string.IsNullOrEmpty(s) || s.Length > 8192) return false;
        return Uri.TryCreate(s, UriKind.Absolute, out uri!);
    }
}
