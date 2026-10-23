using BetterWindows.Core.Dock;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Publishes <c>activeApp.changed</c> (the foreground app, ignoring BetterWindows' own windows; <c>app</c> is null when there is none). It is a state event: the
/// publisher pushes the current value to a menu bar / overlay right after its <c>surface.ready</c> and de-duplicates identical snapshots, so this class only
/// declares the topic and invalidates it when the tracker or the catalog changes.
/// </summary>
public sealed class ActiveAppController : IShellController
{
    private const int IconPx = 64;
    private const int MaxTitleChars = 256;
    private readonly IStatePublisher _state;
    private readonly IWindowTracker _tracker;
    private readonly IAppCatalog _catalog;
    private readonly ILogger _log;
    private IStateTopic? _topic;

    public ActiveAppController(IStatePublisher state, IWindowTracker tracker, IAppCatalog catalog, ILogger<ActiveAppController>? log = null)
    {
        _state = state;
        _tracker = tracker;
        _catalog = catalog;
        _log = (ILogger?)log ?? NullLogger.Instance;
    }

    public string Name => "ActiveAppController";

    public void Start()
    {
        _topic = _state.Declare<ActiveAppState>(Msg.ActiveAppChanged, _ => Current());
        _tracker.Changed += OnTrackerChanged;
        _tracker.ForegroundChanged += OnForegroundChanged;
        _catalog.Changed += Invalidate;
    }

    public void Stop()
    {
        _tracker.Changed -= OnTrackerChanged;
        _tracker.ForegroundChanged -= OnForegroundChanged;
        _catalog.Changed -= Invalidate;
    }

    private void OnTrackerChanged(WindowSnapshot _) => Invalidate();
    private void OnForegroundChanged(TrackedWindow? _) => Invalidate();
    private void Invalidate() => _topic?.Invalidate();

    private ActiveAppState Current()
    {
        var fg = _tracker.Snapshot.Foreground;
        if (fg is null) return new ActiveAppState();
        bool inCatalog;
        try { inCatalog = _catalog.FindById(fg.AppId) is not null; }
        catch (Exception ex) { _log.LogDebug(ex, "Catalog lookup failed"); inCatalog = false; }
        return new ActiveAppState
        {
            App = new ActiveApp
            {
                AppId = fg.AppId,
                Name = fg.AppName,
                IconUrl = AppIconUrls.ForApp(fg.AppId, inCatalog, fg.Handle, IconPx),
                Handle = Hwnd.ToWire(fg.Handle),
                Title = InputPolicy.Truncate(fg.Raw.Title, MaxTitleChars),
            },
        };
    }
}
