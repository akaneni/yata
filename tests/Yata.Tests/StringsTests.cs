using System.Collections;
using System.Globalization;
using System.Resources;
using System.Text.RegularExpressions;
using Yata;
using Yata.Helpers;

namespace Yata.Tests;

public class StringsTests
{
    private static Dictionary<string, string> Read(CultureInfo culture)
    {
        // tryParents: false, so a missing Portuguese text isn't hidden by the English one.
        var set = Strings.ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)
            ?? throw new InvalidOperationException($"No resources for '{culture.Name}'");
        return set.Cast<DictionaryEntry>().ToDictionary(e => (string)e.Key, e => (string)e.Value!);
    }

    private static IEnumerable<string> Placeholders(string text) =>
        Regex.Matches(text, @"\{(\d+)").Select(m => m.Groups[1].Value).Distinct().Order();

    [Fact]
    public void EveryLanguageHasTheSameTexts()
    {
        var english = Read(CultureInfo.InvariantCulture);
        foreach (string language in Lang.Supported.Where(l => l != "en"))
        {
            var translated = Read(CultureInfo.GetCultureInfo(language));
            Assert.Empty(english.Keys.Except(translated.Keys)); // missing
            Assert.Empty(translated.Keys.Except(english.Keys)); // left over after a rename
            foreach (var (key, text) in english)
            {
                Assert.False(string.IsNullOrWhiteSpace(translated[key]), $"{language}: {key} is empty");
                Assert.True(Placeholders(text).SequenceEqual(Placeholders(translated[key])), $"{language}: {key} has other placeholders");
            }
        }
    }

    [Theory]
    [InlineData(null, "pt-BR", "pt-BR")]
    [InlineData(null, "pt-PT", "pt-BR")]
    [InlineData(null, "en-GB", "en")]
    [InlineData(null, "ja-JP", "en")]
    [InlineData("en", "pt-BR", "en")]
    [InlineData("pt-BR", "de-DE", "pt-BR")]
    [InlineData("fr", "pt-BR", "pt-BR")] // unknown choice (newer version's data): follow Windows
    public void LanguageFollowsWindowsUnlessChosen(string? choice, string windows, string expected) =>
        Assert.Equal(expected, Lang.Resolve(choice, CultureInfo.GetCultureInfo(windows)));
}
