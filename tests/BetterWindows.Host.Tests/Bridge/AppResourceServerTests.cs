using BetterWindows.Host.Bridge;
using BetterWindows.Host.Tests.Support;

namespace BetterWindows.Host.Tests.Bridge;

public class AppResourceNormalizeTests
{
    [Theory]
    [InlineData("/", "index.html")]
    [InlineData("/index.html", "index.html")]
    [InlineData("/assets/app-AbCdEf12.js", "assets/app-AbCdEf12.js")]
    [InlineData("/assets/my%20file.css", "assets/my file.css")]
    [InlineData("/fonts/Inter%E2%80%93Var.woff2", "fonts/Inter–Var.woff2")]
    public void Plain_relative_paths_are_accepted(string url, string expected)
    {
        AppResourceServer.TryNormalize(url, out var rel).Should().BeTrue();
        rel.Should().Be(expected);
    }

    [Theory]
    [InlineData("/../secret.txt")]
    [InlineData("/a/../../secret.txt")]
    [InlineData("/./index.html")]
    [InlineData("/%2e%2e/secret.txt")]              // one decode -> "../secret.txt"
    [InlineData("/%2E%2E%2Fsecret.txt")]            // encoded slash after decode
    [InlineData("/%252e%252e/secret.txt")]          // double encoding leaves a '%'
    [InlineData("/assets/%25")]                     // a surviving '%'
    [InlineData("/a\\b.js")]                        // backslash
    [InlineData("/%5Cwindows%5Cwin.ini")]          // encoded backslash
    [InlineData("/C:/Windows/win.ini")]             // drive letter
    [InlineData("/index.html:stream")]              // alternate data stream
    [InlineData("/a*.js")]
    [InlineData("/a?.js")]
    [InlineData("/a%00.js")]                        // NUL
    [InlineData("/a%0Ab.js")]                       // control character
    [InlineData("/assets//app.js")]                 // empty segment
    [InlineData("/assets/app.js/")]                 // trailing slash
    [InlineData("/index.html.")]                    // Windows strips the dot: would alias index.html
    [InlineData("/index.html ")]                    // trailing space alias
    [InlineData("/CON")]                            // reserved device names
    [InlineData("/nul.txt")]
    [InlineData("/com1.js")]
    [InlineData("/.env")]                           // dot-files
    [InlineData("/.git/config")]
    [InlineData("index.html")]                      // no leading slash
    [InlineData("")]
    [InlineData(null)]
    public void Traversal_device_and_alias_tricks_are_rejected(string? url)
    {
        AppResourceServer.TryNormalize(url, out _).Should().BeFalse();
    }

    [Fact]
    public void Overlong_path_is_rejected()
    {
        AppResourceServer.TryNormalize("/" + new string('a', 600), out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("assets/app-AbCdEf12.js", true)]
    [InlineData("assets/app-AbCdEf12.js.map", false)]
    [InlineData("assets/app.js", false)]          // no content hash
    [InlineData("index-AbCdEf12.html", false)]    // not under assets/
    public void Only_content_hashed_assets_are_immutable(string rel, bool hashed)
    {
        AppResourceServer.IsHashedAsset(rel).Should().Be(hashed);
    }

    [Theory]
    [InlineData("a.html", "text/html; charset=utf-8")]
    [InlineData("A.JS", "text/javascript; charset=utf-8")]
    [InlineData("b.svg", "image/svg+xml")]
    [InlineData("c.woff2", "font/woff2")]
    [InlineData("d.exe", null)]
    [InlineData("e.dll", null)]
    [InlineData("f", null)]
    public void Content_types_come_from_a_fixed_allow_list(string name, string? type)
    {
        AppResourceServer.ContentTypeFor(name).Should().Be(type);
    }
}

public class AppResourceServerFileTests
{
    [Fact]
    public async Task Serves_index_with_no_cache_and_the_csp_and_marks_it_as_index()
    {
        using var dir = new TempDir();
        dir.File("index.html", "<!doctype html>");
        var server = new AppResourceServer(dir.Path);
        var res = await server.TryGetAsync("/");
        res.Should().NotBeNull();
        res!.IsIndex.Should().BeTrue();
        res.CacheControl.Should().Be(AppResourceServer.NoCache);
        res.ContentType.Should().Be("text/html; charset=utf-8");
        AppHttpResponse.StaticHeaders(res).Should().Contain("Content-Security-Policy: default-src 'self'");
        server.HasIndex().Should().BeTrue();
    }

    [Fact]
    public async Task Hashed_assets_are_immutable_and_other_files_are_not_csp_carrying()
    {
        using var dir = new TempDir();
        dir.File("index.html", "x");
        dir.File("assets/app-AbCdEf12.js", "x");
        dir.File("assets/plain.css", "x");
        var server = new AppResourceServer(dir.Path);
        (await server.TryGetAsync("/assets/app-AbCdEf12.js"))!.CacheControl.Should().Be(AppResourceServer.Immutable);
        var css = await server.TryGetAsync("/assets/plain.css");
        css!.CacheControl.Should().Be(AppResourceServer.NoCache);
        AppHttpResponse.StaticHeaders(css).Should().NotContain("Content-Security-Policy");
        AppHttpResponse.StaticHeaders(css).Should().Contain("X-Content-Type-Options: nosniff");
    }

    [Fact]
    public async Task Traversal_outside_the_root_is_404_even_when_the_file_exists()
    {
        using var root = new TempDir();
        using var outside = new TempDir();
        outside.File("secret.txt", "classified");
        var rootDir = Path.Combine(root.Path, "bundle");
        Directory.CreateDirectory(rootDir);
        File.WriteAllText(Path.Combine(rootDir, "index.html"), "x");
        var server = new AppResourceServer(rootDir);

        (await server.TryGetAsync("/../secret.txt")).Should().BeNull();
        (await server.TryGetAsync("/%2e%2e/secret.txt")).Should().BeNull();
        (await server.TryGetAsync("/..%5Csecret.txt")).Should().BeNull();
        (await server.TryGetAsync("/%252e%252e/secret.txt")).Should().BeNull();
    }

    [Fact]
    public async Task Unknown_extension_missing_file_and_bad_path_are_null()
    {
        using var dir = new TempDir();
        dir.File("tool.exe", "MZ");
        var server = new AppResourceServer(dir.Path);
        (await server.TryGetAsync("/tool.exe")).Should().BeNull();
        (await server.TryGetAsync("/missing.js")).Should().BeNull();
        (await server.TryGetAsync("/a/../b.js")).Should().BeNull();
        (await server.TryGetAsync(null)).Should().BeNull();
    }

    [Fact]
    public async Task A_symbolic_link_to_a_file_outside_the_root_is_refused()
    {
        using var root = new TempDir();
        using var outside = new TempDir();
        var secret = outside.File("secret.js", "x");
        var rootDir = Path.Combine(root.Path, "bundle");
        Directory.CreateDirectory(rootDir);
        File.WriteAllText(Path.Combine(rootDir, "index.html"), "x");
        var link = Path.Combine(rootDir, "linked.js");
        try { File.CreateSymbolicLink(link, secret); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or PlatformNotSupportedException) { return; } // no symlink rights here
        var server = new AppResourceServer(rootDir);
        (await server.TryGetAsync("/linked.js")).Should().BeNull();
    }

    [Fact]
    public async Task Root_is_normalised_and_a_missing_bundle_has_no_index()
    {
        using var dir = new TempDir();
        var server = new AppResourceServer(Path.Combine(dir.Path, "nope"));
        server.HasIndex().Should().BeFalse();
        (await server.TryGetAsync("/")).Should().BeNull();
        server.Root.Should().EndWith("nope");
    }
}
