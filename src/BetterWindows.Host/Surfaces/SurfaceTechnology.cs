namespace BetterWindows.Host.Surfaces;

/// <summary>
/// Runtime switch between the two window technologies for the dock and menu bar (see the decision record in <see cref="SurfaceWindowBase"/>).
/// Default: non-layered window + DWM system backdrop + window region. BETTERWINDOWS_SURFACE_TECH=layered builds them as layered (per-pixel alpha) windows WITHOUT a
/// backdrop - the escape hatch if a real Windows 11 desktop shows that the backdrop does not coexist with the region/WebView2 on some build.
/// </summary>
internal static class SurfaceTechnology
{
    public const string EnvVar = "BETTERWINDOWS_SURFACE_TECH";

    public static bool UseLayeredForBars { get; } = string.Equals(Environment.GetEnvironmentVariable(EnvVar)?.Trim(), "layered", StringComparison.OrdinalIgnoreCase);
}
