using System.Reflection;
using Microsoft.Extensions.Logging;
using Microsoft.Web.WebView2.Wpf;

namespace BetterWindows.Host.Surfaces;

/// <summary>The WebView control of a surface, as a WPF element and as the <see cref="IWebView2"/> the bridge attaches to.</summary>
internal sealed class WebViewHost : IDisposable
{
    public WebViewHost(System.Windows.FrameworkElement element, IWebView2 webView, bool isCompositionControl)
    {
        Element = element;
        WebView = webView;
        IsCompositionControl = isCompositionControl;
    }

    public System.Windows.FrameworkElement Element { get; }
    public IWebView2 WebView { get; }
    /// <summary>True for WebView2CompositionControl (renders into a WPF D3DImage: real per-pixel alpha, no airspace), false for the HWND-hosted classic control.</summary>
    public bool IsCompositionControl { get; }

    public void Dispose() => (Element as IDisposable)?.Dispose();
}

/// <summary>
/// Creates the WebView control of a surface.
///
/// WHY reflection: transparency over a DWM backdrop (dock/menu bar) and over a layered window (overlay) needs a control that renders INTO the WPF visual tree with alpha.
/// The classic <see cref="WebView2"/> is an HwndHost (a child HWND, "airspace": opaque to WPF's glass/alpha). <c>WebView2CompositionControl</c> renders the browser through
/// Windows.Graphics.Capture into a WPF D3DImage (alpha preserved) and is exactly what the WebView2 team provides for transparent WPF hosts, but in the pinned package
/// version (1.0.2792.45) the class is INTERNAL (public constructor, public members, implements the public IWebView2). It is therefore created through reflection; any
/// failure (type missing after a package update, constructor throws) falls back to the classic control, so the app still works, only without see-through glass.
/// Escape hatch: BETTERWINDOWS_WEBVIEW_HOST=classic forces the classic control (diagnosing rendering problems); =composition forces composition for the opaque surfaces too.
/// </summary>
internal static class WebViewFactory
{
    public const string EnvVar = "BETTERWINDOWS_WEBVIEW_HOST";
    private const string CompositionTypeName = "Microsoft.Web.WebView2.Wpf.WebView2CompositionControl";

    public static WebViewHost Create(bool preferComposition, ILogger log)
    {
        var mode = Environment.GetEnvironmentVariable(EnvVar)?.Trim().ToLowerInvariant();
        if (mode == "classic") preferComposition = false;
        else if (mode == "composition") preferComposition = true;

        if (preferComposition)
        {
            try
            {
                var type = typeof(WebView2).Assembly.GetType(CompositionTypeName, throwOnError: false);
                if (type is not null && Activator.CreateInstance(type) is System.Windows.FrameworkElement element && element is IWebView2 webView)
                {
                    type.GetProperty("DefaultBackgroundColor", BindingFlags.Public | BindingFlags.Instance)?.SetValue(element, System.Drawing.Color.Transparent);
                    return new WebViewHost(element, webView, true);
                }
                log.LogWarning("{Type} is not available in this WebView2 package; using the classic WebView2 control (no see-through glass)", CompositionTypeName);
            }
            catch (Exception ex)
            {
                log.LogWarning(ex, "Could not create {Type}; using the classic WebView2 control (no see-through glass)", CompositionTypeName);
            }
        }
        var classic = new WebView2 { DefaultBackgroundColor = System.Drawing.Color.Transparent };
        return new WebViewHost(classic, classic, false);
    }
}
