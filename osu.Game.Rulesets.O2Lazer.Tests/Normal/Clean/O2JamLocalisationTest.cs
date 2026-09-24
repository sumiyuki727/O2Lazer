using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Resources;
using System.Text;
using System.Text.RegularExpressions;
using NUnit.Framework;
using osu.Framework.IO.Stores;
using osu.Framework.Localisation;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamLocalisationTest
{
    [TestCase("zh")]
    [TestCase("zh-CN")]
    [TestCase("zh-TW")]
    public void ChineseLocalesUseExistingTranslation(string locale)
    {
        var expected = readResources(".zh")["refresh_beatmaps"];
        Assert.That(O2LazerStrings.Catalog.Get("refresh_beatmaps", CultureInfo.GetCultureInfo(locale)), Is.EqualTo(expected));
    }

    [TestCase("ja-JP")]
    [TestCase("en-GB")]
    public void UnsupportedLocaleFallsBackToEnglish(string locale) =>
        Assert.That(O2LazerStrings.Catalog.Get("refresh_beatmaps", CultureInfo.GetCultureInfo(locale)),
            Is.EqualTo(O2LazerStrings.Catalog.GetEnglish("refresh_beatmaps")));

    [Test]
    public void SameStringFollowsLanguageSwitchesAndFormatsNestedArguments()
    {
        LocalisableString text = O2LazerStrings.ManiaScoreDifficultyBadge(O2LazerStrings.ManiaScoreOverallDifficultyAcronym, 7.5f);
        using var chinese = new CultureStore("zh-CN");
        using var english = new CultureStore("en");
        using var german = new CultureStore("de-DE");
        var data = new ResourceLocalisableString(O2LazerStrings.Catalog, "mania_score_difficulty_badge",
            [O2LazerStrings.ManiaScoreOverallDifficultyAcronym, 7.5f]);
        foreach (var store in new[] { chinese, english, german, chinese })
        {
            var culture = store.EffectiveCulture;
            var expected = string.Format(culture, O2LazerStrings.Catalog.Get("mania_score_difficulty_badge", culture),
                O2LazerStrings.Catalog.Get("mania_score_overall_difficulty_acronym", culture), 7.5f);
            Assert.That(data.GetLocalised(LocalisationParameters.DEFAULT.With(store)), Is.EqualTo(expected));
        }
        Assert.That(data.GetLocalised(LocalisationParameters.DEFAULT), Is.EqualTo(text.ToString()));
    }

    [Test]
    public void AllShippedLanguagesMatchEnglishKeysAndPlaceholders()
    {
        var english = readResources("");
        Assert.That(english.Count, Is.GreaterThan(100));
        Assert.That(O2LazerStrings.Catalog.Languages, Does.Contain("zh"));
        foreach (var language in O2LazerStrings.Catalog.Languages)
        {
            var translation = readResources("." + language);
            Assert.That(translation.Keys, Is.EquivalentTo(english.Keys), language);
            foreach (var (key, value) in english)
            {
                Assert.That(translation[key], Is.Not.Empty, language + ":" + key);
                Assert.That(CompositeFormat.Parse(translation[key]).MinimumArgumentCount,
                    Is.EqualTo(CompositeFormat.Parse(value).MinimumArgumentCount), language + ":" + key);
                Assert.That(indices(translation[key]), Is.EquivalentTo(indices(value)), language + ":" + key);
            }
        }
    }

    [Test]
    public void ResourceOnlyLanguageSupportsRegionalAndPerKeyFallback()
    {
        var catalog = new EmbeddedLocalisationCatalog(typeof(O2JamLocalisationTest).Assembly, "Tests.Localisation.Messages");
        var culture = CultureInfo.GetCultureInfo("ja-JP");
        Assert.That(catalog.Languages, Is.EquivalentTo(new[] { "ja", "ja-JP" }));
        Assert.That(catalog.Get("regional", culture), Is.EqualTo("region"));
        Assert.That(catalog.Get("parent", culture), Is.EqualTo("parent"));
        Assert.That(catalog.Get("fallback", culture), Is.EqualTo("base"));
    }

    [Test]
    public void UnknownEnglishKeyFailsClearly() =>
        Assert.Throws<InvalidOperationException>(() => O2LazerStrings.Catalog.GetEnglish("not_a_real_key"));

    [Test]
    public void EqualityIncludesResourceIdentityAndArguments()
    {
        Assert.That(O2LazerStrings.LevelBadge(7), Is.EqualTo(O2LazerStrings.LevelBadge(7)));
        Assert.That(O2LazerStrings.LevelBadge(7).GetHashCode(), Is.EqualTo(O2LazerStrings.LevelBadge(7).GetHashCode()));
        Assert.That(O2LazerStrings.LevelBadge(7), Is.Not.EqualTo(O2LazerStrings.LevelBadge(8)));
    }

    private static IEnumerable<string> indices(string value) =>
        Regex.Matches(value, @"(?<!\{)\{(\d+)(?:[,}:])").Select(match => match.Groups[1].Value).Distinct();

    private static Dictionary<string, string> readResources(string suffix)
    {
        using var stream = typeof(O2LazerStrings).Assembly.GetManifestResourceStream(
            "osu.Game.Rulesets.O2Lazer.Resources.Localisation.O2LazerStrings" + suffix + ".resources")!;
        using var reader = new ResourceReader(stream);
        return reader.Cast<DictionaryEntry>().ToDictionary(entry => (string)entry.Key, entry => (string)entry.Value!);
    }

    private sealed class CultureStore(string culture) : ResourceStore<string>, ILocalisationStore
    {
        public CultureInfo EffectiveCulture { get; } = CultureInfo.GetCultureInfo(culture);
    }
}
