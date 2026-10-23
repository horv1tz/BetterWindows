namespace BetterWindows.Native.Interop;

/// <summary>
/// Feature gates for the status stream's undocumented Windows interfaces. A gate that is false (or a failed probe) makes the feature report
/// "unsupported"; it never falls back to an unsafe path.
/// </summary>
internal static class StatusFeatures
{
    /// <summary>Allows switching the default playback device through the undocumented IPolicyConfig. Disable to remove the feature entirely.</summary>
    public static readonly bool UndocumentedDefaultDevice = true;
}
