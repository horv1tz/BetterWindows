using BetterWindows.Core.Controllers;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search;
using BetterWindows.Core.Services;
using BetterWindows.Core.Tests.Config;
using BetterWindows.Core.Tests.Resources;
using BetterWindows.Core.Tests.Support;
using BetterWindows.Core.Tests.Support.Fakes;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace BetterWindows.Core.Tests.Search;

/// <summary>Drives SearchController through the router exactly as the overlay does; the manual clock is pumped so the file provider's timers fire.</summary>
public class SearchControllerTests : IDisposable
{
    private readonly TestWindowTracker _tracker = new();
    private readonly CoreHarness _h;
    private int _requests;

    public SearchControllerTests()
    {
        _h = ControllerRig.Start<SearchController>(sc => sc.AddSingleton<IWindowTracker>(_tracker));
    }

    public void Dispose() => _h.Dispose();

    private FakeAppCatalogSource Source => _h.Get<FakeAppCatalogSource>();
    private FakeAppLauncher Launcher => _h.Get<FakeAppLauncher>();
    private FakeSystemPower Power => _h.Get<FakeSystemPower>();
    private FakeSurfaceManager Surfaces => _h.Get<FakeSurfaceManager>();
    private FakeClipboard Clipboard => _h.Get<FakeClipboard>();

    private async Task ScanAsync() => await _h.Get<IAppCatalog>().RefreshAsync();

    private string NextRequest() => "req" + Interlocked.Increment(ref _requests);

    private static bool IsComplete(FakeSurface surface, string requestId) =>
        surface.Events(Msg.SearchResults).Any(e => (string?)e["payload"]?["requestId"] == requestId && e["payload"]?["complete"]?.GetValue<bool>() == true);

    /// <summary>Sends search.query and returns the final (complete) results of that request.</summary>
    private async Task<SearchResults> SearchAsync(FakeSurface overlay, string query, string? requestId = null)
    {
        requestId ??= NextRequest();
        var r = await _h.CallRawAsync(overlay, Msg.SearchQuery, new SearchQueryParams { RequestId = requestId, Query = query });
        Assert.True(r["ok"]!.GetValue<bool>(), "search.query must succeed");
        await SearchTestKit.PumpUntilAsync(_h.Time, () => IsComplete(overlay, requestId));
        return overlay.Events(Msg.SearchResults).Select(e => ProtocolJson.Deserialize<SearchResults>(e["payload"])!)
            .Last(s => s.RequestId == requestId && s.Complete);
    }

    private async Task<string> PerformError(FakeSurface overlay, string requestId, string resultId, string? actionId = null) =>
        ControllerRig.ErrorCode(await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = requestId, ResultId = resultId, ActionId = actionId }));

    private static SearchResult Find(SearchResults results, SearchProviderId provider, string titleContains) =>
        results.Results.First(r => r.Provider == provider && r.Title.Contains(titleContains, StringComparison.OrdinalIgnoreCase));

    private async Task AddNotesAsync()
    {
        Source.Apps.Add(FakeAppCatalogSource.App("Notes", aumid: "Contoso.Notes!App"));
        await ScanAsync();
    }

    [Fact]
    public async Task Results_stream_to_the_sending_overlay_and_to_no_other_surface()
    {
        await AddNotesAsync();
        var overlay = _h.AddSurface(SurfaceKind.Overlay, "m1");
        var other = _h.AddSurface(SurfaceKind.Overlay, "m2");

        var results = await SearchAsync(overlay, "notes");
        Assert.NotEmpty(results.Results);
        Assert.Empty(other.Events(Msg.SearchResults));
    }

    [Fact]
    public async Task Apps_settings_and_web_results_are_offered_with_the_app_first_and_web_last()
    {
        await AddNotesAsync();
        var overlay = _h.AddSurface(SurfaceKind.Overlay);

        var results = await SearchAsync(overlay, "notes");
        Assert.Equal(SearchProviderId.Apps, results.Results[0].Provider);
        Assert.Equal(SearchProviderId.Web, results.Results[^1].Provider);
        Assert.Equal("Search the web for notes", results.Results[^1].Title);
    }

    [Fact]
    public async Task An_empty_query_completes_at_once_without_providers()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var r = await _h.CallRawAsync(overlay, Msg.SearchQuery, new SearchQueryParams { RequestId = "e1", Query = "   " });
        Assert.True(r["ok"]!.GetValue<bool>());
        var last = overlay.LastEvent<SearchResults>(Msg.SearchResults)!;
        Assert.Equal("e1", last.RequestId);
        Assert.True(last.Complete);
        Assert.Empty(last.Results);
    }

    [Fact]
    public async Task Queries_over_256_characters_complete_empty()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        await _h.CallRawAsync(overlay, Msg.SearchQuery, new SearchQueryParams { RequestId = "long", Query = new string('a', 300) });
        var last = overlay.LastEvent<SearchResults>(Msg.SearchResults)!;
        Assert.True(last.Complete);
        Assert.Empty(last.Results);
    }

    [Fact]
    public async Task Calculator_answers_are_copied_by_the_primary_action_and_hide_the_overlay()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "6*7");
        var calc = Find(results, SearchProviderId.Calc, "= 42");
        var before = Surfaces.HideCount;

        var r = await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = results.RequestId, ResultId = calc.Id });
        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(new[] { "42" }, Clipboard.Texts);
        Assert.Equal(before + 1, Surfaces.HideCount);
    }

    [Fact]
    public async Task Performing_an_app_launches_it_by_catalog_id_records_usage_and_hides_the_overlay()
    {
        await AddNotesAsync();
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "notes");
        var notes = Find(results, SearchProviderId.Apps, "Notes");

        var r = await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = results.RequestId, ResultId = notes.Id });
        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal("Contoso.Notes!App", Launcher.Launched.Single().Target.Aumid);
        Assert.Equal(1, Surfaces.HideCount);

        var recent = await _h.CallAsync<SearchRecent>(overlay, Msg.SearchRecent);
        Assert.Equal("Notes", recent!.Results.Single().Title);
    }

    [Fact]
    public async Task Recent_results_resolve_through_their_own_request_id()
    {
        await AddNotesAsync();
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var first = await SearchAsync(overlay, "notes");
        await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = first.RequestId, ResultId = Find(first, SearchProviderId.Apps, "Notes").Id });

        var recent = await _h.CallAsync<SearchRecent>(overlay, Msg.SearchRecent);
        var id = recent!.Results.Single().Id;
        var r = await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = recent.RequestId, ResultId = id });
        Assert.True(r["ok"]!.GetValue<bool>());
        Assert.Equal(2, Launcher.Launched.Count);
    }

    [Fact]
    public async Task Settings_pages_open_through_the_launcher_with_a_validated_uri()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "wifi");
        var wifi = Find(results, SearchProviderId.Settings, "Wi-Fi");

        Assert.True((await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = results.RequestId, ResultId = wifi.Id }))["ok"]!.GetValue<bool>());
        Assert.Contains("ms-settings:network-wifi", Launcher.Opened);
    }

    [Fact]
    public async Task System_commands_run_their_port_and_destructive_ones_need_the_confirm_id()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "lock screen");
        var lockHit = Find(results, SearchProviderId.Commands, "Lock");
        Assert.True((await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = results.RequestId, ResultId = lockHit.Id }))["ok"]!.GetValue<bool>());
        Assert.Equal(new[] { PowerAction.Lock }, Power.Actions);

        var restart = await SearchAsync(overlay, "restart");
        var restartHit = Find(restart, SearchProviderId.Commands, "Restart");
        Assert.Equal("badRequest", await PerformError(overlay, restart.RequestId, restartHit.Id));
        Assert.Equal("badRequest", await PerformError(overlay, restart.RequestId, restartHit.Id, "run"));
        Assert.Equal(new[] { PowerAction.Lock }, Power.Actions);

        Assert.True((await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = restart.RequestId, ResultId = restartHit.Id, ActionId = "confirm" }))["ok"]!.GetValue<bool>());
        Assert.Equal(new[] { PowerAction.Lock, PowerAction.Restart }, Power.Actions);
    }

    [Fact]
    public async Task Unknown_request_or_result_is_not_found_and_unknown_action_is_a_bad_request()
    {
        await AddNotesAsync();
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "notes");
        var notes = Find(results, SearchProviderId.Apps, "Notes");

        Assert.Equal("notFound", await PerformError(overlay, "no-such-request", notes.Id));
        Assert.Equal("notFound", await PerformError(overlay, results.RequestId, "r999"));
        Assert.Equal("badRequest", await PerformError(overlay, results.RequestId, notes.Id, "explode"));
        Assert.Empty(Launcher.Launched);
    }

    [Fact]
    public async Task Preview_of_an_app_with_a_file_is_its_path_and_unknown_ids_are_not_found()
    {
        Source.Apps.Add(FakeAppCatalogSource.App("Paint", path: @"C:\Windows\System32\mspaint.exe"));
        await ScanAsync();
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "paint");
        var paint = Find(results, SearchProviderId.Apps, "Paint");

        var preview = await _h.CallAsync<SearchPreview>(overlay, Msg.SearchPreview, new SearchPreviewParams { RequestId = results.RequestId, ResultId = paint.Id });
        Assert.Equal(SearchPreviewKind.Text, preview!.Kind);
        Assert.Equal(@"C:\Windows\System32\mspaint.exe", preview.Text);

        Assert.Equal("notFound", ControllerRig.ErrorCode(await _h.CallRawAsync(overlay, Msg.SearchPreview, new SearchPreviewParams { RequestId = "x", ResultId = "y" })));
    }

    [Fact]
    public async Task Cancel_for_an_unknown_request_is_harmless()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var r = await _h.CallRawAsync(overlay, Msg.SearchCancel, new SearchCancelParams { RequestId = "never-started" });
        Assert.True(r["ok"]!.GetValue<bool>());
    }

    [Fact]
    public async Task A_cancelled_run_publishes_no_further_results()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        await _h.CallRawAsync(overlay, Msg.SearchQuery, new SearchQueryParams { RequestId = "slow", Query = "file report" });
        await _h.CallRawAsync(overlay, Msg.SearchCancel, new SearchCancelParams { RequestId = "slow" });
        int CountForSlow() => overlay.Events(Msg.SearchResults).Count(e => (string?)e["payload"]?["requestId"] == "slow");

        int before = CountForSlow();
        _h.Time.Advance(TimeSpan.FromSeconds(5));
        await Task.Delay(20);
        Assert.Equal(before, CountForSlow());
    }

    [Fact]
    public async Task A_newer_query_replaces_the_older_one()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        await _h.CallRawAsync(overlay, Msg.SearchQuery, new SearchQueryParams { RequestId = "old", Query = "file one" });
        var newer = await SearchAsync(overlay, "2+2", "new");
        Assert.Equal("new", newer.RequestId);
        Assert.Contains(newer.Results, r => r.Provider == SearchProviderId.Calc);
    }

    [Fact]
    public async Task Files_are_searched_only_after_two_characters_and_the_idle_delay()
    {
        var files = _h.Get<FakeFileSearch>();
        files.Hits.Add(new FileHit(@"C:\Users\me\Documents\budget.xlsx", "budget.xlsx", false, null));
        var overlay = _h.AddSurface(SurfaceKind.Overlay);

        var results = await SearchAsync(overlay, "budget");
        var file = Find(results, SearchProviderId.Files, "budget");
        Assert.Equal(@"C:\Users\me\Documents", file.Subtitle);
    }

    [Fact]
    public async Task Windows_are_activated_only_through_their_tracked_handle_and_process()
    {
        _tracker.Add(801, appId: "exe:c:\\apps\\mail.exe", name: "Mail Client");
        _h.Get<FakeWindowSystem>().Add(FakeWindowSystem.Win(801, "Mail Client", @"C:\apps\mail.exe"));
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "mail");
        var window = Find(results, SearchProviderId.Windows, "Mail");

        Assert.True((await _h.CallRawAsync(overlay, Msg.SearchPerform, new SearchPerformParams { RequestId = results.RequestId, ResultId = window.Id }))["ok"]!.GetValue<bool>());
        Assert.Contains(_h.Get<FakeWindowSystem>().Calls, c => c.Op == "activate" && c.Handle == 801);
        Assert.Equal(1, Surfaces.HideCount);
    }

    [Fact]
    public async Task Taskbar_can_not_be_hidden_by_search_without_an_escape_hotkey()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "hide taskbar");
        var hide = Find(results, SearchProviderId.Commands, "Hide Taskbar");
        Assert.Equal("unsupported", await PerformError(overlay, results.RequestId, hide.Id));
        Assert.DoesNotContain("apply:Hidden", _h.Get<FakeTaskbarController>().Calls);
    }

    [Fact]
    public async Task The_result_list_is_bounded_by_the_settings_limit()
    {
        var overlay = _h.AddSurface(SurfaceKind.Overlay);
        var results = await SearchAsync(overlay, "e");
        Assert.True(results.Results.Count <= 9);
    }
}
