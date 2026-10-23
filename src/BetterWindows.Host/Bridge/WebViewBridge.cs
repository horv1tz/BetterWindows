using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using BetterWindows.Core.Ipc;
using BetterWindows.Core.Platform;
using BetterWindows.Core.Protocol;
using BetterWindows.Core.Services;
using BetterWindows.Host.Lifecycle;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace BetterWindows.Host.Bridge;

/// <summary>Per-surface view of the bridge for diagnostics and the smoke test.</summary>
/// <param name="Surface">The surface.</param>
/// <param name="Healthy">Not failed and not timed out.</param>
/// <param name="Ready">surface.ready completed for the current page.</param>
/// <param name="Delivered">Messages handed to the page.</param>
/// <param name="Pending">Messages waiting in the outbound queue.</param>
public sealed record SurfaceDiagnostics(SurfaceId Surface, bool Healthy, bool Ready, long Delivered, int Pending);

/// <summary>
/// The WebView2 bridge (docs/IPC.md sections 1, 1.1, 13, 16): ONE shared <see cref="CoreWebView2Environment"/> (InPrivate profile, user data folder from
/// <see cref="IAppPaths.WebView2Dir"/>), hardened per-surface settings, <c>https://app.local</c> served by the bridge itself through WebResourceRequested
/// (never <c>SetVirtualHostNameToFolderMapping</c>, which disables that event), origin-checked messages routed to <see cref="IMessageRouter"/> without awaiting, an
/// <see cref="ISurfaceChannel"/> per attachment registered in <see cref="SurfaceRegistry"/>, and the failure policy of section 16 reported through <see cref="Failed"/>.
/// All public members may be called from any thread; WebView2 objects are only touched on the UI thread.
/// </summary>
public sealed class WebViewBridge : IWebViewBridge, IDisposable
{
    private const int MaxTrackedFailures = 32;
    private static readonly TimeSpan UnresponsiveGrace = TimeSpan.FromSeconds(5);
    private static readonly TimeSpan InitTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan EnvironmentRetryAfterCrashLoop = TimeSpan.FromSeconds(60);

    private sealed class Attachment
    {
        public required SurfaceId Id { get; init; }
        public required IWebView2 View { get; init; }
        public required string Route { get; init; }
        public required string Url { get; init; }
        public required WebViewChannel Channel { get; init; }
        public required SurfaceHealth Health { get; init; }
        public CancellationTokenSource Cts { get; } = new();
        public CoreWebView2? Core;
        public List<Action> Cleanup { get; } = new();
        public volatile bool Retired;
        public bool ErrorPageArmed;
        public bool AwaitingFirstMessage = true;
        public string CommittedUrl = "";
        public int Generation;
        public long LastMessageStamp;
        public ITimer? ReadyTimer, UnresponsiveTimer;
    }

    private readonly IServiceProvider _services;
    private readonly SurfaceRegistry _registry;
    private readonly IMessageRouter _router;
    private readonly IAppPaths _paths;
    private readonly HostRuntimeOptions _options;
    private readonly TimeProvider _time;
    private readonly IUiScheduler _ui;
    private readonly ILogger _log;
    private readonly NavigationPolicy _policy;
    private readonly AppResourceServer _static;
    private readonly SlidingWindowLimiter _environmentRecreations;
    private readonly object _gate = new();
    private readonly Dictionary<SurfaceId, Attachment> _attachments = new();
    private readonly List<SurfaceFailure> _failures = new();
    private Task<CoreWebView2Environment>? _environmentTask;
    private int _generation;
    private long _giveUpUntilStamp;
    private volatile string _browserVersion = "unknown";
    private int _disposed;

    public WebViewBridge(IServiceProvider services, SurfaceRegistry registry, IMessageRouter router, IAppPaths paths, HostRuntimeOptions options,
        TimeProvider time, IUiScheduler ui, ILogger<WebViewBridge> log)
    {
        _services = services;
        _registry = registry;
        _router = router;
        _paths = paths;
        _options = options;
        _time = time;
        _ui = ui;
        _log = log;
        _policy = new NavigationPolicy(options.UiOrigin);
        _static = new AppResourceServer(Path.Combine(AppContext.BaseDirectory, "wwwroot"));
        _environmentRecreations = new SlidingWindowLimiter(1, TimeSpan.FromSeconds(60), time);
        _registry.SurfaceReady += OnSurfaceReady;
    }

    /// <inheritdoc />
    public event Action<SurfaceFailure>? Failed;

    /// <summary>Version of the WebView2 runtime in use ("unknown" until the environment exists). Feeds <see cref="IAppLifecycle.WebView2Version"/>.</summary>
    public string BrowserVersion => _browserVersion;

    /// <summary>The origin policy in force (production: app.local; dev: the --ui-url origin).</summary>
    public NavigationPolicy Policy => _policy;

    /// <summary>The bundled UI server (diagnostics report whether wwwroot has an index.html).</summary>
    public AppResourceServer StaticFiles => _static;

    /// <summary>Latest failures (newest last), for diag.report and the smoke test.</summary>
    public IReadOnlyList<SurfaceFailure> RecentFailures { get { lock (_gate) return _failures.ToArray(); } }

    /// <summary>One line per attached surface.</summary>
    public IReadOnlyList<SurfaceDiagnostics> DescribeSurfaces()
    {
        lock (_gate)
            return _attachments.Values.Select(a => new SurfaceDiagnostics(a.Id, a.Health.IsHealthy, a.Health.IsReady, a.Channel.Delivered, a.Channel.Pending)).ToArray();
    }

    /// <summary>How many events of <paramref name="type"/> were delivered to the page of <paramref name="id"/> (the smoke test checks <c>dock.state</c>).</summary>
    public int DeliveredEvents(SurfaceId id, string type)
    {
        lock (_gate) return _attachments.TryGetValue(id, out var a) ? a.Channel.DeliveredCount(type) : 0;
    }

    // ------------------------------------------------------------------ attach / detach

    public Task AttachAsync(IWebView2 webView, SurfaceId id, string route)
    {
        ArgumentNullException.ThrowIfNull(webView);
        var url = _policy.BuildUrl(route, id.MonitorId); // ArgumentException for a bad route: a programming error of the caller
        if (_ui.CheckAccess()) return AttachOnUiAsync(webView, id, route, url);

        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(() => _ = CompleteAsync(AttachOnUiAsync(webView, id, route, url), tcs));
        return tcs.Task;

        static async Task CompleteAsync(Task work, TaskCompletionSource tcs)
        {
            try { await work.ConfigureAwait(true); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        }
    }

    private async Task AttachOnUiAsync(IWebView2 webView, SurfaceId id, string route, string url)
    {
        var health = new SurfaceHealth(_time);
        Attachment? att = null;
        var channel = new WebViewChannel(id, _ui, ch => _registry.IsReady(ch), json => Deliver(() => att, json), _log, countTypes: _options.Smoke);
        att = new Attachment { Id = id, View = webView, Route = route, Url = url, Channel = channel, Health = health };

        Attachment? previous;
        lock (_gate)
        {
            _attachments.TryGetValue(id, out previous);
            _attachments[id] = att;
        }
        if (previous is not null) Retire(previous, unregister: false); // Register() below replaces and retires the old registry entry itself
        _registry.Register(channel);

        CoreWebView2Environment env;
        try { env = await GetEnvironmentAsync().ConfigureAwait(true); }
        catch (Exception ex)
        {
            var runtimeMissing = IsRuntimeMissing(ex);
            Fail(att, runtimeMissing ? SurfaceFailureKind.RuntimeMissing : SurfaceFailureKind.EnvironmentFailed, ex.Message, showErrorPage: false);
            return;
        }
        if (att.Retired) return;
        att.Generation = Volatile.Read(ref _generation);

        try
        {
            // Both must be set before the controller exists: transparent background (no white flash) and no file drop navigation.
            webView.DefaultBackgroundColor = System.Drawing.Color.Transparent;
            webView.AllowExternalDrop = false;
            if (webView.CoreWebView2 is null)
            {
                var controllerOptions = env.CreateCoreWebView2ControllerOptions();
                controllerOptions.IsInPrivateModeEnabled = true;
                var init = webView.EnsureCoreWebView2Async(env, controllerOptions);
                using var timeout = new CancellationTokenSource();
                var done = await Task.WhenAny(init, Task.Delay(InitTimeout, _time, timeout.Token)).ConfigureAwait(true);
                if (done != init) throw new TimeoutException("WebView2 did not initialise (the window may have no handle)");
                timeout.Cancel();
                await init.ConfigureAwait(true);
            }
        }
        catch (Exception ex)
        {
            Fail(att, IsRuntimeMissing(ex) ? SurfaceFailureKind.RuntimeMissing : SurfaceFailureKind.EnvironmentFailed, ex.Message, showErrorPage: false);
            return;
        }
        if (att.Retired || webView.CoreWebView2 is null) return;

        try
        {
            att.Core = webView.CoreWebView2;
            Configure(att, att.Core);
            _log.LogInformation("Attaching {Surface} to {Url} (protocol v{Protocol}, WebView2 {Version})", id, url, ProtocolInfo.Version, _browserVersion);
            att.Core.Navigate(url);
        }
        catch (Exception ex)
        {
            Fail(att, SurfaceFailureKind.NavigationFailed, ex.Message, showErrorPage: false);
        }
    }

    public void Detach(SurfaceId id)
    {
        if (!_ui.CheckAccess()) { _ui.Post(() => Detach(id)); return; } // WebView2 handlers and registry events are UI-thread only
        Attachment? att;
        lock (_gate)
        {
            if (!_attachments.Remove(id, out att)) return;
        }
        Retire(att, unregister: true);
    }

    public void Detach(SurfaceId id, IWebView2 webView)
    {
        ArgumentNullException.ThrowIfNull(webView);
        if (!_ui.CheckAccess()) { _ui.Post(() => Detach(id, webView)); return; }
        Attachment? att;
        lock (_gate)
        {
            if (!_attachments.TryGetValue(id, out att) || !ReferenceEquals(att.View, webView)) return; // a newer attachment owns this id now
            _attachments.Remove(id);
        }
        Retire(att, unregister: true);
    }

    /// <summary>Stops everything for an attachment: handlers off, timers off, channel closed; optionally unregisters ITS channel (a newer one with the same id is untouched).</summary>
    private void Retire(Attachment att, bool unregister)
    {
        att.Retired = true;
        try { att.Cts.Cancel(); } catch (ObjectDisposedException) { }
        SwapTimer(ref att.ReadyTimer, null);
        SwapTimer(ref att.UnresponsiveTimer, null);
        foreach (var undo in att.Cleanup)
        {
            try { undo(); } catch (Exception) { /* the control may already be disposed */ }
        }
        att.Cleanup.Clear();
        att.Channel.Close();
        if (unregister) _registry.Unregister(att.Channel);
    }

    public void OpenDevTools(SurfaceId id)
    {
        if (!_options.DevTools) { _log.LogWarning("OpenDevTools({Surface}) ignored: start with --devtools", id); return; }
        _ui.Post(() =>
        {
            try { Find(id)?.Core?.OpenDevToolsWindow(); }
            catch (Exception ex) { _log.LogWarning(ex, "Opening DevTools for {Surface} failed", id); }
        });
    }

    public bool IsHealthy(SurfaceId id) => Find(id)?.Health.IsHealthy ?? false;

    public Task ReloadAsync(SurfaceId id)
    {
        var att = Find(id);
        if (att is null) return Task.CompletedTask;
        if (att.Core is null) return AttachAsync(att.View, id, att.Route); // never initialised (environment failure): start over
        var tcs = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _ui.Post(() =>
        {
            try { ReloadOnUi(att); tcs.TrySetResult(); }
            catch (Exception ex) { tcs.TrySetException(ex); }
        });
        return tcs.Task;
    }

    private Attachment? Find(SurfaceId id)
    {
        lock (_gate) return _attachments.GetValueOrDefault(id);
    }

    private void ReloadOnUi(Attachment att)
    {
        if (att.Retired || att.Core is null) return;
        att.ErrorPageArmed = false;
        att.Health.OnNavigationStarted();
        att.Channel.SetFailed(false);
        att.Core.Navigate(att.Url);
    }

    // ------------------------------------------------------------------ environment

    private Task<CoreWebView2Environment> GetEnvironmentAsync()
    {
        lock (_gate)
        {
            if (_environmentTask is { IsFaulted: false, IsCanceled: false }) return _environmentTask;
            if (_time.GetTimestamp() < Interlocked.Read(ref _giveUpUntilStamp))
                return Task.FromException<CoreWebView2Environment>(new InvalidOperationException("the WebView2 browser process keeps crashing; not restarting it yet"));
            return _environmentTask = CreateEnvironmentAsync();
        }
    }

    private async Task<CoreWebView2Environment> CreateEnvironmentAsync()
    {
        // WEBVIEW2_DEFAULT_BACKGROUND_COLOR must exist before the first environment (the loader reads it); ProcessEnvironment normally did this at startup.
        Environment.SetEnvironmentVariable(ProcessEnvironment.WebView2BackgroundVar, ProcessEnvironment.TransparentBackground);
        Directory.CreateDirectory(_paths.WebView2Dir);
        // No AdditionalBrowserArguments: every Chromium switch is a support burden and none is needed for transparent, GPU-accelerated pages.
        var env = await CoreWebView2Environment.CreateAsync(null, _paths.WebView2Dir, new CoreWebView2EnvironmentOptions()).ConfigureAwait(true);
        _browserVersion = env.BrowserVersionString;
        _log.LogInformation("WebView2 runtime {Version}, user data folder {Folder} (InPrivate profile), protocol v{Protocol}", _browserVersion, _paths.WebView2Dir, ProtocolInfo.Version);
        return env;
    }

    /// <summary>The loader reports a missing Evergreen runtime in several ways depending on where it fails.</summary>
    internal static bool IsRuntimeMissing(Exception ex) =>
        ex is WebView2RuntimeNotFoundException or DllNotFoundException or BadImageFormatException
        || ex is COMException { HResult: unchecked((int)0x80070002) or unchecked((int)0x8007007E) }
        || ex.InnerException is not null && IsRuntimeMissing(ex.InnerException);

    // ------------------------------------------------------------------ configuration of one CoreWebView2

    private void Configure(Attachment att, CoreWebView2 core)
    {
        // docs/IPC.md section 16: lock everything down; the page is our own bundle but the UI is treated as untrusted.
        var s = core.Settings;
        s.AreHostObjectsAllowed = false;
        s.AreDefaultScriptDialogsEnabled = false;
        s.IsPasswordAutosaveEnabled = false;
        s.IsGeneralAutofillEnabled = false;
        s.IsSwipeNavigationEnabled = false;
        s.IsStatusBarEnabled = false;
        s.IsBuiltInErrorPageEnabled = false;
        s.AreDefaultContextMenusEnabled = false;
        s.IsZoomControlEnabled = false;
        s.IsPinchZoomEnabled = false;
        s.AreBrowserAcceleratorKeysEnabled = false;
        s.AreDevToolsEnabled = _options.DevTools;
        s.IsWebMessageEnabled = true;
        s.IsScriptEnabled = true;

        // Serve https://app.local/* (or the dev origin's /dyn/*) ourselves; everything else on the network is answered 403.
        var filters = _policy.ResourceFilters().Append("*").ToArray();
        foreach (var filter in filters)
        {
            core.AddWebResourceRequestedFilter(filter, CoreWebView2WebResourceContext.All);
            att.Cleanup.Add(() => core.RemoveWebResourceRequestedFilter(filter, CoreWebView2WebResourceContext.All));
        }

        void Sub<T>(Action<EventHandler<T>> add, Action<EventHandler<T>> remove, EventHandler<T> handler)
        {
            add(handler);
            att.Cleanup.Add(() => remove(handler));
        }

        Sub<CoreWebView2WebMessageReceivedEventArgs>(h => core.WebMessageReceived += h, h => core.WebMessageReceived -= h, (_, e) => OnWebMessage(att, e));
        Sub<CoreWebView2WebResourceRequestedEventArgs>(h => core.WebResourceRequested += h, h => core.WebResourceRequested -= h, (_, e) => OnWebResourceRequested(att, e));
        Sub<CoreWebView2NavigationStartingEventArgs>(h => core.NavigationStarting += h, h => core.NavigationStarting -= h, (_, e) => OnNavigationStarting(att, e, mainFrame: true));
        Sub<CoreWebView2NavigationStartingEventArgs>(h => core.FrameNavigationStarting += h, h => core.FrameNavigationStarting -= h, (_, e) => OnNavigationStarting(att, e, mainFrame: false));
        Sub<CoreWebView2NavigationCompletedEventArgs>(h => core.NavigationCompleted += h, h => core.NavigationCompleted -= h, (_, e) => OnNavigationCompleted(att, e));
        Sub<CoreWebView2NewWindowRequestedEventArgs>(h => core.NewWindowRequested += h, h => core.NewWindowRequested -= h, (_, e) => OnNewWindowRequested(att, e));
        Sub<CoreWebView2PermissionRequestedEventArgs>(h => core.PermissionRequested += h, h => core.PermissionRequested -= h, (_, e) => { e.State = CoreWebView2PermissionState.Deny; e.Handled = true; });
        Sub<CoreWebView2DownloadStartingEventArgs>(h => core.DownloadStarting += h, h => core.DownloadStarting -= h, (_, e) => { e.Cancel = true; e.Handled = true; });
        Sub<CoreWebView2ProcessFailedEventArgs>(h => core.ProcessFailed += h, h => core.ProcessFailed -= h, (_, e) => OnProcessFailed(att, e));
    }

    // ------------------------------------------------------------------ navigation

    private void OnNavigationStarting(Attachment att, CoreWebView2NavigationStartingEventArgs e, bool mainFrame)
    {
        if (att.Retired) return;
        var uri = e.Uri;
        var allowed = _policy.IsAllowedNavigation(uri) || (mainFrame && att.ErrorPageArmed && WebViewErrorPage.IsErrorDocument(uri));
        if (!allowed)
        {
            e.Cancel = true;
            _log.LogWarning("Blocked {Kind} navigation of {Surface} to {Uri}", mainFrame ? "main-frame" : "frame", att.Id, InputPolicy.Truncate(uri, 200));
            return;
        }
        if (!mainFrame) return;
        var sameDocument = StripFragment(uri) == StripFragment(att.CommittedUrl);
        if (sameDocument) return; // a #/route change inside the page must not drop queued messages
        att.CommittedUrl = uri;
        att.AwaitingFirstMessage = true;
        att.Channel.NavigationStarted();
        att.Health.OnNavigationStarted();
        ArmReadyTimer(att);
    }

    private void OnNavigationCompleted(Attachment att, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (att.Retired) return;
        att.Channel.NavigationCompleted();
        if (e.IsSuccess) return;
        if (att.ErrorPageArmed) { att.ErrorPageArmed = false; return; }
        Fail(att, SurfaceFailureKind.NavigationFailed, e.WebErrorStatus.ToString(), showErrorPage: true);
    }

    private void OnNewWindowRequested(Attachment att, CoreWebView2NewWindowRequestedEventArgs e)
    {
        // window.open / target=_blank never creates a window. A user-initiated http(s) link is passed to the platform opener, but only through the same validator
        // system.open uses (InputPolicy.IsSafeWebUrl); everything else is dropped.
        e.Handled = true;
        if (!e.IsUserInitiated || !InputPolicy.IsSafeWebUrl(e.Uri, out var safe)) { _log.LogDebug("Dropped new-window request from {Surface}", att.Id); return; }
        _ = Task.Run(() =>
        {
            try { _services.GetService<IAppLauncher>()?.Open(safe.AbsoluteUri); }
            catch (Exception ex) { _log.LogWarning(ex, "Opening a link from {Surface} failed", att.Id); }
        });
    }

    private static string StripFragment(string url)
    {
        var i = url.IndexOf('#');
        return i < 0 ? url : url[..i];
    }

    // ------------------------------------------------------------------ messages in

    private void OnWebMessage(Attachment att, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (att.Retired) return;
        // Only the bundle's own index.html may talk to the host: an injected frame, a redirected document or the error page is ignored.
        if (!_policy.IsAllowedMessageSource(e.Source))
        {
            _log.LogWarning("Ignored a web message from unexpected source {Source} on {Surface}", InputPolicy.Truncate(e.Source, 200), att.Id);
            return;
        }
        string json;
        try { json = e.TryGetWebMessageAsString(); }
        catch (ArgumentException) { _log.LogDebug("Ignored a non-string web message on {Surface}", att.Id); return; }
        if (json.Length > Limits.MaxMessageChars)
        {
            _log.LogWarning("Dropped an oversized web message ({Length} chars) on {Surface}", json.Length, att.Id);
            return;
        }
        Volatile.Write(ref att.LastMessageStamp, _time.GetTimestamp());
        if (att.AwaitingFirstMessage)
        {
            att.AwaitingFirstMessage = false;
            if (HasProtocolMismatch(json, out var version))
            {
                Fail(att, SurfaceFailureKind.ProtocolMismatch, $"page speaks protocol v{version}, host v{ProtocolInfo.Version}", showErrorPage: true);
                return;
            }
        }
        Dispatch(att, json);
    }

    /// <summary>The first message of a page load carries its protocol version; a different major version cannot be served.</summary>
    internal static bool HasProtocolMismatch(string json, out int version)
    {
        version = ProtocolInfo.Version;
        try
        {
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.ValueKind != JsonValueKind.Object || !doc.RootElement.TryGetProperty("v", out var v) || !v.TryGetInt32(out version)) return false;
            return version != ProtocolInfo.Version;
        }
        catch (JsonException) { return false; }
    }

    private void Dispatch(Attachment att, string json)
    {
        try
        {
            // Fire and forget: the router parses on this thread, runs handlers on the thread pool and posts the reply to THIS channel object itself.
            var task = _router.DispatchAsync(att.Channel, json, att.Cts.Token);
            if (!task.IsCompleted)
                _ = task.ContinueWith(t => _log.LogError(t.Exception!.GetBaseException(), "Dispatching a message from {Surface} failed", att.Id),
                    CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            else if (task.IsFaulted) _log.LogError(task.Exception!.GetBaseException(), "Dispatching a message from {Surface} failed", att.Id);
        }
        catch (Exception ex) { _log.LogError(ex, "Dispatching a message from {Surface} failed", att.Id); }
    }

    // ------------------------------------------------------------------ messages out

    /// <summary>UI thread: hand one JSON string to the page. False when the WebView is gone.</summary>
    private bool Deliver(Func<Attachment?> attachment, string json)
    {
        var att = attachment();
        if (att is null || att.Retired || att.Core is null) return false;
        try { att.Core.PostWebMessageAsString(json); return true; }
        catch (Exception ex) when (ex is InvalidOperationException or ObjectDisposedException or COMException)
        {
            _log.LogDebug(ex, "PostWebMessageAsString failed for {Surface}", att.Id);
            return false;
        }
    }

    // ------------------------------------------------------------------ resources (page + /dyn)

    private void OnWebResourceRequested(Attachment att, CoreWebView2WebResourceRequestedEventArgs e)
    {
        if (att.Retired || att.Core is null) return;
        var uri = e.Request.Uri;
        var env = att.Core.Environment;
        switch (_policy.Classify(uri))
        {
            case RequestDisposition.Ignore:
            case RequestDisposition.DevPassThrough:
                return;
            case RequestDisposition.Forbidden:
                _log.LogDebug("403 for {Uri} requested by {Surface}", InputPolicy.Truncate(uri, 160), att.Id);
                e.Response = env.CreateWebResourceResponse(null, 403, "Forbidden", AppHttpResponse.ErrorHeaders());
                return;
        }

        var method = e.Request.Method;
        if (!string.Equals(method, "GET", StringComparison.OrdinalIgnoreCase) && !string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase))
        {
            e.Response = env.CreateWebResourceResponse(null, 405, "Method Not Allowed", AppHttpResponse.ErrorHeaders());
            return;
        }

        var head = string.Equals(method, "HEAD", StringComparison.OrdinalIgnoreCase);
        var deferral = e.GetDeferral();
        _ = ServeAsync(att, e, deferral, uri, head);
    }

    private async Task ServeAsync(Attachment att, CoreWebView2WebResourceRequestedEventArgs e, CoreWebView2Deferral deferral, string uri, bool head)
    {
        try
        {
            CoreWebView2WebResourceResponse? response;
            if (_policy.Classify(uri) == RequestDisposition.Dynamic) response = await BuildDynResponseAsync(att, e, uri, head).ConfigureAwait(true);
            else response = await BuildStaticResponseAsync(att, e, uri, head).ConfigureAwait(true);
            if (!att.Retired) e.Response = response;
        }
        catch (Exception ex)
        {
            _log.LogError(ex, "Serving {Uri} to {Surface} failed", InputPolicy.Truncate(uri, 160), att.Id);
            try { if (!att.Retired) e.Response = att.Core!.Environment.CreateWebResourceResponse(null, 500, "Internal Server Error", AppHttpResponse.ErrorHeaders()); }
            catch (Exception) { /* the WebView went away while we were answering */ }
        }
        finally
        {
            try { deferral.Complete(); } catch (Exception) { /* same */ }
        }
    }

    private async Task<CoreWebView2WebResourceResponse> BuildStaticResponseAsync(Attachment att, CoreWebView2WebResourceRequestedEventArgs e, string uri, bool head)
    {
        var env = att.Core!.Environment;
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed)) return env.CreateWebResourceResponse(null, 404, "Not Found", AppHttpResponse.ErrorHeaders());
        var resource = await _static.TryGetAsync(parsed.AbsolutePath, att.Cts.Token).ConfigureAwait(true);
        if (resource is null) return env.CreateWebResourceResponse(null, 404, "Not Found", AppHttpResponse.ErrorHeaders());
        var body = head ? Array.Empty<byte>() : resource.Data;
        return env.CreateWebResourceResponse(new MemoryStream(body, writable: false), 200, "OK", AppHttpResponse.StaticHeaders(resource));
    }

    private async Task<CoreWebView2WebResourceResponse> BuildDynResponseAsync(Attachment att, CoreWebView2WebResourceRequestedEventArgs e, string uri, bool head)
    {
        var env = att.Core!.Environment;
        DynHttpResponse mapped;
        if (!DynResponse.TrySplit(uri, out var pathAndQuery)) mapped = DynResponse.NotFound();
        else
        {
            DynResource? resource = null;
            try
            {
                var provider = _services.GetService<IDynamicResourceProvider>();
                if (provider is not null) resource = await provider.GetAsync(pathAndQuery, att.Cts.Token).ConfigureAwait(true);
            }
            catch (OperationCanceledException) { /* the surface closed: answer 404 */ }
            catch (Exception ex) { _log.LogWarning(ex, "Resource provider failed for {Path}", InputPolicy.Truncate(pathAndQuery, 160)); }
            mapped = DynResponse.Map(resource);
        }
        var body = head || mapped.Body.Length == 0 ? null : new MemoryStream(mapped.Body, writable: false);
        return env.CreateWebResourceResponse(body, mapped.StatusCode, mapped.ReasonPhrase, mapped.Headers);
    }

    // ------------------------------------------------------------------ failures (docs/IPC.md section 16)

    private void ArmReadyTimer(Attachment att)
    {
        SwapTimer(ref att.ReadyTimer, _time.CreateTimer(_ => _ui.Post(() => OnReadyTimeout(att)), null, _options.ReadyTimeout, Timeout.InfiniteTimeSpan));
    }

    private void OnSurfaceReady(SurfaceId id)
    {
        var att = Find(id);
        if (att is null) return;
        SwapTimer(ref att.ReadyTimer, null);
        SwapTimer(ref att.UnresponsiveTimer, null);
        var wasUnhealthy = !att.Health.IsHealthy;
        att.Health.OnReady();
        att.Channel.SetFailed(false);
        if (wasUnhealthy) _log.LogInformation("Surface {Surface} recovered", id);
    }

    private void OnReadyTimeout(Attachment att)
    {
        if (att.Retired || att.Health.IsReady) return;
        att.Health.OnReadyTimeout();
        Fail(att, SurfaceFailureKind.ReadyTimeout, $"no surface.ready within {_options.ReadyTimeout.TotalSeconds:0} s", showErrorPage: true);
    }

    private void OnProcessFailed(Attachment att, CoreWebView2ProcessFailedEventArgs e)
    {
        if (att.Retired) return;
        var detail = $"{e.ProcessFailedKind}, reason {e.Reason}, exit code {e.ExitCode}, {InputPolicy.Truncate(e.ProcessDescription, 120)}";
        _log.LogWarning("WebView2 process failure on {Surface}: {Detail}", att.Id, detail);
        switch (e.ProcessFailedKind)
        {
            case CoreWebView2ProcessFailedKind.BrowserProcessExited:
                OnBrowserProcessExited(att, detail);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessExited:
                RecoverByReload(att, SurfaceFailureKind.RenderProcessExited, detail);
                break;
            case CoreWebView2ProcessFailedKind.GpuProcessExited:
                RecoverByReload(att, SurfaceFailureKind.GpuProcessExited, detail);
                break;
            case CoreWebView2ProcessFailedKind.RenderProcessUnresponsive:
                ArmUnresponsiveTimer(att, detail);
                break;
            default:
                break; // utility / sandbox helper / subframe renderers: Chromium restarts them, the page is unaffected
        }
    }

    /// <summary>Render or GPU process gone: reload the page (max 3 per 60 s), keeping the registry channel. The new page's surface.ready cancels pending calls.</summary>
    private void RecoverByReload(Attachment att, SurfaceFailureKind kind, string detail)
    {
        if (att.Health.TryBeginReload())
        {
            ReloadOnUi(att);
            RaiseFailed(att, kind, detail, recovered: true);
            return;
        }
        Fail(att, kind, detail + " (reload budget exhausted)", showErrorPage: true);
    }

    private void ArmUnresponsiveTimer(Attachment att, string detail)
    {
        var since = _time.GetTimestamp();
        SwapTimer(ref att.UnresponsiveTimer, _time.CreateTimer(_ => _ui.Post(() =>
        {
            // If the page talked to us in the meantime it is alive again.
            if (att.Retired || Volatile.Read(ref att.LastMessageStamp) > since) return;
            RecoverByReload(att, SurfaceFailureKind.RenderProcessUnresponsive, detail);
        }), null, UnresponsiveGrace, Timeout.InfiniteTimeSpan));
    }

    /// <summary>Replaces a timer slot atomically and disposes the previous timer (timers are armed on the UI thread and cancelled from pool threads).</summary>
    private static void SwapTimer(ref ITimer? slot, ITimer? replacement) => Interlocked.Exchange(ref slot, replacement)?.Dispose();

    /// <summary>
    /// The shared browser process died: every surface lost its page. The environment is dropped so the next AttachAsync creates a fresh one (once per minute at most), and
    /// EVERY surface is told to re-create its control and attach again.
    /// </summary>
    private void OnBrowserProcessExited(Attachment att, string detail)
    {
        List<Attachment> all;
        lock (_gate)
        {
            if (att.Generation != Volatile.Read(ref _generation)) return; // a sibling surface already handled this crash
            Interlocked.Increment(ref _generation);
            _environmentTask = null;
            all = _attachments.Values.ToList();
        }
        var allowed = _environmentRecreations.TryAcquire();
        if (!allowed)
        {
            Interlocked.Exchange(ref _giveUpUntilStamp, _time.GetTimestamp() + (long)(EnvironmentRetryAfterCrashLoop.TotalSeconds * _time.TimestampFrequency));
            detail = "gave up, crash loop: " + detail;
        }
        foreach (var a in all)
        {
            a.Health.MarkFailed();
            a.Channel.SetFailed(true);
        }
        foreach (var a in all) RaiseFailed(a, SurfaceFailureKind.BrowserProcessExited, detail, recovered: false);
    }

    /// <summary>Marks the surface failed, optionally replaces its page with the static error document, and reports it.</summary>
    private void Fail(Attachment att, SurfaceFailureKind kind, string detail, bool showErrorPage)
    {
        att.Health.MarkFailed();
        att.Channel.SetFailed(true);
        if (showErrorPage && att.Core is not null && !att.Retired)
        {
            try
            {
                att.ErrorPageArmed = true;
                att.Core.NavigateToString(WebViewErrorPage.Build(kind, detail));
            }
            catch (Exception ex)
            {
                att.ErrorPageArmed = false;
                _log.LogDebug(ex, "Could not show the error page for {Surface}", att.Id);
            }
        }
        RaiseFailed(att, kind, detail, recovered: false);
    }

    private void RaiseFailed(Attachment att, SurfaceFailureKind kind, string detail, bool recovered)
    {
        var failure = new SurfaceFailure(att.Id, kind, InputPolicy.Truncate(detail, 500), recovered);
        _log.LogError("Surface {Surface} failure {Kind}: {Detail} (recovered: {Recovered})", failure.Surface, failure.Kind, failure.Detail, failure.Recovered);
        lock (_gate)
        {
            _failures.Add(failure);
            if (_failures.Count > MaxTrackedFailures) _failures.RemoveAt(0);
        }
        var handlers = Failed;
        if (handlers is null) return;
        void Invoke()
        {
            foreach (var d in handlers.GetInvocationList())
            {
                try { ((Action<SurfaceFailure>)d)(failure); }
                catch (Exception ex) { _log.LogError(ex, "A SurfaceFailure subscriber threw"); }
            }
        }
        if (_ui.CheckAccess()) Invoke(); else _ui.Post(Invoke);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _registry.SurfaceReady -= OnSurfaceReady;
        List<Attachment> all;
        lock (_gate)
        {
            all = _attachments.Values.ToList();
            _attachments.Clear();
        }
        foreach (var a in all) Retire(a, unregister: true);
    }
}
