namespace BetterWindows.Core.Config;

/// <summary>
/// Resolves settings.general.language ('auto' | 'en' | 'ru') to the effective UI locale ('en' | 'ru'), the value of SurfaceInit.locale and locale.changed.
/// Pure: the Windows UI language is passed in, so the rule is testable. Unknown or 'auto' values follow the system language; only 'ru' is non-English.
/// </summary>
public static class LocaleResolver
{
    public const string English = "en", Russian = "ru";

    /// <param name="language">The configured language (any string; anything but en/ru means 'auto').</param>
    /// <param name="systemLanguage">The system UI language tag, e.g. "ru-RU" (null/empty = unknown, treated as English).</param>
    public static string Resolve(string? language, string? systemLanguage)
    {
        if (language is English or Russian) return language;
        var primary = string.IsNullOrWhiteSpace(systemLanguage) ? "" : systemLanguage.Trim().Split('-')[0].ToLowerInvariant();
        return primary == Russian ? Russian : English;
    }
}
