using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// The installed-app catalog for the UI (apps.list, apps.changed) and launch / reveal by id. The UI sends an id only: no arguments are ever accepted, and every id is
/// resolved to a host-known target (catalog, running window or a path granted by system.pickPath) before anything is started (docs/IPC.md section 14).
/// </summary>
public sealed class AppsController : IShellController
{
    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly IAppCatalog _catalog;
    private readonly IWindowTracker _tracker;
    private readonly IPathGrants _grants;
    private readonly IAppLauncher _launcher;
    private readonly ILogger _log;
    private IStateTopic? _topic;
    private bool _started;

    public AppsController(IMessageRouter router, IStatePublisher state, IAppCatalog catalog, IWindowTracker tracker, IPathGrants grants, IAppLauncher launcher, ILogger<AppsController> log)
    {
        _router = router;
        _state = state;
        _catalog = catalog;
        _tracker = tracker;
        _grants = grants;
        _launcher = launcher;
        _log = log;
    }

    public string Name => "AppsController";

    public void Start()
    {
        if (_started) return;
        _started = true;

        _router.HandleNoParams<AppList>(Msg.AppsList, _ => Task.FromResult(CurrentList()));
        _router.Handle<AppLaunchParams>(Msg.AppsLaunch, (_, p) => { Launch(p.AppId); return Task.CompletedTask; });
        _router.Handle<AppIdParams>(Msg.AppsReveal, (_, p) => { Reveal(p.AppId); return Task.CompletedTask; });

        _topic = _state.Declare<AppList>(Msg.AppsChanged, _ => CurrentList());
        _catalog.Changed += OnCatalogChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _catalog.Changed -= OnCatalogChanged;
    }

    private void OnCatalogChanged() => _topic?.Invalidate();

    private AppList CurrentList() => new() { Apps = _catalog.Apps.ToArray() };

    private void Launch(string appId)
    {
        var target = Resolve(appId);
        var result = _launcher.Launch(target.Launch);
        switch (result)
        {
            case LaunchResult.Started: return;
            case LaunchResult.NotFound: throw ProtocolException.NotFound("the app could not be found any more");
            default: throw ProtocolException.Failed("the app could not be started");
        }
    }

    private void Reveal(string appId)
    {
        var target = Resolve(appId);
        if (target.RevealPath is null) throw ProtocolException.Unsupported("this app has no file to show in Explorer");
        if (!_launcher.RevealInExplorer(target.RevealPath)) throw ProtocolException.Failed("Explorer could not show the app");
    }

    /// <summary>What an app id means on this machine. The launch target and (if any) the file to reveal are both host-derived.</summary>
    internal sealed record Target(LaunchTarget Launch, string? RevealPath);

    /// <summary>
    /// Resolves an app id: <c>aumid:</c> must be in the catalog or belong to a running window; <c>exe:</c> must be in the catalog, a running window's executable or a
    /// path granted this session; <c>dir:</c> is not an app (folders are opened by dock.activate). Unknown ids are notFound, malformed ids badRequest.
    /// </summary>
    internal Target Resolve(string? appId)
    {
        if (!InputPolicy.TryParseAppId(appId, out var kind, out var value)) throw ProtocolException.BadRequest("invalid app id");
        var running = _tracker.Snapshot.Windows;
        switch (kind)
        {
            case AppIdKind.Aumid:
            {
                var entry = _catalog.FindByAumid(value);
                var window = running.FirstOrDefault(w => string.Equals(w.AppId, appId, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(w.Raw.Aumid, value, StringComparison.OrdinalIgnoreCase));
                if (entry is null && window is null) throw ProtocolException.NotFound("unknown app");
                var revealPath = entry?.Path ?? window?.Raw.ExePath;
                return new Target(LaunchTarget.FromAumid(value), NormalizeOrNull(revealPath));
            }
            case AppIdKind.ExePath:
            {
                var entry = _catalog.FindByExePath(value);
                var tracked = running.Any(w => string.Equals(NormalizeOrNull(w.Raw.ExePath), value, StringComparison.OrdinalIgnoreCase));
                if (entry is null && !tracked && !_grants.IsGranted(value)) throw ProtocolException.NotFound("unknown app");
                return new Target(LaunchTarget.FromPath(value), value);
            }
            default:
                throw ProtocolException.NotFound("folders are not apps; use dock.activate");
        }
    }

    private static string? NormalizeOrNull(string? path) => InputPolicy.TryNormalizeLocalPath(path, out var p) ? p : null;
}
