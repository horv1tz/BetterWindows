using System.Text;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Support;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using static BetterWindows.Core.Tests.Search.SearchTestKit;

namespace BetterWindows.Core.Tests.Search;

public class SearchPreviewerTests : IDisposable
{
    private readonly string _dir = TempDir();
    private readonly SearchPreviewer _previewer = new();


    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private SearchHit FileHit(string path) => new(SearchProviderId.Files, "f", null, null, 0.5, Array.Empty<HitAction>(), PreviewPath: path, PreviewIsApp: false);

    private Task<SearchPreview> Preview(SearchHit hit) => _previewer.PreviewAsync(hit, CancellationToken.None);

    [Fact]
    public async Task A_small_utf8_text_file_gives_a_text_preview()
    {
        if (!OperatingSystem.IsWindows()) return; // real files need a drive-letter path, which only exists on Windows
        var path = Path.Combine(_dir, "note.txt");
        File.WriteAllText(path, "Hello there\nsecond line", new UTF8Encoding(false));
        var p = await Preview(FileHit(path));
        Assert.Equal(SearchPreviewKind.Text, p.Kind);
        Assert.Contains("Hello there", p.Text);
        Assert.Contains("second line", p.Text);
    }

    [Fact]
    public async Task Utf16_files_with_a_byte_order_mark_are_text()
    {
        if (!OperatingSystem.IsWindows()) return; // real files need a drive-letter path, which only exists on Windows
        var path = Path.Combine(_dir, "wide.txt");
        File.WriteAllBytes(path, new byte[] { 0xFF, 0xFE }.Concat(Encoding.Unicode.GetBytes("Привет")).ToArray());
        var p = await Preview(FileHit(path));
        Assert.Equal("Привет", p.Text);
    }

    [Fact]
    public async Task Binary_files_with_nul_bytes_give_no_preview()
    {
        if (!OperatingSystem.IsWindows()) return; // real files need a drive-letter path, which only exists on Windows
        var path = Path.Combine(_dir, "data.bin");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x00, 0x47, 0x0D });
        Assert.Equal(SearchPreviewKind.None, (await Preview(FileHit(path))).Kind);
    }

    [Fact]
    public async Task Only_the_first_4_kb_of_a_text_file_are_read()
    {
        if (!OperatingSystem.IsWindows()) return; // real files need a drive-letter path, which only exists on Windows
        var path = Path.Combine(_dir, "long.txt");
        File.WriteAllText(path, new string('a', 10_000));
        var p = await Preview(FileHit(path));
        Assert.Equal(SearchPreviewer.MaxTextBytes, p.Text!.Length);
    }

    [Fact]
    public async Task Files_over_two_megabytes_give_no_preview()
    {
        if (!OperatingSystem.IsWindows()) return; // real files need a drive-letter path, which only exists on Windows
        var path = Path.Combine(_dir, "big.txt");
        using (var fs = File.Create(path)) fs.SetLength(SearchPreviewer.MaxFileBytes + 1);
        Assert.Equal(SearchPreviewKind.None, (await Preview(FileHit(path))).Kind);
    }

    [Fact]
    public async Task Empty_files_missing_files_and_folders_give_no_preview()
    {
        if (!OperatingSystem.IsWindows()) return; // real files need a drive-letter path, which only exists on Windows
        var empty = Path.Combine(_dir, "empty.txt");
        File.WriteAllText(empty, "");
        Assert.Equal(SearchPreviewKind.None, (await Preview(FileHit(empty))).Kind);
        Assert.Equal(SearchPreviewKind.None, (await Preview(FileHit(Path.Combine(_dir, "missing.txt")))).Kind);
        Assert.Equal(SearchPreviewKind.None, (await Preview(FileHit(_dir))).Kind);
    }

    [Fact]
    public async Task Images_get_a_thumbnail_url_at_256_pixels()
    {
        if (!OperatingSystem.IsWindows()) return; // real files need a drive-letter path, which only exists on Windows
        var path = Path.Combine(_dir, "photo.png");
        File.WriteAllBytes(path, new byte[] { 0x89, 0x50, 0x4E, 0x47 });
        var p = await Preview(FileHit(path));
        Assert.Equal(SearchPreviewKind.Image, p.Kind);
        Assert.Equal(DynUrls.PathIcon(path, 256), p.ImageUrl);
    }

    [Fact]
    public async Task Apps_preview_their_full_path_as_text()
    {
        var hit = new SearchHit(SearchProviderId.Apps, "Paint", null, null, 0.9, Array.Empty<HitAction>(), PreviewPath: @"C:\Windows\mspaint.exe", PreviewIsApp: true);
        var p = await Preview(hit);
        Assert.Equal(SearchPreviewKind.Text, p.Kind);
        Assert.Equal(@"C:\Windows\mspaint.exe", p.Text);
    }

    [Fact]
    public async Task Hits_without_a_path_and_unsafe_paths_give_no_preview()
    {
        var none = new SearchHit(SearchProviderId.Web, "w", null, null, 0.1, Array.Empty<HitAction>());
        Assert.Equal(SearchPreviewKind.None, (await Preview(none)).Kind);
        Assert.Equal(SearchPreviewKind.None, (await Preview(FileHit(@"\\server\share\x.txt"))).Kind);
    }

    [Fact]
    public async Task A_cheap_inline_preview_on_the_hit_is_returned_as_is()
    {
        var inline = new SearchPreview { Kind = SearchPreviewKind.Text, Text = "= 4" };
        var hit = new SearchHit(SearchProviderId.Calc, "= 4", null, null, 1, Array.Empty<HitAction>(), Preview: inline);
        Assert.Same(inline, await Preview(hit));
    }

    [Fact]
    public void Decoding_recognises_text_and_rejects_binary()
    {
        Assert.Equal("abc", SearchPreviewer.DecodeIfText(Encoding.UTF8.GetBytes("abc")));
        Assert.Equal("abc", SearchPreviewer.DecodeIfText(new byte[] { 0xEF, 0xBB, 0xBF }.Concat(Encoding.UTF8.GetBytes("abc")).ToArray()));
        Assert.Null(SearchPreviewer.DecodeIfText(new byte[] { 0x41, 0x00, 0x42 }));
        Assert.Equal("", SearchPreviewer.DecodeIfText(Array.Empty<byte>()));
    }

    [Fact]
    public void Control_characters_are_replaced_by_spaces_but_line_breaks_survive_decoding()
    {
        var text = SearchPreviewer.DecodeIfText(Encoding.UTF8.GetBytes("a\u0001b\nc"));
        Assert.Equal("a b\nc", text);
    }
}

public class UsageStoreTests : IDisposable
{
    private readonly string _dir = TempDir();
    private readonly ManualTimeProvider _time = new();

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch (IOException) { }
    }

    private string Path_ => Path.Combine(_dir, "data", "search-usage.json");

    private UsageStore Store() => new(Path_, _time, NullLogger.Instance);

    [Fact]
    public void Launches_are_counted_and_persisted_across_instances()
    {
        var store = Store();
        store.RecordLaunch("aumid:Notes");
        store.RecordLaunch("aumid:Notes");
        Assert.Equal(2, store.Get("aumid:Notes")!.Count);

        var reopened = Store();
        Assert.Equal(2, reopened.Get("aumid:Notes")!.Count);
        Assert.True(File.Exists(Path_));
    }

    [Fact]
    public void Recent_app_ids_are_ordered_by_last_launch()
    {
        var store = Store();
        store.RecordLaunch("exe:a");
        _time.Advance(TimeSpan.FromMinutes(1));
        store.RecordLaunch("exe:b");
        _time.Advance(TimeSpan.FromMinutes(1));
        store.RecordLaunch("exe:a");
        Assert.Equal(new[] { "exe:a", "exe:b" }, store.RecentAppIds(8));
        Assert.Equal(new[] { "exe:a" }, store.RecentAppIds(1));
        Assert.Empty(store.RecentAppIds(0));
    }

    [Fact]
    public void Boost_grows_with_the_launch_count_and_is_capped()
    {
        var store = Store();
        Assert.Equal(0, store.Boost("exe:never"));
        store.RecordLaunch("exe:a");
        var once = store.Boost("exe:a");
        for (int i = 0; i < 50; i++) store.RecordLaunch("exe:a");
        var many = store.Boost("exe:a");
        Assert.True(many > once);
        Assert.True(many <= 0.16 + 1e-9);
    }

    [Fact]
    public void Recent_use_adds_a_boost_that_expires_after_a_week()
    {
        var store = Store();
        store.RecordLaunch("exe:a");
        var fresh = store.Boost("exe:a");
        _time.Advance(TimeSpan.FromDays(8));
        var stale = store.Boost("exe:a");
        Assert.True(fresh > stale);
    }

    [Fact]
    public void A_corrupt_file_is_ignored_and_the_store_starts_empty()
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path_)!);
        File.WriteAllText(Path_, "{ not json");
        var store = Store();
        Assert.Null(store.Get("exe:a"));
        store.RecordLaunch("exe:a");
        Assert.Equal(1, Store().Get("exe:a")!.Count);
    }

    [Fact]
    public void Entries_are_pruned_to_the_maximum()
    {
        var store = Store();
        for (int i = 0; i <= UsageStore.MaxEntries; i++) store.RecordLaunch("exe:app" + i);
        Assert.Equal(UsageStore.MaxEntries, Store().RecentAppIds(10_000).Count);
    }

    [Fact]
    public void Empty_or_oversized_ids_are_ignored()
    {
        var store = Store();
        store.RecordLaunch("");
        store.RecordLaunch(new string('x', 5000));
        Assert.Empty(store.RecentAppIds(10));
    }
}

public class SearchRecentsTests
{
    [Fact]
    public void Recent_launches_come_first_then_pinned_apps_without_duplicates()
    {
        var time = new ManualTimeProvider();
        var usage = new UsageStore(Path.Combine(TempDir(), "u.json"), time, NullLogger.Instance);
        var catalog = new FakeCatalog();
        catalog.Items.Add(FakeCatalog.Exe("Paint", @"C:\p\mspaint.exe"));
        catalog.Items.Add(FakeCatalog.Exe("Notes", @"C:\n\notes.exe"));
        catalog.Items.Add(FakeCatalog.Exe("Mail", @"C:\m\mail.exe"));
        usage.RecordLaunch("exe:c:\\n\\notes.exe");

        var pins = new[]
        {
            new PinnedItem { Id = "exe:c:\\n\\notes.exe", Kind = PinnedKind.App, Target = @"C:\n\notes.exe" },
            new PinnedItem { Id = "exe:c:\\m\\mail.exe", Kind = PinnedKind.App, Target = @"C:\m\mail.exe" },
            new PinnedItem { Id = "folder:x", Kind = PinnedKind.Folder, Target = @"C:\x" },
            new PinnedItem { Id = "exe:missing", Kind = PinnedKind.App, Target = @"C:\gone.exe" },
        };
        var hits = SearchRecents.Build(usage, catalog, pins, "en");
        Assert.Equal(new[] { "Notes", "Mail" }, hits.Select(h => h.Title));
        Assert.All(hits, h => Assert.Equal(SearchProviderId.Apps, h.Provider));
    }

    [Fact]
    public void The_list_is_limited_to_eight()
    {
        var time = new ManualTimeProvider();
        var usage = new UsageStore(Path.Combine(TempDir(), "u.json"), time, NullLogger.Instance);
        var catalog = new FakeCatalog();
        for (int i = 0; i < 12; i++)
        {
            catalog.Items.Add(FakeCatalog.Exe("App " + i, $@"C:\a\app{i}.exe"));
            usage.RecordLaunch($"exe:c:\\a\\app{i}.exe");
        }
        Assert.Equal(SearchRecents.DefaultLimit, SearchRecents.Build(usage, catalog, Array.Empty<PinnedItem>(), "en").Count);
    }
}
