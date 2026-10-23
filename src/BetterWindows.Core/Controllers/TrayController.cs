using BetterWindows.Core.Input;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Status;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Notification-area (tray) icons in the menu bar. Hosting a second notification area next to a running Explorer is not possible (apps send Shell_NotifyIcon to Explorer's own
/// Shell_TrayWnd), so tray.state reports <c>supported = false</c> unless the experimental flag settings.general.features.trayHosting is on AND the platform bound a build-specific
/// mechanism (<see cref="ITrayHost.IsSupported"/>). The controller also starts/stops the host to match the flag, publishes tray.state (state event) and forwards tray.click.
/// <para>tray.click: the id must belong to a currently known icon; the click position is the real cursor position from <see cref="ICursorProvider"/>, never a UI-supplied
/// coordinate (the UI's DIP anchor is meaningless in screen space and untrusted).</para>
/// </summary>
public sealed class TrayController : IShellController
{
    private static readonly TimeSpan Coalesce = TimeSpan.FromMilliseconds(100);
    private const int MaxIdChars = 256;

    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly ITrayHost _tray;
    private readonly ISettingsService _settings;
    private readonly ICursorProvider _cursor;
    private readonly ILogger _log;
    private readonly Dictionary<string, (string Signature, int Rev)> _revisions = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private readonly SerialRunner _hostLifecycle;
    private IStateTopic? _topic;
    private Action? _onTrayChanged;
    private Action<Settings>? _onSettings;
    private volatile bool _started, _stopped;
    private bool _hostRunning;
    private int _revCounter;

    public TrayController(IMessageRouter router, IStatePublisher state, ITrayHost tray, ISettingsService settings, ICursorProvider cursor, ILogger<TrayController>? log = null)
    {
        _router = router;
        _state = state;
        _tray = tray;
        _settings = settings;
        _cursor = cursor;
        _log = (ILogger?)log ?? NullLogger.Instance;
        _hostLifecycle = new SerialRunner(ReconcileHost, _log);
    }

    public string Name => "TrayController";

    public void Start()
    {
        if (_started) return;
        _started = true;

        _topic = _state.Declare<TrayState>(Msg.TrayState, _ => Snapshot(), Coalesce);
        _onTrayChanged = () => _topic?.Invalidate();
        _tray.Changed += _onTrayChanged;
        _onSettings = _ => { _topic?.Invalidate(); _hostLifecycle.Trigger(); };
        _settings.Changed += _onSettings;
        _router.Handle<TrayClickParams>(Msg.TrayClick, Click);
        _hostLifecycle.Trigger();
    }

    public void Stop()
    {
        _stopped = true;
        if (_onTrayChanged is not null) _tray.Changed -= _onTrayChanged;
        if (_onSettings is not null) _settings.Changed -= _onSettings;
        _onTrayChanged = null;
        _onSettings = null;
        _hostLifecycle.Stop();
        StopHost();
    }

    /// <summary>The feature flag AND a platform that can really receive tray notifications.</summary>
    private bool IsHostingEnabled() => _settings.Current.General.Features.TrayHosting && _tray.IsSupported;

    // ---- host lifecycle (never on the caller's thread) ----

    private void ReconcileHost()
    {
        var want = IsHostingEnabled();
        bool start, stop;
        lock (_gate)
        {
            start = want && !_hostRunning;
            stop = !want && _hostRunning;
            if (start) _hostRunning = true;
            if (stop) _hostRunning = false;
        }
        try
        {
            if (start) _tray.Start();
            else if (stop) _tray.Stop();
        }
        catch (Exception ex) { _log.LogWarning(ex, "Tray host {Operation} failed", start ? "start" : "stop"); }
        if (_stopped) StopHost(); // the controller was stopped while this pass was starting the host: do not leave it running
        _topic?.Invalidate();
    }

    private void StopHost()
    {
        bool stop;
        lock (_gate) { stop = _hostRunning; _hostRunning = false; }
        if (!stop) return;
        try { _tray.Stop(); }
        catch (Exception ex) { _log.LogDebug(ex, "Tray host stop failed"); }
    }

    // ---- state ----

    private TrayState Snapshot()
    {
        if (!IsHostingEnabled()) return new TrayState { Supported = false };
        var icons = _tray.Icons
            .Take(StatusSanitizer.MaxTrayIcons)
            .Select(i =>
            {
                var id = StatusSanitizer.Text(i.Id, MaxIdChars) ?? "";
                var tooltip = StatusSanitizer.Text(i.Tooltip) ?? "";
                return new TrayIcon { Id = id, Tooltip = tooltip, IconUrl = DynUrls.TrayIcon(id, RevisionOf(id, i, tooltip)), Hidden = i.Hidden };
            })
            .ToArray();
        return new TrayState { Supported = true, Icons = icons };
    }

    /// <summary>Per-icon cache-busting revision: changes when the icon's host-reported record (image marker, tooltip, visibility) changes. Idempotent for equal input.</summary>
    private int RevisionOf(string id, TrayIcon icon, string tooltip)
    {
        var signature = string.Join('\u0001', icon.IconUrl, tooltip, icon.Hidden ? "h" : "v");
        lock (_gate)
        {
            if (_revisions.TryGetValue(id, out var known) && known.Signature == signature) return known.Rev;
            var rev = ++_revCounter;
            _revisions[id] = (signature, rev);
            if (_revisions.Count > 4 * StatusSanitizer.MaxTrayIcons) _revisions.Clear(); // icons come and go; do not grow without bound
            return rev;
        }
    }

    // ---- tray.click ----

    private Task Click(CallContext ctx, TrayClickParams p)
    {
        if (!Enum.IsDefined(p.Button)) throw ProtocolException.BadRequest("unknown tray button");
        if (string.IsNullOrEmpty(p.Id) || p.Id.Length > MaxIdChars || p.Id.Any(c => c < 0x20 || c == 0x7f)) throw ProtocolException.BadRequest("invalid tray icon id");
        if (!IsHostingEnabled()) throw ProtocolException.Unsupported("tray hosting is not available");
        if (!_tray.Icons.Any(i => string.Equals(i.Id, p.Id, StringComparison.Ordinal))) throw ProtocolException.NotFound("unknown tray icon");
        if (!_cursor.TryGetCursorPosition(out var pos)) throw ProtocolException.Failed("the pointer position is not available");

        try { _tray.Click(p.Id, p.Button, pos); }
        catch (Exception ex)
        {
            _log.LogWarning(ex, "tray.click failed");
            throw ProtocolException.Failed("tray.click failed");
        }
        return Task.CompletedTask;
    }
}
