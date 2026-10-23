using System.Globalization;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Search;
using BetterWindows.Core.Search.Providers;
using BetterWindows.Core.Services;
using BetterWindows.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Spotlight's search (docs/IPC.md section 6): search.query starts a run and returns at once; the merged results stream to the sending overlay as
/// search.results. A new query, search.cancel, the overlay hiding or the surface closing cancels the run. search.perform runs a result's action (ids are
/// resolved from the host-side request tables only), search.recent and search.preview serve the empty state and the preview pane. Owner: core-search.
/// </summary>
public sealed class SearchController : IShellController
{
    private readonly IMessageRouter _router;
    private readonly IEventHub _hub;
    private readonly ISettingsService _settings;
    private readonly IOverlayState _overlay;
    private readonly ISurfaceManager _surfaces;
    private readonly IAppCatalog _catalog;
    private readonly ILogger _log;
    private readonly SearchEngine _engine;
    private readonly SearchActionRunner _actions;
    private readonly SearchPreviewer _previewer;
    private readonly UsageStore _usage;
    private readonly Dictionary<SearchProviderId, ISearchProvider> _providers;
    private readonly object _gate = new();
    private ActiveRun? _active;
    private bool _started;

    /// <summary>The run that currently owns search.results (a new query replaces it).</summary>
    private sealed record ActiveRun(string RequestId, CancellationTokenSource Cancellation);

    public SearchController(
        IMessageRouter router, IEventHub hub, ISettingsService settings, IOverlayState overlay, ISurfaceManager surfaces, IAppCatalog catalog,
        IWindowTracker tracker, IWindowSystem windows, IAppLauncher launcher, IClipboard clipboard, ISystemPower power, IAppLifecycle lifecycle,
        ITaskbarController taskbar, IHotkeyStatus hotkeys, IInputSimulator input, IFileSearch files, IAppPaths paths, TimeProvider time,
        ILogger<SearchController> log)
    {
        _router = router;
        _hub = hub;
        _settings = settings;
        _overlay = overlay;
        _surfaces = surfaces;
        _catalog = catalog;
        _log = log;

        _usage = new UsageStore(Path.Combine(paths.DataDir, "search-usage.json"), time, log);
        _engine = new SearchEngine(time);
        _actions = new SearchActionRunner(catalog, launcher, clipboard, power, lifecycle, taskbar, hotkeys, input, surfaces, settings, tracker, windows, _usage);
        _previewer = new SearchPreviewer();
        _providers = new Dictionary<SearchProviderId, ISearchProvider>
        {
            [SearchProviderId.Apps] = new AppsProvider(catalog, _usage),
            [SearchProviderId.Calc] = new CalculatorProvider(),
            [SearchProviderId.Settings] = new SettingsPagesProvider(),
            [SearchProviderId.Commands] = new CommandsProvider(),
            [SearchProviderId.Windows] = new WindowsProvider(tracker),
            [SearchProviderId.Files] = new FilesProvider(files, time),
            [SearchProviderId.Web] = new WebProvider(() => settings.Current.Spotlight.WebSearchUrl),
        };
    }

    public string Name => "SearchController";

    public void Start()
    {
        if (_started) return;
        _started = true;

        _router.Handle<SearchQueryParams>(Msg.SearchQuery, (ctx, p) => { Query(ctx, p); return Task.CompletedTask; });
        _router.Handle<SearchCancelParams>(Msg.SearchCancel, (_, p) => { Cancel(p.RequestId); return Task.CompletedTask; });
        _router.Handle<SearchPerformParams>(Msg.SearchPerform, Perform);
        _router.HandleNoParams<SearchRecent>(Msg.SearchRecent, _ => Task.FromResult(Recent()));
        _router.Handle<SearchPreviewParams, SearchPreview>(Msg.SearchPreview, Preview);

        _overlay.Changed += OnOverlayChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _overlay.Changed -= OnOverlayChanged;
        CancelActive(null);
    }

    /// <summary>Starts a run for the sending surface. Empty and over-long queries complete at once with no results.</summary>
    private void Query(CallContext ctx, SearchQueryParams p)
    {
        var raw = p.Query ?? "";
        var text = InputPolicy.Truncate(raw, Limits.MaxSearchQueryChars).Trim();
        if (raw.Length > Limits.MaxSearchQueryChars || text.Length == 0)
        {
            CancelActive(null);
            Publish(ctx.Surface, p.RequestId, Array.Empty<SearchResult>(), complete: true);
            return;
        }

        var spotlight = _settings.Current.Spotlight;
        var query = new SearchQuery(text, Locale());
        var providers = EnabledProviders(spotlight.Providers);
        var maxResults = Math.Clamp(spotlight.MaxResults, 1, SearchMerger.MaxResultsLimit);

        var run = new ActiveRun(p.RequestId, new CancellationTokenSource());
        ActiveRun? previous;
        lock (_gate)
        {
            previous = _active;
            _active = run;
        }
        previous?.Cancellation.Cancel();

        // The surface closing cancels the run through the call's token; the run itself stops at the next checkpoint.
        var surfaceClosed = ctx.Cancellation.Register(() => run.Cancellation.Cancel());
        var surface = ctx.Surface;
        _ = Task.Run(async () =>
        {
            try
            {
                await _engine.RunAsync(
                    p.RequestId,
                    query,
                    providers,
                    maxResults,
                    results => Publish(surface, results.RequestId, results.Results, results.Complete, run),
                    run.Cancellation.Token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                _log.LogError(ex, "search run {RequestId} failed", p.RequestId);
            }
            finally
            {
                surfaceClosed.Dispose();
                lock (_gate)
                {
                    if (ReferenceEquals(_active, run)) _active = null;
                }
            }
        });
    }

    private void Cancel(string? requestId)
    {
        if (string.IsNullOrEmpty(requestId)) return;
        lock (_gate)
        {
            if (_active is { } run && run.RequestId == requestId) run.Cancellation.Cancel();
        }
    }

    /// <summary>Cancels whatever run is active (or only the given one).</summary>
    private void CancelActive(ActiveRun? only)
    {
        ActiveRun? run;
        lock (_gate)
        {
            run = _active;
            if (only is not null && !ReferenceEquals(only, run)) return;
            _active = null;
        }
        run?.Cancellation.Cancel();
    }

    private void OnOverlayChanged(OverlayState state)
    {
        // Quick: cancellation only. Runs for a hidden overlay must not keep providers busy.
        if (!state.Visible) CancelActive(null);
    }

    private void Publish(SurfaceId surface, string requestId, IReadOnlyList<SearchResult> results, bool complete, ActiveRun? run = null)
    {
        // A run superseded in the meantime must not overwrite the newer list (the UI ignores it too, but do not send it).
        if (run is not null && run.Cancellation.IsCancellationRequested) return;
        _hub.PublishTo(surface, Msg.SearchResults, new SearchResults { RequestId = requestId, Results = results, Complete = complete });
    }

    private async Task Perform(CallContext ctx, SearchPerformParams p)
    {
        var hit = _engine.TryResolve(p.RequestId, p.ResultId) ?? throw ProtocolException.NotFound("the result is no longer available");

        HitAction action;
        if (p.ActionId is null)
        {
            // Enter runs the primary action only. Destructive results have no primary action: they need the explicit "confirm" id.
            action = hit.Actions.FirstOrDefault(a => a.Primary)
                ?? throw ProtocolException.BadRequest(hit.Actions.Count == 0 ? "the result has no action" : "this action needs an explicit confirmation");
        }
        else
        {
            action = hit.Actions.FirstOrDefault(a => a.Id == p.ActionId) ?? throw ProtocolException.BadRequest("unknown action for this result");
        }

        await _actions.RunAsync(action.Operation, ctx.Cancellation).ConfigureAwait(false);
        _surfaces.HideOverlay();
    }

    private SearchRecent Recent()
    {
        var hits = SearchRecents.Build(_usage, _catalog, _settings.Current.Dock.Pins, Locale());
        return _engine.RegisterRecent(hits);
    }

    private async Task<SearchPreview> Preview(CallContext ctx, SearchPreviewParams p)
    {
        var hit = _engine.TryResolve(p.RequestId, p.ResultId) ?? throw ProtocolException.NotFound("the result is no longer available");
        return await _previewer.PreviewAsync(hit, ctx.Cancellation).ConfigureAwait(false);
    }

    /// <summary>The enabled providers in the order of settings.spotlight.providers; unknown and duplicate names are ignored.</summary>
    private List<ISearchProvider> EnabledProviders(IEnumerable<string> names)
    {
        var list = new List<ISearchProvider>();
        var seen = new HashSet<SearchProviderId>();
        foreach (var name in names)
        {
            if (!Enum.TryParse<SearchProviderId>(name, ignoreCase: true, out var id) || !Enum.IsDefined(id)) continue;
            if (!seen.Add(id) || !_providers.TryGetValue(id, out var provider)) continue;
            list.Add(provider);
        }
        return list;
    }

    private string Locale() => LocaleResolver.Resolve(_settings.Current.General.Language, CultureInfo.CurrentUICulture.Name);
}
