using System.Globalization;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Core.Config;
using Microsoft.Extensions.Logging;

namespace BetterWindows.Core.Controllers;

/// <summary>
/// Owns the surface handshake (surface.ready), the window reports of a surface (surface.resize for the dock, surface.shape for dock and menubar, surface.log),
/// the settings calls (settings.get / patch / openWindow) and the settings, theme and locale state topics. Every UI-reported number is validated and clamped here
/// (docs/IPC.md sections 5, 11, 12); the surface identity is always the one the host assigned to the channel.
/// </summary>
public sealed class SurfaceController : IShellController
{
    /// <summary>Settings pages that settings.openWindow accepts (protocol/common.json SettingsOpenParams).</summary>
    public static readonly IReadOnlySet<string> SettingsPages = new HashSet<string>(StringComparer.Ordinal)
    {
        "general", "appearance", "dock", "menubar", "spotlight", "hotCorners", "desktop", "widgets", "shortcuts", "about", "onboarding",
    };

    private const int MaxLogMessageChars = Limits.MaxLogMessageChars, MaxLogDataChars = Limits.MaxLogDataChars;
    /// <summary>The menu bar is an AppBar of fixed height on every monitor (docs/IPC.md section 5).</summary>
    private const double MenuBarHeightDip = 28;

    private readonly IMessageRouter _router;
    private readonly IStatePublisher _state;
    private readonly SurfaceRegistry _registry;
    private readonly ISettingsService _settings;
    private readonly IThemeService _theme;
    private readonly ISurfaceManager _surfaces;
    private readonly IMonitorProvider _monitors;
    private readonly IAppLifecycle _app;
    private readonly ILogger _log;
    private readonly object _gate = new();
    private readonly Dictionary<SurfaceId, (double Width, double Height)> _windowSizes = new();
    private IStateTopic? _settingsTopic, _themeTopic, _localeTopic;
    private bool _started;

    public SurfaceController(
        IMessageRouter router, IStatePublisher state, SurfaceRegistry registry, ISettingsService settings, IThemeService theme, ISurfaceManager surfaces,
        IMonitorProvider monitors, IAppLifecycle app, ILogger<SurfaceController> log)
    {
        _router = router;
        _state = state;
        _registry = registry;
        _settings = settings;
        _theme = theme;
        _surfaces = surfaces;
        _monitors = monitors;
        _app = app;
        _log = log;
    }

    public string Name => "SurfaceController";

    public void Start()
    {
        if (_started) return;
        _started = true;

        _router.Handle<SurfaceReadyParams, SurfaceInit>(Msg.SurfaceReady, (ctx, p) => Task.FromResult(Handshake(ctx, p)));
        _router.OnEvent<SurfaceResizeParams>(Msg.SurfaceResize, OnResize);
        _router.OnEvent<SurfaceShapeParams>(Msg.SurfaceShape, OnShape);
        _router.OnEvent<LogParams>(Msg.SurfaceLog, OnLog);

        _router.HandleNoParams<Protocol.Settings>(Msg.SettingsGet, _ => Task.FromResult(_settings.Current));
        _router.Handle<SettingsPatchParams, Protocol.Settings>(Msg.SettingsPatch, (ctx, p) => _settings.PatchAsync(p.Patch ?? throw ProtocolException.BadRequest("settings.patch: 'patch' is required"), ctx.Cancellation));
        _router.Handle<SettingsOpenParams>(Msg.SettingsOpenWindow, (_, p) =>
        {
            if (p.Page is not null && !SettingsPages.Contains(p.Page)) throw ProtocolException.BadRequest($"unknown settings page '{InputPolicy.Truncate(p.Page, 40)}'");
            _surfaces.ShowSettingsWindow(p.Page);
            return Task.CompletedTask;
        });

        _settingsTopic = _state.Declare<Protocol.Settings>(Msg.SettingsChanged, _ => _settings.Current);
        _themeTopic = _state.Declare<ThemeState>(Msg.ThemeChanged, _ => _theme.Current);
        _localeTopic = _state.Declare<LocaleState>(Msg.LocaleChanged, _ => new LocaleState { Locale = CurrentLocale() });
        _settings.Changed += OnSettingsChanged;
        _theme.Changed += OnThemeChanged;
    }

    public void Stop()
    {
        if (!_started) return;
        _started = false;
        _settings.Changed -= OnSettingsChanged;
        _theme.Changed -= OnThemeChanged;
        lock (_gate) _windowSizes.Clear();
    }

    // ---- handshake -------------------------------------------------------------------------------------------------------------------------------------

    private SurfaceInit Handshake(CallContext ctx, SurfaceReadyParams p)
    {
        // The identity is the host's assignment; the payload is informational and a mismatch is a protocol error (docs/IPC.md section 14).
        if (p.Surface != ctx.Surface.Kind) throw ProtocolException.BadRequest($"surface.ready: this channel is '{ctx.Surface.Kind}', not '{p.Surface}'");
        if (p.MonitorId is not null && !string.Equals(p.MonitorId, ctx.Surface.MonitorId, StringComparison.Ordinal))
            throw ProtocolException.BadRequest("surface.ready: monitorId does not match this window");

        var monitor = ResolveMonitor(ctx.Surface);
        var settings = _settings.Current;
        return new SurfaceInit
        {
            Surface = ctx.Surface.Kind,
            Monitor = monitor,
            Theme = _theme.Current,
            Settings = settings,
            Locale = LocaleResolver.Resolve(settings.General.Language, CultureInfo.CurrentUICulture.Name),
            HostVersion = _app.Version,
            ProtocolVersion = ProtocolInfo.Version,
            Features = settings.General.Features,
            GlassOverflow = _surfaces.SupportsGlassOverflow,
        };
    }

    /// <summary>The surface's monitor, or the primary one for surfaces that are not bound to a monitor. An unknown monitor id is a badRequest.</summary>
    private MonitorInfo ResolveMonitor(SurfaceId surface)
    {
        var all = _monitors.GetMonitors();
        if (surface.MonitorId is not null)
        {
            var found = all.FirstOrDefault(m => string.Equals(m.Id, surface.MonitorId, StringComparison.Ordinal));
            return found ?? throw ProtocolException.BadRequest("surface.ready: unknown monitor");
        }
        return all.FirstOrDefault(m => m.Primary) ?? all.FirstOrDefault() ?? new MonitorInfo();
    }

    // ---- window reports ---------------------------------------------------------------------------------------------------------------------------------

    private void OnResize(CallContext ctx, SurfaceResizeParams p)
    {
        if (ctx.Surface.Kind != SurfaceKind.Dock) return;
        var monitor = MonitorOf(ctx.Surface);
        if (monitor is null) return;
        var maxW = MonitorDip(monitor, horizontal: true);
        var maxH = MonitorDip(monitor, horizontal: false);
        if (!SurfaceGeometry.TryClampSize(p.Width, maxW, out var w) || !SurfaceGeometry.TryClampSize(p.Height, maxH, out var h))
        {
            _log.LogDebug("Dropped a non-finite or oversize dock resize {Width}x{Height}", p.Width, p.Height);
            return;
        }
        var bar = SurfaceGeometry.ClampBar(p.BarThickness, h);
        lock (_gate) _windowSizes[ctx.Surface] = (w, h);
        _surfaces.ResizeSurface(ctx.Surface, w, h, bar);
    }

    private void OnShape(CallContext ctx, SurfaceShapeParams p)
    {
        if (ctx.Surface.Kind is not (SurfaceKind.Dock or SurfaceKind.Menubar)) return;
        var (w, h) = WindowSize(ctx.Surface);
        var shape = new SurfaceShapeParams
        {
            Rects = SurfaceGeometry.ClampRects(p.Rects, w, h),
            HitRects = SurfaceGeometry.ClampRects(p.HitRects, w, h),
            Blur = p.Blur,
        };
        _surfaces.SetSurfaceShape(ctx.Surface, shape);
    }

    /// <summary>Size used to clamp shapes: the last dock resize, else the monitor's size (a menu bar spans its monitor).</summary>
    private (double Width, double Height) WindowSize(SurfaceId surface)
    {
        lock (_gate) if (_windowSizes.TryGetValue(surface, out var known)) return known;
        var monitor = MonitorOf(surface);
        if (monitor is null) return (0, 0);
        return (MonitorDip(monitor, horizontal: true), surface.Kind == SurfaceKind.Menubar ? MenuBarHeightDip : MonitorDip(monitor, horizontal: false));
    }

    private MonitorInfo? MonitorOf(SurfaceId surface)
    {
        if (surface.MonitorId is null) return null;
        return _monitors.GetMonitors().FirstOrDefault(m => string.Equals(m.Id, surface.MonitorId, StringComparison.Ordinal));
    }

    /// <summary>A monitor's size in DIPs (physical pixels / scale). Scale is clamped away from zero so a broken report cannot produce infinity.</summary>
    private static double MonitorDip(MonitorInfo m, bool horizontal)
    {
        var scale = double.IsFinite(m.Scale) && m.Scale > 0 ? m.Scale : 1;
        var px = horizontal ? m.BoundsPx.Width : m.BoundsPx.Height;
        return px / scale;
    }

    // ---- logging ----------------------------------------------------------------------------------------------------------------------------------------

    private void OnLog(CallContext ctx, LogParams p)
    {
        // Log injection and flooding: control characters are replaced and both parts are bounded (docs/IPC.md section 11). The rate limit lives in the router.
        var message = InputPolicy.Truncate(p.Message, MaxLogMessageChars);
        var data = p.Data is null ? "" : InputPolicy.Truncate(p.Data.ToJsonString(), MaxLogDataChars);
        _log.Log(ToLogLevel(p.Level), "UI {Surface}: {Message} {Data}", ctx.Surface.ToString(), message, data);
    }

    private static Microsoft.Extensions.Logging.LogLevel ToLogLevel(BetterWindows.Core.Protocol.LogLevel level) => level switch
    {
        BetterWindows.Core.Protocol.LogLevel.Debug => Microsoft.Extensions.Logging.LogLevel.Debug,
        BetterWindows.Core.Protocol.LogLevel.Info => Microsoft.Extensions.Logging.LogLevel.Information,
        BetterWindows.Core.Protocol.LogLevel.Warn => Microsoft.Extensions.Logging.LogLevel.Warning,
        _ => Microsoft.Extensions.Logging.LogLevel.Error,
    };

    // ---- state topics -----------------------------------------------------------------------------------------------------------------------------------

    private void OnSettingsChanged(Protocol.Settings _)
    {
        _settingsTopic?.Invalidate();
        _localeTopic?.Invalidate();
    }

    private void OnThemeChanged(ThemeState _) => _themeTopic?.Invalidate();

    private string CurrentLocale() => LocaleResolver.Resolve(_settings.Current.General.Language, CultureInfo.CurrentUICulture.Name);
}
