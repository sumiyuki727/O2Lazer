using System;
using System.Globalization;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Configuration;
using osu.Framework.IO.Stores;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.SongSelect;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
public class O2JamLocalisationBindingTest
{
    [TestCase("settings-caption")]
    [TestCase("settings-hint")]
    [TestCase("notification")]
    [TestCase("mod-description")]
    [TestCase("mod-setting-label")]
    [TestCase("mod-setting-value")]
    [TestCase("mod-mania-setting-value")]
    [TestCase("attribute-label")]
    [TestCase("attribute-description")]
    [TestCase("attribute-missing-description")]
    [TestCase("level-filter")]
    [TestCase("level-group")]
    public void ExistingUiTextFollowsNativeLanguageBindings(string surface)
    {
        using var config = new MemoryFrameworkConfig();
        using var manager = new LocalisationManager(config);
        using var english = new CultureStore("en");
        using var chinese = new CultureStore("zh-CN");
        using var german = new CultureStore("de-DE");
        manager.AddLanguage("en", english);
        manager.AddLanguage("zh-CN", chinese);
        manager.AddLanguage("de-DE", german);
        config.SetValue(FrameworkSetting.Locale, "en");
        var (text, key, arguments) = getText(surface);
        var binding = manager.GetLocalisedBindableString(text);
        try
        {
            // Keep the original value alive across switches, as loaded native controls do.
            // Recreating text for each culture would hide an early ToString() snapshot.
            string[] locales = ["en", "zh-CN", "de-DE", "en", "zh-CN"];
            foreach (var locale in locales)
            {
                config.SetValue(FrameworkSetting.Locale, locale);
                var culture = CultureInfo.GetCultureInfo(locale);
                var expected = string.Format(culture, O2LazerStrings.Catalog.Get(key, culture), arguments);
                Assert.That(binding.Value, Is.EqualTo(expected), surface + ":" + locale);
            }
        }
        finally
        {
            binding.UnbindAll();
        }
    }

    [Test]
    public void ReplacedAndReleasedBindingsUseNativeLifetime()
    {
        using var config = new MemoryFrameworkConfig();
        using var manager = new LocalisationManager(config);
        using var english = new CultureStore("en");
        using var chinese = new CultureStore("zh-CN");
        manager.AddLanguage("en", english);
        manager.AddLanguage("zh-CN", chinese);
        config.SetValue(FrameworkSetting.Locale, "zh-CN");
        var binding = manager.GetLocalisedBindableString(O2LazerStrings.ImportInitialising);
        try
        {
            binding.Text = O2LazerStrings.RefreshingProgress(3, 10);
            Assert.That(binding.Value, Is.EqualTo(manager.GetLocalisedString(O2LazerStrings.RefreshingProgress(3, 10))));
            config.SetValue(FrameworkSetting.Locale, "en");
            Assert.That(binding.Value, Is.EqualTo(O2LazerStrings.RefreshingProgress(3, 10).ToString()));
            binding.UnbindAll();
            var releasedValue = binding.Value;
            config.SetValue(FrameworkSetting.Locale, "zh-CN");
            Assert.That(binding.Value, Is.EqualTo(releasedValue), "Disposed UI must not keep a language subscription alive.");
        }
        finally
        {
            binding.UnbindAll();
        }
    }

    [TestCase("en-US")]
    [TestCase("zh-CN")]
    [TestCase("de-DE")]
    public void NativeStringContractsAndStoredNamesRemainCultureIndependent(string locale)
    {
        var previousCulture = CultureInfo.CurrentCulture;
        var previousUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(locale);
            var mod = new O2JamModManiaScore();
            mod.OverallDifficulty.Value = 7.5f;
            var attributes = O2JamBeatmapAttributes.Create(new BeatmapInfo()).ToArray();
            var collections = O2JamSourceFolderCollectionService.BuildPlans("D:/o2jam",
                [new O2JamSourceFolderBeatmap("D:/o2jam/Pack", "hash")]);
            string[] expectedAcronyms = ["o2ma", "SR", "LV"];
            Assert.Multiple(() =>
            {
                Assert.That(mod.Name, Is.EqualTo("Mania Score"));
                Assert.That(mod.Acronym, Is.EqualTo("MS"));
                Assert.That(mod.ExtendedIconInformation, Is.EqualTo("OD7.5"));
                Assert.That(new O2LazerRuleset().Description, Is.EqualTo("O2Jam"));
                Assert.That(attributes.Select(attribute => attribute.Acronym), Is.EqualTo(expectedAcronyms));
                Assert.That(O2LazerStrings.DifficultyName("EX", 75).ToString(), Is.EqualTo("EX Lv.75"));
                Assert.That(collections.Single().Name, Is.EqualTo("O2Lazer: Pack"));
                Assert.That(O2LazerStrings.SourceFolderCollectionPrefix.ToString(), Is.EqualTo("O2Lazer: "));
            });
        }
        finally
        {
            CultureInfo.CurrentCulture = previousCulture;
            CultureInfo.CurrentUICulture = previousUiCulture;
        }
    }

    private static (LocalisableString Text, string Key, object[] Arguments) getText(string surface)
    {
        var easy = new O2JamModEasy();
        easy.Retries.Value = 3;
        var maniaScore = new O2JamModManiaScore();
        maniaScore.OverallDifficulty.Value = 7.5f;
        var attribute = O2JamBeatmapAttributes.Create(new BeatmapInfo
        {
            StarRating = 3.25,
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.ManiaVersionTag },
        }).ElementAt(1);
        return surface switch
        {
            "settings-caption" => (O2LazerStrings.RefreshBeatmaps, "refresh_beatmaps", []),
            "settings-hint" => (O2LazerStrings.O2JamLongNoteVisualDescription, "o2jam_long_note_visual_description", []),
            "notification" => (O2LazerStrings.RefreshingProgress(3, 10), "refreshing_progress", [3, 10]),
            "mod-description" => (new O2JamModManiaScore().Description, "mod_mania_score_description", []),
            "mod-setting-label" => (easy.GetOrderedSettingsSourceProperties().Single().Item1.Label, "mod_easy_extra_lives", []),
            "mod-setting-value" => (easy.SettingDescription.Single().value, "mod_easy_extra_lives_value", [3]),
            "mod-mania-setting-value" => (maniaScore.SettingDescription.Single().value, "mania_score_difficulty_value", [7.5f]),
            "attribute-label" => (attribute.Label, "star_rating", []),
            "attribute-description" => (attribute.Description!.Value, "mania_star_rating_description", []),
            "attribute-missing-description" => (O2JamBeatmapAttributes.Create(new BeatmapInfo()).ElementAt(1).Description!.Value,
                "missing_mania_star_rating_description", []),
            "level-filter" => (O2JamLevelFilterPatch.FormatLevelTooltip(75), "level_badge", [75]),
            "level-group" => (O2LazerStrings.O2JamLevelGroupOver(150), "o2jam_level_group_over", [150]),
            _ => throw new ArgumentOutOfRangeException(nameof(surface)),
        };
    }

    private sealed class MemoryFrameworkConfig() : FrameworkConfigManager(null!)
    {
        protected override string Filename => null!;

        protected override void InitialiseDefaults()
        {
            SetDefault(FrameworkSetting.Locale, "en");
            SetDefault(FrameworkSetting.ShowUnicode, true);
        }
    }

    private sealed class CultureStore(string locale) : ResourceStore<string>, ILocalisationStore
    {
        public CultureInfo EffectiveCulture { get; } = CultureInfo.GetCultureInfo(locale);
    }
}
