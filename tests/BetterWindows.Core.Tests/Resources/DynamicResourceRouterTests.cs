using System.Text;
using System.Text.Json.Nodes;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Resources;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BetterWindows.Core.Tests.Resources;

public class DynamicResourceRouterTests : IDisposable
{
    private readonly List<RouterRig> _rigs = new();

    public void Dispose() { foreach (var r in _rigs) r.Dispose(); }

    private RouterRig Rig(Action<IServiceCollection>? configure = null, bool overlayVisible = true)
    {
        var r = new RouterRig(configure, overlayVisible);
        _rigs.Add(r);
        return r;
    }

    private static byte[] Bytes(params byte[] b) => b;

    private static async Task<DynResource?> Get(RouterRig rig, string url) => await rig.Get(url);

    private static readonly byte[] Svg = Encoding.ASCII.GetBytes("<svg xmlns='http://www.w3.org/2000/svg'><script>alert(1)</script></svg>");

    private static async Task Scan(RouterRig rig, FakeAppCatalogSource source, params AppEntry[] apps)
    {
        source.Apps.AddRange(apps);
        await rig.H.Get<IAppCatalog>().RefreshAsync();
    }

    // ---- URL handling ----------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData("/dyn/nope/x")]
    [InlineData("https://app.local/dyn/icon/app/x")]
    [InlineData("/dyn/icon/app/")]
    [InlineData("/dyn/icon/window/0?s=64")]
    [InlineData("/dyn/thumb/abc?w=480")]
    [InlineData("/dyn/icon/path/..%2F..%2Fwindows")]
    [InlineData("/dyn/wallpaper/bad%00id")]
    public async Task Unknown_and_malformed_urls_are_not_found(string url)
    {
        using var rig = Rig();
        Assert.Null(await Get(rig, url));
    }

    // ---- application icons ----------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_catalog_icon_is_an_allowlisted_png_cached_per_size()
    {
        using var rig = Rig();
        await Scan(rig, rig.H.Get<FakeAppCatalogSource>(), FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        var url = DynUrls.AppIcon("exe:c:\\windows\\mspaint.exe", 100);

        var first = await Get(rig, url);
        var second = await Get(rig, url);
        Assert.NotNull(first);
        Assert.Equal(ImageFormats.Png, first!.ContentType);
        Assert.Equal(second!.Data, first.Data);
        Assert.Single(rig.Icons.Requests);
        Assert.Equal(128, rig.Icons.Requests[0].Size); // 100 rounds up to the 128 bucket
    }

    [Fact]
    public async Task Icons_with_a_rev_are_immutable_and_without_one_are_short_lived()
    {
        using var rig = Rig();
        await Scan(rig, rig.H.Get<FakeAppCatalogSource>(), FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        var id = "exe:c:\\windows\\mspaint.exe";

        var short_ = await Get(rig, DynUrls.AppIcon(id, 64));
        Assert.Equal(TimeSpan.FromSeconds(60), short_!.MaxAge);

        var immutable = await Get(rig, DynUrls.AppIcon(id, 64) + "&rev=ab12");
        Assert.Equal(TimeSpan.FromDays(365), immutable!.MaxAge);
    }

    [Fact]
    public async Task An_unknown_app_is_not_found_and_never_asks_the_icon_port()
    {
        using var rig = Rig();
        Assert.Null(await Get(rig, DynUrls.AppIcon("aumid:Nobody.Here!App")));
        Assert.Null(await Get(rig, DynUrls.AppIcon("exe:c:\\nowhere\\x.exe")));
        Assert.Empty(rig.Icons.Requests);
    }

    [Fact]
    public async Task A_running_app_that_is_not_installed_uses_its_window_for_the_icon()
    {
        using var rig = Rig();
        rig.Tracker.Add(5, appId: "exe:c:\\run\\r.exe", name: "Runner", exe: @"C:\run\r.exe");
        var r = await Get(rig, DynUrls.AppIcon("exe:c:\\run\\r.exe", 64));
        Assert.NotNull(r);
        Assert.Equal(@"C:\run\r.exe", ((IconKey)rig.Icons.Requests[0].Key).Path, ignoreCase: true);
    }

    [Fact]
    public async Task Icons_that_are_not_allowlisted_images_are_dropped()
    {
        using var rig = Rig();
        rig.Icons.Png = Svg;
        await Scan(rig, rig.H.Get<FakeAppCatalogSource>(), FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        Assert.Null(await Get(rig, DynUrls.AppIcon("exe:c:\\windows\\mspaint.exe", 64)));
    }

    [Fact]
    public async Task Oversized_icons_are_dropped()
    {
        using var rig = Rig();
        rig.Icons.Png = Bytes(0x89, 0x50, 0x4E, 0x47).Concat(new byte[4 * 1024 * 1024]).ToArray();
        await Scan(rig, rig.H.Get<FakeAppCatalogSource>(), FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));
        Assert.Null(await Get(rig, DynUrls.AppIcon("exe:c:\\windows\\mspaint.exe", 64)));
    }

    [Fact]
    public async Task Icon_generation_gives_up_after_three_seconds_of_clock_time()
    {
        using var rig = Rig(sc => sc.AddSingleton<IIconSource>(new HangingIcons()));
        await Scan(rig, rig.H.Get<FakeAppCatalogSource>(), FakeAppCatalogSource.App("Paint", path: @"C:\Windows\mspaint.exe"));

        var pending = Get(rig, DynUrls.AppIcon("exe:c:\\windows\\mspaint.exe", 64));
        rig.H.Time.Advance(TimeSpan.FromSeconds(4));
        Assert.Null(await pending);
    }

    private sealed class HangingIcons : IIconSource
    {
        public async Task<byte[]?> GetIconPngAsync(IconKey key, int size, CancellationToken ct = default)
        {
            await Task.Delay(Timeout.Infinite, ct);
            return null;
        }
    }

    // ---- window icons ---------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Window_icons_need_a_tracked_window_and_are_never_cached()
    {
        using var rig = Rig();
        rig.Tracker.Add(9, name: "Editor");

        Assert.NotNull(await Get(rig, DynUrls.WindowIcon(9, 32)));
        Assert.NotNull(await Get(rig, DynUrls.WindowIcon(9, 32)));
        Assert.Equal(2, rig.Icons.Requests.Count);
        Assert.Null((await Get(rig, DynUrls.WindowIcon(9, 32)))?.MaxAge);

        Assert.Null(await Get(rig, DynUrls.WindowIcon(10, 32)));
        Assert.Equal(3, rig.Icons.Requests.Count);
    }

    // ---- path icons -----------------------------------------------------------------------------------------------------------------------------------

    [Theory]
    [InlineData(@"relative\file.txt")]
    [InlineData(@"\\server\share\file.txt")]
    [InlineData(@"\\?\C:\long\file.txt")]
    [InlineData(@"C:\a\..\..\..\etc")]
    [InlineData(@"C:\dir\CON")]
    public async Task Path_icons_refuse_relative_unc_device_and_traversal_paths(string path)
    {
        using var rig = Rig();
        Assert.Null(await Get(rig, DynUrls.PathIcon(path, 64)));
        Assert.Empty(rig.Icons.Requests);
    }

    [Fact]
    public async Task A_missing_path_is_not_found_without_asking_the_shell()
    {
        using var rig = Rig();
        Assert.Null(await Get(rig, DynUrls.PathIcon(@"C:\" + RouterRig.Unique(".txt"), 64)));
        Assert.Empty(rig.Icons.Requests);
    }

    [Fact]
    public async Task An_existing_file_gets_a_shell_icon()
    {
        using var rig = Rig();
        var file = rig.MakeFile(RouterRig.Unique(".txt"), Encoding.ASCII.GetBytes("hello"));
        var r = await Get(rig, DynUrls.PathIcon(file, 48) + "&rev=1");
        Assert.NotNull(r);
        Assert.Equal(ImageFormats.Png, r!.ContentType);
        Assert.Equal(64, rig.Icons.Requests.Single().Size);
        Assert.Equal(file, ((IconKey)rig.Icons.Requests.Single().Key).Path, ignoreCase: true);
    }

    // ---- thumbnails -----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task A_tracked_window_thumbnail_is_a_png_served_from_the_cache_with_no_store()
    {
        using var rig = Rig();
        rig.Tracker.Add(20);
        rig.Capture.Result = (_, _) => RouterRig.Png("snap");

        var a = await Get(rig, DynUrls.WindowThumb(20, 300));
        var b = await Get(rig, DynUrls.WindowThumb(20, 300));
        Assert.Equal(ImageFormats.Png, a!.ContentType);
        Assert.Null(a.MaxAge);
        Assert.Equal(a.Data, b!.Data);
        Assert.Single(rig.Capture.Requests);
        Assert.Equal(320, rig.Capture.Requests[0].Width); // 300 rounds up to the 320 bucket
    }

    [Fact]
    public async Task Thumbnails_of_untracked_windows_are_not_found()
    {
        using var rig = Rig();
        Assert.Null(await Get(rig, DynUrls.WindowThumb(404, 480)));
        Assert.Empty(rig.Capture.Requests);
    }

    [Fact]
    public async Task Thumbnails_stop_being_served_once_the_overlay_has_been_hidden_for_longer_than_the_grace_period()
    {
        using var rig = Rig();
        rig.Tracker.Add(21);
        rig.HideOverlay();
        rig.H.Time.Advance(TimeSpan.FromSeconds(1));
        Assert.NotNull(await Get(rig, DynUrls.WindowThumb(21, 480)));

        rig.H.Time.Advance(TimeSpan.FromSeconds(10));
        Assert.Null(await Get(rig, DynUrls.WindowThumb(21, 480, rev: 2)));
    }

    [Fact]
    public async Task Thumbnails_are_not_served_while_the_overlay_was_never_shown()
    {
        using var rig = Rig(overlayVisible: false);
        rig.Tracker.Add(22);
        Assert.Null(await Get(rig, DynUrls.WindowThumb(22, 480)));
        Assert.Empty(rig.Capture.Requests);
    }

    [Fact]
    public async Task Identical_concurrent_thumbnail_requests_share_one_capture()
    {
        using var rig = Rig();
        rig.Tracker.Add(23);
        rig.Capture.Gate = new TaskCompletionSource();
        var requests = Enumerable.Range(0, 5).Select(_ => Get(rig, DynUrls.WindowThumb(23, 480))).ToArray();
        await WaitUntil(() => rig.Capture.Calls >= 1);
        rig.Capture.Gate.SetResult();
        var results = await Task.WhenAll(requests);
        Assert.All(results, r => Assert.NotNull(r));
        Assert.Equal(1, rig.Capture.Calls);
    }

    [Fact]
    public async Task At_most_three_captures_run_at_once()
    {
        using var rig = Rig();
        for (long h = 30; h < 36; h++) rig.Tracker.Add(h);
        rig.Capture.Gate = new TaskCompletionSource();
        var requests = Enumerable.Range(30, 6).Select(h => Get(rig, DynUrls.WindowThumb(h, 480))).ToArray();
        await WaitUntil(() => rig.Capture.Calls >= 3);
        await Task.Delay(20);
        Assert.Equal(3, rig.Capture.Calls);
        rig.Capture.Gate.SetResult();
        await Task.WhenAll(requests);
        Assert.Equal(6, rig.Capture.Calls);
        Assert.True(rig.Capture.MaxConcurrent <= 3, $"max concurrency {rig.Capture.MaxConcurrent}");
    }

    [Fact]
    public async Task When_a_capture_fails_the_last_good_image_of_that_window_is_served()
    {
        using var rig = Rig();
        rig.Tracker.Add(40);
        var good = RouterRig.Png("good");
        rig.Capture.Result = (_, _) => good;
        var first = await Get(rig, DynUrls.WindowThumb(40, 480, rev: 1));

        rig.Capture.Result = (_, _) => null;
        var fallback = await Get(rig, DynUrls.WindowThumb(40, 480, rev: 2));
        Assert.NotNull(first);
        Assert.Equal(first!.Data, fallback!.Data);
    }

    [Fact]
    public async Task Prewarming_a_window_feeds_the_last_good_image_and_ignores_unknown_handles()
    {
        using var rig = Rig();
        rig.Tracker.Add(41);
        rig.Capture.Result = (_, _) => RouterRig.Png("warm");
        await rig.Concrete.PrewarmWindowThumbnailAsync(41);
        Assert.Single(rig.Capture.Requests);

        rig.Capture.Result = (_, _) => null;
        var served = await Get(rig, DynUrls.WindowThumb(41, 480, rev: 3));
        Assert.NotNull(served);

        await rig.Concrete.PrewarmWindowThumbnailAsync(999);
        Assert.Equal(2, rig.Capture.Requests.Count); // the prewarm and the one cache miss; an unknown handle captures nothing
    }

    // ---- cover art ------------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Cover_art_is_re_sniffed_and_the_declared_type_is_ignored()
    {
        using var rig = Rig();
        rig.H.Get<FakeMediaSource>().Art = (RouterRig.Jpeg("cover"), "image/svg+xml");
        var r = await Get(rig, DynUrls.MediaArt(1));
        Assert.Equal(ImageFormats.Jpeg, r!.ContentType);
        Assert.Null(r.MaxAge);
    }

    [Fact]
    public async Task Cover_art_that_is_not_a_png_or_jpeg_is_refused()
    {
        using var rig = Rig();
        rig.H.Get<FakeMediaSource>().Art = (Svg, "image/png");
        Assert.Null(await Get(rig, DynUrls.MediaArt(2)));
    }

    [Fact]
    public async Task Cover_art_over_two_megabytes_is_refused_and_missing_art_is_not_found()
    {
        using var rig = Rig();
        Assert.Null(await Get(rig, DynUrls.MediaArt(3)));
        rig.H.Get<FakeMediaSource>().Art = (new byte[] { 0x89, 0x50, 0x4E, 0x47 }.Concat(new byte[2 * 1024 * 1024]).ToArray(), "image/png");
        Assert.Null(await Get(rig, DynUrls.MediaArt(4)));
    }

    // ---- wallpapers -----------------------------------------------------------------------------------------------------------------------------------

    private static async Task SetWallpaper(RouterRig rig, string monitorId, string path)
    {
        var patch = new JsonObject { ["desktop"] = new JsonObject { ["wallpapers"] = new JsonObject { [monitorId] = path } } };
        await rig.H.Get<ISettingsService>().PatchAsync(patch);
    }

    [Fact]
    public async Task A_wallpaper_from_settings_is_served_when_its_bytes_match_its_extension()
    {
        using var rig = Rig();
        var file = rig.MakeFile(RouterRig.Unique(".png"), RouterRig.Png("wall"));
        await SetWallpaper(rig, "m1", file);

        var r = await Get(rig, DynUrls.Wallpaper("m1", 1));
        Assert.NotNull(r);
        Assert.Equal(ImageFormats.Png, r!.ContentType);
    }

    [Fact]
    public async Task Wallpapers_for_unknown_monitors_are_not_found()
    {
        using var rig = Rig();
        var file = rig.MakeFile(RouterRig.Unique(".png"), RouterRig.Png("wall"));
        await SetWallpaper(rig, "m1", file);
        Assert.Null(await Get(rig, DynUrls.Wallpaper("m7")));
    }

    [Fact]
    public async Task A_settings_wallpaper_must_have_an_image_extension()
    {
        using var rig = Rig();
        var file = rig.MakeFile(RouterRig.Unique(".txt"), RouterRig.Png("wall"));
        await SetWallpaper(rig, "m1", file);
        Assert.Null(await Get(rig, DynUrls.Wallpaper("m1")));
    }

    [Fact]
    public async Task A_wallpaper_whose_bytes_do_not_match_its_extension_is_refused()
    {
        using var rig = Rig();
        var file = rig.MakeFile(RouterRig.Unique(".jpg"), RouterRig.Png("really a png"));
        await SetWallpaper(rig, "m1", file);
        Assert.Null(await Get(rig, DynUrls.Wallpaper("m1")));

        var text = rig.MakeFile(RouterRig.Unique(".png"), Encoding.ASCII.GetBytes("<html>not an image</html>"));
        await SetWallpaper(rig, "m1", text);
        Assert.Null(await Get(rig, DynUrls.Wallpaper("m1")));
    }

    [Fact]
    public async Task A_network_wallpaper_path_is_never_read()
    {
        using var rig = Rig();
        await SetWallpaper(rig, "m1", @"\\attacker\share\wall.png");
        Assert.Null(await Get(rig, DynUrls.Wallpaper("m1")));
    }

    [Fact]
    public async Task The_os_wallpaper_may_be_extensionless_but_must_still_be_a_real_image()
    {
        using var rig = Rig();
        var jpeg = rig.MakeFile(RouterRig.Unique(""), RouterRig.Jpeg("transcoded"));
        rig.H.Get<FakeWallpaperService>().Paths["m1"] = jpeg;
        var r = await Get(rig, DynUrls.Wallpaper("m1"));
        Assert.Equal(ImageFormats.Jpeg, r!.ContentType);

        var bogus = rig.MakeFile(RouterRig.Unique(""), Encoding.ASCII.GetBytes("nope"));
        rig.H.Get<FakeWallpaperService>().Paths["m1"] = bogus;
        Assert.Null(await Get(rig, DynUrls.Wallpaper("m1")));
    }

    // ---- tray icons -----------------------------------------------------------------------------------------------------------------------------------

    [Fact]
    public async Task Tray_icons_need_a_supported_tray_and_an_icon_the_host_reported()
    {
        using var rig = Rig();
        Assert.Null(await Get(rig, DynUrls.TrayIcon("wifi")));

        var tray = rig.H.Get<FakeTrayHost>();
        tray.IsSupported = true;
        Assert.Null(await Get(rig, DynUrls.TrayIcon("wifi")));
        Assert.Empty(tray.Icons);
    }

    private static async Task WaitUntil(Func<bool> condition, int timeoutMs = 5000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (!condition())
        {
            if (DateTime.UtcNow > deadline) throw new Xunit.Sdk.XunitException("condition not reached in time");
            await Task.Delay(2);
        }
    }
}
