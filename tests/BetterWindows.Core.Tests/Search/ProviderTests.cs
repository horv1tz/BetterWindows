using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search;
using BetterWindows.Core.Search.Calculator;
using BetterWindows.Core.Search.Providers;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Resources;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BetterWindows.Core.Tests.Search;

public class NumberTextTests
{
    [Theory]
    [InlineData(1234.5, "en", "1,234.5")]
    [InlineData(1234.5, "ru", "1\u00A0234,5")]
    [InlineData(0.1 + 0.2, "en", "0.3")]
    [InlineData(10.0 / 3, "en", "3.3333333333")]
    [InlineData(10.0 / 3, "ru", "3,3333333333")]
    [InlineData(-0.0, "en", "0")]
    [InlineData(-1234, "en", "-1,234")]
    [InlineData(1e-11, "en", "0")]
    [InlineData(1e15, "en", "1E+15")]
    [InlineData(1.5e20, "ru", "1,5E+20")]
    [InlineData(2, "xx", "2")]
    [InlineData(0.5, "en", "0.5")]
    [InlineData(1000000, "ru", "1\u00A0000\u00A0000")]
    [InlineData(42, "en", "42")]
    public void Formats_numbers_for_the_locale(double value, string locale, string expected)
    {
        Assert.Equal(expected, NumberText.Format(value, locale));
    }

    [Fact]
    public void Non_finite_values_format_as_empty_text()
    {
        Assert.Equal("", NumberText.Format(double.NaN, "en"));
        Assert.Equal("", NumberText.Format(double.PositiveInfinity, "en"));
    }
}

public class CalculatorProviderTests
{
    private static async Task<IReadOnlyList<SearchHit>> Query(string text, string locale = "en") =>
        await new CalculatorProvider().QueryAsync(new SearchQuery(text, locale), CancellationToken.None);

    [Fact]
    public async Task A_complete_expression_yields_one_result_titled_with_the_value()
    {
        var hits = await Query("2+2");
        var hit = Assert.Single(hits);
        Assert.Equal("= 4", hit.Title);
        Assert.Equal(SearchProviderId.Calc, hit.Provider);
        Assert.Equal(1.0, hit.Score);
        Assert.Equal("2+2", hit.Subtitle);
    }

    [Fact]
    public async Task The_primary_action_copies_the_value()
    {
        var hit = Assert.Single(await Query("1,5 * 4"));
        var action = Assert.Single(hit.Actions);
        Assert.True(action.Primary);
        var copy = Assert.IsType<CopyTextOperation>(action.Operation);
        Assert.Equal("6", copy.Text);
    }

    [Fact]
    public async Task Russian_locale_formats_the_title_and_the_copied_text()
    {
        var hit = Assert.Single(await Query("1000000 + 0,5", "ru"));
        Assert.Equal("= 1\u00A0000\u00A0000,5", hit.Title);
        Assert.Equal("1\u00A0000\u00A0000,5", Assert.IsType<CopyTextOperation>(hit.Actions[0].Operation).Text);
    }

    [Fact]
    public async Task Unit_conversions_show_the_unit_symbol()
    {
        var hit = Assert.Single(await Query("72 f to c"));
        Assert.StartsWith("= 22.2222222222", hit.Title);
        Assert.EndsWith(" °C", hit.Title);
    }

    [Fact]
    public async Task Bare_numbers_and_invalid_input_yield_nothing()
    {
        Assert.Empty(await Query("2024"));
        Assert.Empty(await Query("hello"));
        Assert.Empty(await Query("2+"));
        Assert.Empty(await Query("1/0"));
    }

    [Fact]
    public async Task The_preview_is_cheap_text_showing_expression_and_result()
    {
        var hit = Assert.Single(await Query("3*3"));
        Assert.Equal(SearchPreviewKind.Text, hit.Preview!.Kind);
        Assert.Equal("3*3 = 9", hit.Preview.Text);
    }
}

public class AppsProviderTests
{
    private static FakeCatalog Build()
    {
        var c = new FakeCatalog();
        c.Items.Add(FakeCatalog.Aumid("Notes", "Contoso.Notes!App"));
        c.Items.Add(FakeCatalog.Exe("Microsoft Edge", @"C:\Program Files\Edge\msedge.exe"));
        c.Items.Add(FakeCatalog.Exe("Paint", @"C:\Windows\System32\mspaint.exe"));
        return c;
    }

    private static UsageStore Usage() => new(Path.Combine(SearchTestKit.TempDir(), "usage.json"), TimeProvider.System, NullLogger.Instance);

    private static async Task<IReadOnlyList<SearchHit>> Query(string text, FakeCatalog? catalog = null, UsageStore? usage = null) =>
        await new AppsProvider(catalog ?? Build(), usage ?? Usage()).QueryAsync(new SearchQuery(text, "en"), CancellationToken.None);

    [Fact]
    public async Task Matches_the_display_name()
    {
        var hits = await Query("notes");
        Assert.Equal("Notes", Assert.Single(hits).Title);
    }

    [Fact]
    public async Task Matches_the_executable_name_as_an_alias()
    {
        var hits = await Query("msedge");
        Assert.Equal("Microsoft Edge", hits[0].Title);
    }

    [Fact]
    public async Task Opening_is_the_primary_action_by_catalog_id_and_explorer_is_secondary()
    {
        var hit = (await Query("paint"))[0];
        Assert.Equal(new LaunchAppOperation("exe:c:\\windows\\system32\\mspaint.exe"), hit.Actions[0].Operation);
        Assert.True(hit.Actions[0].Primary);
        Assert.Equal("reveal", hit.Actions[1].Id);
        Assert.False(hit.Actions[1].Primary);
        Assert.IsType<RevealPathOperation>(hit.Actions[1].Operation);
    }

    [Fact]
    public async Task Nothing_matches_gibberish()
    {
        Assert.Empty(await Query("qqqxxx"));
    }

    [Fact]
    public async Task Launch_history_breaks_ties_between_similar_names()
    {
        var c = new FakeCatalog();
        c.Items.Add(FakeCatalog.Exe("Calc A", @"C:\a\calc.exe"));
        c.Items.Add(FakeCatalog.Exe("Calc B", @"C:\b\calc.exe"));
        var usage = Usage();
        usage.RecordLaunch("exe:c:\\b\\calc.exe");
        var hits = await Query("calc", c, usage);
        Assert.Equal("Calc B", hits[0].Title);
    }

    [Fact]
    public async Task Results_are_capped()
    {
        var c = new FakeCatalog();
        for (int i = 0; i < 20; i++) c.Items.Add(FakeCatalog.Exe("Tool " + i, $@"C:\t\tool{i}.exe"));
        Assert.Equal(AppsProvider.MaxResults, (await Query("tool", c)).Count);
    }
}

public class SettingsPagesProviderTests
{
    private static async Task<IReadOnlyList<SearchHit>> Query(string text, string locale = "en") =>
        await new SettingsPagesProvider().QueryAsync(new SearchQuery(text, locale), CancellationToken.None);

    [Fact]
    public void The_table_has_about_forty_five_valid_unique_settings_uris()
    {
        var pages = SettingsPagesProvider.Pages;
        Assert.InRange(pages.Count, 40, 60);
        Assert.All(pages, p => Assert.True(InputPolicy.IsMsSettingsUri(p.Uri), p.Uri));
        Assert.Equal(pages.Count, pages.Select(p => p.Uri).Distinct().Count());
        Assert.All(pages, p => Assert.False(string.IsNullOrWhiteSpace(p.Ru)));
    }

    [Fact]
    public async Task Wifi_finds_the_network_page_and_opens_its_uri()
    {
        var hit = (await Query("wifi"))[0];
        Assert.Equal("Wi-Fi", hit.Title);
        Assert.Equal(new OpenUriOperation("ms-settings:network-wifi"), hit.Actions[0].Operation);
    }

    [Fact]
    public async Task Keywords_find_pages_by_what_they_are_for()
    {
        Assert.Equal("Display", (await Query("resolution"))[0].Title);
        Assert.Equal("Bluetooth", (await Query("bluetooth"))[0].Title);
    }

    [Fact]
    public async Task Russian_names_and_keywords_are_searched_and_shown_in_russian()
    {
        var hit = (await Query("экран блокировки", "ru"))[0];
        Assert.Equal("Экран блокировки", hit.Title);
        Assert.Equal("Параметры", hit.Subtitle);
    }

    [Fact]
    public async Task Russian_keyword_finds_the_page_in_an_english_ui()
    {
        Assert.Equal("Sound", (await Query("громкость"))[0].Title);
    }

    [Fact]
    public async Task Unrelated_text_matches_nothing()
    {
        Assert.Empty(await Query("zqxjv"));
    }
}

public class CommandsProviderTests
{
    private static async Task<IReadOnlyList<SearchHit>> Query(string text, string locale = "en") =>
        await new CommandsProvider().QueryAsync(new SearchQuery(text, locale), CancellationToken.None);

    [Fact]
    public async Task Lock_runs_the_power_action_directly()
    {
        var hit = (await Query("lock screen"))[0];
        Assert.Equal("Lock Screen", hit.Title);
        Assert.Equal(new PowerOperation(PowerAction.Lock), hit.Actions[0].Operation);
        Assert.True(hit.Actions[0].Primary);
    }

    [Fact]
    public async Task Destructive_commands_have_no_primary_action_and_need_the_confirm_id()
    {
        var hit = (await Query("restart"))[0];
        var action = Assert.Single(hit.Actions);
        Assert.Equal(CommandsProvider.ConfirmActionId, action.Id);
        Assert.False(action.Primary);
        Assert.Equal(new PowerOperation(PowerAction.Restart), action.Operation);
        Assert.Equal("Needs confirmation", hit.Subtitle);
    }

    [Theory]
    [InlineData("shut down", PowerAction.Shutdown)]
    [InlineData("log out", PowerAction.Logout)]
    [InlineData("sleep", PowerAction.Sleep)]
    [InlineData("hibernate", PowerAction.Hibernate)]
    public async Task Power_commands_are_found_by_name(string query, PowerAction action)
    {
        var hit = (await Query(query))[0];
        Assert.Equal(new PowerOperation(action), hit.Actions[0].Operation);
    }

    [Fact]
    public async Task Russian_destructive_command_is_found_and_keeps_confirm()
    {
        var hit = (await Query("перезагрузить", "ru"))[0];
        Assert.Equal("Перезагрузить", hit.Title);
        Assert.Equal(CommandsProvider.ConfirmActionId, hit.Actions[0].Id);
    }

    [Theory]
    [InlineData("dark", ThemePreference.Dark)]
    [InlineData("light mode", ThemePreference.Light)]
    public async Task Theme_commands_set_the_app_preference(string query, ThemePreference preference)
    {
        var hit = (await Query(query))[0];
        Assert.Equal(new ThemeOperation(preference), hit.Actions[0].Operation);
    }

    [Fact]
    public async Task Taskbar_show_and_hide_and_quit_and_desktop_are_offered()
    {
        Assert.Equal(new TaskbarOperation(TaskbarMode.Visible), (await Query("show taskbar"))[0].Actions[0].Operation);
        Assert.Equal(new TaskbarOperation(TaskbarMode.Hidden), (await Query("hide taskbar"))[0].Actions[0].Operation);
        Assert.IsType<QuitOperation>((await Query("quit betterwindows"))[0].Actions[0].Operation);
        Assert.IsType<ShowDesktopOperation>((await Query("show desktop"))[0].Actions[0].Operation);
    }

    [Fact]
    public async Task Nonsense_matches_no_command()
    {
        Assert.Empty(await Query("zzqqxx"));
    }
}

public class WindowsProviderTests
{
    [Fact]
    public async Task Matches_window_titles_and_activates_with_the_handle_and_process()
    {
        var tracker = new TestWindowTracker();
        tracker.Add(501, appId: "exe:c:\\apps\\editor.exe", name: "Editor");
        tracker.Add(502, appId: "exe:c:\\apps\\mail.exe", name: "Mail");
        var provider = new WindowsProvider(tracker);

        var hits = await provider.QueryAsync(new SearchQuery("mail", "en"), CancellationToken.None);
        var hit = Assert.Single(hits);
        Assert.Equal("Mail", hit.Title);
        Assert.Equal(new ActivateWindowOperation(502, 100), hit.Actions[0].Operation);
        Assert.Equal(DynUrls.WindowIcon(502, 64), hit.IconUrl);
    }

    [Fact]
    public async Task Windows_without_titles_are_skipped()
    {
        var tracker = new TestWindowTracker();
        tracker.Add(701, appId: "exe:c:\\x\\a.exe", name: "");
        var hits = await new WindowsProvider(tracker).QueryAsync(new SearchQuery("a", "en"), CancellationToken.None);
        Assert.Empty(hits);
    }
}

public class FilesProviderTests
{
    [Fact]
    public async Task Queries_shorter_than_two_characters_never_touch_the_index()
    {
        var files = new FakeFileSearch();
        files.Hits.Add(new FileHit(@"C:\a\r.txt", "r.txt", false, null));
        var provider = new FilesProvider(files, new ManualTimeProvider());
        Assert.Empty(await provider.QueryAsync(new SearchQuery("r", "en"), CancellationToken.None));
    }

    [Fact]
    public async Task Waits_for_the_idle_delay_and_then_returns_index_hits_with_actions()
    {
        var time = new ManualTimeProvider();
        var files = new FakeFileSearch();
        files.Hits.Add(new FileHit(@"C:\Users\me\Documents\report.docx", "report.docx", false, null));
        files.Hits.Add(new FileHit(@"C:\Users\me\Documents\Reports", "Reports", true, null));
        var provider = new FilesProvider(files, time);

        var task = provider.QueryAsync(new SearchQuery("report", "en"), CancellationToken.None);
        Assert.False(task.IsCompleted);
        time.Advance(FilesProvider.IdleDelay);
        var hits = await task;

        Assert.Equal(2, hits.Count);
        var first = hits[0];
        Assert.Equal("report.docx", first.Title);
        Assert.Equal(@"C:\Users\me\Documents", first.Subtitle);
        Assert.Equal(new OpenPathOperation(@"C:\Users\me\Documents\report.docx"), first.Actions[0].Operation);
        Assert.Equal(new RevealPathOperation(@"C:\Users\me\Documents\report.docx"), first.Actions[1].Operation);
        Assert.Equal(new CopyTextOperation(@"C:\Users\me\Documents\report.docx"), first.Actions[2].Operation);
        Assert.Equal(DynUrls.PathIcon(@"C:\Users\me\Documents\report.docx", 64), first.IconUrl);
        Assert.Equal(@"C:\Users\me\Documents\report.docx", first.PreviewPath);
    }

    [Fact]
    public async Task A_cancelled_query_stops_during_the_idle_wait()
    {
        var time = new ManualTimeProvider();
        var files = new FakeFileSearch();
        files.Hits.Add(new FileHit(@"C:\a\note.txt", "note.txt", false, null));
        var provider = new FilesProvider(files, time);
        using var cts = new CancellationTokenSource();
        var task = provider.QueryAsync(new SearchQuery("note", "en"), cts.Token);
        cts.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => task);
    }

    [Fact]
    public async Task Unavailable_index_yields_nothing()
    {
        var files = new FakeFileSearch { IsAvailable = false };
        files.Hits.Add(new FileHit(@"C:\a\note.txt", "note.txt", false, null));
        Assert.Empty(await new FilesProvider(files, new ManualTimeProvider()).QueryAsync(new SearchQuery("note", "en"), CancellationToken.None));
    }

    [Fact]
    public async Task Hits_with_paths_that_fail_validation_are_dropped()
    {
        var time = new ManualTimeProvider();
        var files = new FakeFileSearch();
        files.Hits.Add(new FileHit(@"\\server\share\note.txt", "note.txt", false, null));
        files.Hits.Add(new FileHit(@"C:\ok\note.txt", "note.txt", false, null));
        var task = new FilesProvider(files, time).QueryAsync(new SearchQuery("note", "en"), CancellationToken.None);
        time.Advance(FilesProvider.IdleDelay);
        var hits = await task;
        Assert.Equal(@"C:\ok\note.txt", Assert.Single(hits).PreviewPath);
    }
}

public class WebProviderTests
{
    private const string DefaultTemplate = "https://duckduckgo.com/?q={query}";

    private static async Task<IReadOnlyList<SearchHit>> Query(string text, string template = DefaultTemplate) =>
        await new WebProvider(() => template).QueryAsync(new SearchQuery(text, "en"), CancellationToken.None);

    [Fact]
    public async Task A_bare_host_gets_an_open_result_first_and_the_search_result_last()
    {
        var hits = await Query("example.com");
        Assert.Equal(2, hits.Count);
        Assert.Equal("Open example.com", hits[0].Title);
        Assert.Equal(new OpenUriOperation("https://example.com/"), hits[0].Actions[0].Operation);
        Assert.Equal("Search the web for example.com", hits[1].Title);
    }

    [Fact]
    public async Task An_explicit_https_url_opens_as_typed()
    {
        var hit = (await Query("https://example.org/docs?x=1"))[0];
        Assert.Equal(new OpenUriOperation("https://example.org/docs?x=1"), hit.Actions[0].Operation);
    }

    [Fact]
    public async Task Other_schemes_are_never_offered_as_open()
    {
        var hits = await Query("javascript:alert(1)");
        Assert.DoesNotContain(hits, h => h.Actions[0].Operation is OpenUriOperation { Uri: var u } && !u.StartsWith("https://duckduckgo.com"));
        Assert.DoesNotContain(hits, h => h.Title.StartsWith("Open ", StringComparison.Ordinal));
        var ftp = await Query("ftp://files.example.com");
        Assert.DoesNotContain(ftp, h => h.Title.StartsWith("Open ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Plain_text_becomes_a_url_encoded_search()
    {
        var hit = Assert.Single(await Query("hello world"));
        Assert.Equal(new OpenUriOperation("https://duckduckgo.com/?q=hello%20world"), hit.Actions[0].Operation);
        Assert.Equal(SearchProviderId.Web, hit.Provider);
        Assert.Equal(0.1, hit.Score);
    }

    [Fact]
    public async Task File_names_are_not_mistaken_for_hosts()
    {
        var hit = Assert.Single(await Query("report.pdf"));
        Assert.StartsWith("Search the web", hit.Title);
    }

    [Fact]
    public async Task A_template_without_the_placeholder_or_with_an_unsafe_scheme_gives_no_search()
    {
        Assert.Empty(await Query("hello", "https://duckduckgo.com/"));
        Assert.Empty(await Query("hello", "javascript:{query}"));
        Assert.Empty(await Query("hello", "file:///C:/{query}"));
    }

    [Fact]
    public async Task Empty_text_yields_nothing()
    {
        Assert.Empty(await Query("   "));
    }

    [Fact]
    public async Task The_template_is_read_for_every_query()
    {
        var template = DefaultTemplate;
        var provider = new WebProvider(() => template);
        template = "https://www.bing.com/search?q={query}";
        var hit = Assert.Single(await provider.QueryAsync(new SearchQuery("cats", "en"), CancellationToken.None));
        Assert.Equal(new OpenUriOperation("https://www.bing.com/search?q=cats"), hit.Actions[0].Operation);
    }
}
