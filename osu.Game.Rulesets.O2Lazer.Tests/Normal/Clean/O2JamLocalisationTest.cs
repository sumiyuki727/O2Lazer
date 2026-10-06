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

    [TestCase("zh-CN", "300毫秒 (28.0速 · ×3)")]
    [TestCase("en", "300ms (speed 28.0 · ×3)")]
    public void ScrollSpeedUsesOneParenthesisGroupAndFollowsLanguageChanges(string locale, string expected)
    {
        using var store = new CultureStore(locale);
        var data = new ResourceLocalisableString(O2LazerStrings.Catalog, "scroll_speed_tooltip_with_o2jam_grade", [300, 28.0, 3.0]);
        Assert.That(O2LazerStrings.ScrollSpeedTooltipWithO2JamGrade(300, 28, 3), Is.EqualTo(new LocalisableString(data)));
        var text = data.GetLocalised(LocalisationParameters.DEFAULT.With(store));
        Assert.That(text, Is.EqualTo(expected));
        Assert.That(text.Count(character => character == '('), Is.EqualTo(1));
    }

    [TestCase("en")]
    [TestCase("zh-CN")]
    public void RandomAlgorithmNamesAndDescriptionsStayEnglish(string locale)
    {
        var culture = CultureInfo.GetCultureInfo(locale);
        string[] keys = ["random_algorithm", "random_algorithm_description", "random_algorithm_native", "random_algorithm_o2jam",
                         "random_algorithm_panic", "random_algorithm_r_random", "random_algorithm_s_random"];
        LocalisableString[] values = [O2LazerStrings.RandomAlgorithm, O2LazerStrings.RandomAlgorithmDescription, O2LazerStrings.RandomAlgorithmNative,
                                      O2LazerStrings.RandomAlgorithmO2Jam, O2LazerStrings.RandomAlgorithmPanic,
                                      O2LazerStrings.RandomAlgorithmRRandom, O2LazerStrings.RandomAlgorithmSRandom];
        for (var index = 0; index < keys.Length; index++)
        {
            var english = O2LazerStrings.Catalog.GetEnglish(keys[index]);
            Assert.That(O2LazerStrings.Catalog.Get(keys[index], culture), Is.EqualTo(english), keys[index]);
            Assert.That(values[index], Is.EqualTo(new LocalisableString(english)), keys[index]);
        }
        Assert.That(O2LazerStrings.Catalog.Get("random_algorithm", culture), Is.EqualTo("Algorithm"));
        Assert.That(O2LazerStrings.Catalog.Get("random_algorithm_panic", culture), Is.EqualTo("S-Random"));
        Assert.That(O2LazerStrings.Catalog.Get("random_algorithm_s_random", culture), Is.EqualTo("S-Random (legacy)"));
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
