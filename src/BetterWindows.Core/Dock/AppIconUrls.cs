using BetterWindows.Core.Protocol;

namespace BetterWindows.Core.Dock;

/// <summary>
/// Which <c>/dyn/icon/*</c> URL represents an app or window. Catalog apps are addressed by id; apps the catalog does not know (portable exes, elevated or
/// framed windows) use the icon of one of their windows, which is the only thing the shell can always produce.
/// </summary>
public static class AppIconUrls
{
    /// <summary>The largest magnified dock icon is 192 DIP x devicePixelRatio, so dock and running icons are requested at 256 (docs: ADDENDUM v1.1).</summary>
    public const int DockIconPx = 256;

    public static string ForApp(string appId, bool inCatalog, long fallbackWindow, int size = DockIconPx) =>
        inCatalog || fallbackWindow == 0 ? DynUrls.AppIcon(appId, size) : DynUrls.WindowIcon(fallbackWindow, size);
}
