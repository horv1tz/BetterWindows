using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Host.Bridge;

namespace BetterWindows.Host.Tests.Bridge;

public class DynResponseTests
{
    [Theory]
    [InlineData("https://app.local/dyn/icon/app/aumid%3AMicrosoft.Edge?s=128", "/dyn/icon/app/aumid%3AMicrosoft.Edge?s=128")]
    [InlineData("https://app.local/dyn/thumb/197890?w=480&rev=3#frag", "/dyn/thumb/197890?w=480&rev=3")]
    [InlineData("http://localhost:5173/dyn/art/4", "/dyn/art/4")]
    public void Dyn_urls_are_split_exactly_as_the_page_built_them(string url, string expected)
    {
        DynResponse.TrySplit(url, out var pq).Should().BeTrue();
        pq.Should().Be(expected);
    }

    [Theory]
    [InlineData("https://app.local/index.html")]
    [InlineData("https://app.local/dynamic/x")]               // prefix must be exactly /dyn/
    [InlineData("https://app.local/dyn/x\\y")]                // backslash
    [InlineData("https://app.local/dyn/x\u0001")]             // control character
    [InlineData("")]
    [InlineData(null)]
    public void Non_dyn_or_hostile_urls_are_not_split(string? url)
    {
        DynResponse.TrySplit(url, out _).Should().BeFalse();
    }

    [Fact]
    public void Overlong_dyn_path_is_refused()
    {
        DynResponse.TrySplit("https://app.local/dyn/" + new string('a', 5000), out _).Should().BeFalse();
    }

    [Fact]
    public void Successful_image_carries_the_mandated_security_headers()
    {
        var r = DynResponse.Map(new DynResource(new byte[] { 1, 2, 3 }, "image/png", TimeSpan.FromDays(2)));
        r.StatusCode.Should().Be(200);
        r.Body.Should().Equal(1, 2, 3);
        r.Headers.Should().Contain("Content-Type: image/png");
        r.Headers.Should().Contain("Content-Length: 3");
        r.Headers.Should().Contain("X-Content-Type-Options: nosniff");
        r.Headers.Should().Contain("Content-Security-Policy: default-src 'none'; sandbox");
        r.Headers.Should().Contain("Cross-Origin-Resource-Policy: same-origin");
        r.Headers.Should().NotContain("Access-Control-Allow-Origin");
        r.Headers.Should().Contain("Cache-Control: private, max-age=172800, immutable");
    }

    [Theory]
    [InlineData("image/svg+xml")]
    [InlineData("text/html")]
    [InlineData("application/javascript")]
    [InlineData("")]
    [InlineData(null)]
    public void Only_raster_images_are_served(string? type)
    {
        DynResponse.Map(new DynResource(new byte[] { 1 }, type!)).StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData("image/PNG; charset=binary", "image/png")]
    [InlineData("  image/jpeg  ", "image/jpeg")]
    [InlineData("IMAGE/WEBP", "image/webp")]
    public void Content_type_parameters_and_case_are_normalised(string type, string expected)
    {
        var r = DynResponse.Map(new DynResource(new byte[] { 1 }, type));
        r.StatusCode.Should().Be(200);
        r.Headers.Should().Contain("Content-Type: " + expected);
    }

    [Fact]
    public void Null_resource_and_empty_body_are_404_never_cached()
    {
        var missing = DynResponse.Map(null);
        missing.StatusCode.Should().Be(404);
        missing.Headers.Should().Contain("Cache-Control: no-store");
        missing.Body.Should().BeEmpty();
        DynResponse.Map(new DynResource(Array.Empty<byte>(), "image/png")).StatusCode.Should().Be(404);
    }

    [Theory]
    [InlineData(-1, "no-store")]                  // -1 stands for "no lifetime declared" (null)
    [InlineData(0, "no-store")]
    [InlineData(3600, "private, max-age=3600")]
    [InlineData(172800, "private, max-age=172800, immutable")]
    [InlineData(10_000_000_000, "private, max-age=31536000, immutable")]
    public void Cache_policy_follows_the_declared_lifetime(long seconds, string expected)
    {
        DynResponse.CacheControlFor(seconds < 0 ? null : TimeSpan.FromSeconds(seconds)).Should().Be(expected);
    }

    [Fact]
    public void Url_builders_round_trip_through_the_splitter()
    {
        var url = DynUrls.Absolute(DynUrls.AppIcon("aumid:Microsoft.Edge", 64));
        url.Should().StartWith(DynUrls.Origin);
        DynResponse.TrySplit(url, out var pq).Should().BeTrue();
        pq.Should().Be(DynUrls.AppIcon("aumid:Microsoft.Edge", 64));
        pq.Should().Be("/dyn/icon/app/aumid%3AMicrosoft.Edge?s=64");
        DynUrls.WindowThumb(42, 480, 2).Should().Be("/dyn/thumb/42?w=480&rev=2");
        DynUrls.WindowIcon(7).Should().Be("/dyn/icon/window/7?s=128");
        DynUrls.Wallpaper("m1234abcd").Should().Be("/dyn/wallpaper/m1234abcd?rev=0");
        DynUrls.TrayIcon("tray id").Should().Be("/dyn/tray/tray%20id?rev=0");
    }
}

public class NavigationPolicyTests
{
    [Fact]
    public void Production_urls_use_the_app_origin_and_the_hash_route()
    {
        var p = new NavigationPolicy();
        p.IsDevMode.Should().BeFalse();
        p.BuildUrl("dock").Should().Be("https://app.local/index.html#/dock");
        p.BuildUrl("dock", "m1a2b3c4d").Should().Be("https://app.local/index.html#/dock/m1a2b3c4d");
        p.BuildUrl("settings/dock", "m1a2b3c4d").Should().Be("https://app.local/index.html#/settings/dock");
        p.BuildUrl("settings").Should().Be("https://app.local/index.html#/settings");
    }

    [Theory]
    [InlineData("")]
    [InlineData("bogus")]
    [InlineData("dock/extra/more")]
    [InlineData("dock/../x")]
    public void Unknown_or_hostile_routes_are_rejected(string route)
    {
        Assert.Throws<ArgumentException>(() => new NavigationPolicy().BuildUrl(route));
    }

    [Theory]
    [InlineData("../evil")]
    [InlineData("m 1")]
    public void Invalid_monitor_ids_are_rejected(string monitor)
    {
        Assert.Throws<ArgumentException>(() => new NavigationPolicy().BuildUrl("dock", monitor));
    }

    [Theory]
    [InlineData("https://app.local/index.html#/dock", true)]
    [InlineData("https://app.local/any/path", true)]
    [InlineData("https://app.local.evil.test/", false)]
    [InlineData("https://evil.test/app.local", false)]
    [InlineData("http://app.local/index.html", false)]
    [InlineData("https://user@app.local/index.html", false)]
    [InlineData("https://app.local:8443/index.html", false)]
    [InlineData("file:///C:/index.html", false)]
    [InlineData("about:blank", false)]
    [InlineData("data:text/html,<script>", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Only_the_app_origin_may_be_navigated_to(string? url, bool allowed)
    {
        new NavigationPolicy().IsAllowedNavigation(url).Should().Be(allowed);
    }

    [Theory]
    [InlineData("https://app.local/index.html", true)]
    [InlineData("https://app.local/index.html?x=1#/dock", true)]
    [InlineData("https://app.local/other.html", false)]
    [InlineData("https://app.local/", false)]
    [InlineData("https://app.local/assets/app.js", false)]
    [InlineData("https://evil.test/index.html", false)]
    public void Only_the_bundle_index_may_post_messages(string url, bool allowed)
    {
        new NavigationPolicy().IsAllowedMessageSource(url).Should().Be(allowed);
    }

    [Theory]
    [InlineData("https://app.local/dyn/icon/app/x", RequestDisposition.Dynamic)]
    [InlineData("https://app.local/index.html", RequestDisposition.AppStatic)]
    [InlineData("https://app.local/assets/a.js", RequestDisposition.AppStatic)]
    [InlineData("https://example.test/ad.js", RequestDisposition.Forbidden)]
    [InlineData("http://example.test/", RequestDisposition.Forbidden)]
    [InlineData("wss://example.test/socket", RequestDisposition.Forbidden)]
    [InlineData("https://app.local.evil.test/x", RequestDisposition.Forbidden)]
    [InlineData("data:image/png;base64,AAAA", RequestDisposition.Ignore)]
    [InlineData("blob:https://app.local/abc", RequestDisposition.Ignore)]
    [InlineData("file:///C:/secret", RequestDisposition.Ignore)]
    [InlineData("devtools://devtools/bundled", RequestDisposition.Ignore)]
    [InlineData("not a uri at all", RequestDisposition.Ignore)]
    [InlineData("", RequestDisposition.Ignore)]
    public void Requests_are_classified_for_the_production_origin(string url, RequestDisposition expected)
    {
        new NavigationPolicy().Classify(url).Should().Be(expected);
    }

    [Fact]
    public void Production_filters_cover_only_the_app_origin()
    {
        new NavigationPolicy().ResourceFilters().Should().Equal("https://app.local/*");
    }

    [Fact]
    public void Dev_mode_passes_the_dev_server_through_except_dyn_and_blocks_everything_else()
    {
        var p = new NavigationPolicy("http://localhost:5173/");
        p.IsDevMode.Should().BeTrue();
        p.Origin.Should().Be("http://localhost:5173");
        p.BuildUrl("overlay").Should().Be("http://localhost:5173/index.html#/overlay");
        p.Classify("http://localhost:5173/dyn/thumb/1?w=320").Should().Be(RequestDisposition.Dynamic);
        p.Classify("http://localhost:5173/src/main.ts").Should().Be(RequestDisposition.DevPassThrough);
        p.Classify("https://app.local/index.html").Should().Be(RequestDisposition.Forbidden);
        p.ResourceFilters().Should().Equal("http://localhost:5173/dyn/*");
        p.IsAllowedNavigation("http://localhost:5173/index.html").Should().BeTrue();
        p.IsAllowedNavigation("http://localhost:6000/index.html").Should().BeFalse();
    }

    [Theory]
    [InlineData("ftp://localhost")]
    [InlineData("localhost:5173")]
    [InlineData("not a url")]
    public void Dev_origin_must_be_an_absolute_http_url(string origin)
    {
        Assert.Throws<ArgumentException>(() => new NavigationPolicy(origin));
    }

    [Fact]
    public void Csp_forbids_network_and_inline_scripts()
    {
        NavigationPolicy.Csp.Should().Contain("connect-src 'none'");
        NavigationPolicy.Csp.Should().Contain("default-src 'self'");
        NavigationPolicy.Csp.Should().NotContain("script-src 'unsafe-inline'");
        NavigationPolicy.Csp.Should().NotContain("unsafe-eval");
    }
}

public class ErrorPageTests
{
    [Fact]
    public void Error_page_encodes_the_detail_and_runs_no_script()
    {
        var html = WebViewErrorPage.Build(SurfaceFailureKind.ReadyTimeout, "<script>alert(1)</script>");
        html.Should().Contain("ReadyTimeout");
        html.Should().Contain("&lt;script&gt;");
        html.Should().NotContain("<script>");
        html.Should().NotContain("<script");
    }

    [Fact]
    public void Error_detail_is_clipped()
    {
        var html = WebViewErrorPage.Build(SurfaceFailureKind.NavigationFailed, new string('x', 2000));
        html.Length.Should().BeLessThan(2000);
    }

    [Theory]
    [InlineData("data:text/html;charset=utf-8,%3Chtml", true)]
    [InlineData("DATA:TEXT/HTML,x", true)]
    [InlineData("data:text/plain,x", false)]
    [InlineData("https://app.local/index.html", false)]
    [InlineData(null, false)]
    public void Only_the_html_error_document_is_recognised(string? uri, bool expected)
    {
        WebViewErrorPage.IsErrorDocument(uri).Should().Be(expected);
    }
}
