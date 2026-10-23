using BetterWindows.Core.Config;
using Xunit;

namespace BetterWindows.Core.Tests.Config;

public class LocaleResolverTests
{
    [Theory]
    [InlineData("en", "ru-RU", "en")]
    [InlineData("ru", "en-US", "ru")]
    [InlineData("ru", null, "ru")]
    public void Explicit_languages_win_over_the_system(string language, string? system, string expected) =>
        Assert.Equal(expected, LocaleResolver.Resolve(language, system));

    [Theory]
    [InlineData("auto", "ru-RU", "ru")]
    [InlineData("auto", "RU", "ru")]
    [InlineData("auto", "de-DE", "en")]
    [InlineData("auto", "", "en")]
    [InlineData("auto", null, "en")]
    [InlineData("fr", "ru-RU", "ru")]
    [InlineData("", "ru", "ru")]
    [InlineData(null, "en-GB", "en")]
    public void Auto_and_unknown_values_follow_the_system_language(string? language, string? system, string expected) =>
        Assert.Equal(expected, LocaleResolver.Resolve(language, system));

    [Fact]
    public void Only_english_and_russian_are_ever_returned_for_unsupported_system_languages()
    {
        foreach (var sys in new[] { "fr-FR", "zh-Hans-CN", "  ", "\t" }) Assert.Equal("en", LocaleResolver.Resolve("auto", sys));
    }
}
