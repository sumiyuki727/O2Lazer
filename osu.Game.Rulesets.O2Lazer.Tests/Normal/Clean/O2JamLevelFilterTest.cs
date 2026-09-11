using System;
using System.Collections.Generic;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamLevelFilterTest
{
    [Test]
    public void LevelAndStarRangesRemainIndependentAcrossProfileChanges()
    {
        var ruleset = new Bindable<RulesetInfo>(new O2LazerRuleset().RulesetInfo);
        var mods = new Bindable<IReadOnlyList<Mod>>([]);
        var starLower = createStarBindable(2, 0, 10);
        var starUpper = createStarBindable(8, 10.1, 10.1);
        var slider = new FilterControl.DifficultyRangeSlider();

        using var attachment = O2JamLevelFilterPatch.Attach(slider, ruleset, mods, starLower, starUpper);
        Assert.Multiple(() =>
        {
            Assert.That(((BindableNumber<double>)slider.LowerBound).MaxValue, Is.EqualTo(100));
            Assert.That(slider.UpperBound.Value, Is.EqualTo(101));
            Assert.That(slider.UpperBound.IsDefault, Is.True, "The slot after Lv.100 represents no limit.");
        });
        slider.LowerBound.Value = 75;
        slider.UpperBound.Value = 100;

        mods.Value = [new O2JamModManiaScore()];
        Assert.Multiple(() =>
        {
            Assert.That(slider.LowerBound.Value, Is.EqualTo(2));
            Assert.That(slider.UpperBound.Value, Is.EqualTo(8));
            Assert.That(((BindableNumber<double>)slider.LowerBound).MaxValue, Is.EqualTo(10));
            Assert.That(((BindableNumber<double>)slider.UpperBound).MaxValue, Is.EqualTo(10.1));
        });

        slider.LowerBound.Value = 3;
        mods.Value = [];
        Assert.Multiple(() =>
        {
            Assert.That(slider.LowerBound.Value, Is.EqualTo(75));
            Assert.That(slider.UpperBound.Value, Is.EqualTo(100));
            Assert.That(((BindableNumber<double>)slider.LowerBound).Precision, Is.EqualTo(1));
            Assert.That(starLower.Value, Is.EqualTo(3));
            Assert.That(O2JamLevelFilterPatch.FormatLevelTooltip(slider.LowerBound.Value).ToString(), Is.EqualTo("Lv.75"));
            Assert.That(O2JamLevelFilterPatch.FormatLevelNub(100).ToString(), Is.EqualTo("100"));
        });
    }

    [Test]
    public void LevelLowerBoundSurvivesRulesetRoundTripWithNativeRangeConstraints()
    {
        var o2Lazer = new O2LazerRuleset();
        var ruleset = new Bindable<RulesetInfo>(o2Lazer.RulesetInfo);
        var mods = new Bindable<IReadOnlyList<Mod>>([]);
        var starLower = createStarBindable(9, 0, 10);
        var starUpper = createStarBindable(10.1, 10.1, 10.1);
        var slider = new FilterControl.DifficultyRangeSlider();

        using var attachment = O2JamLevelFilterPatch.Attach(slider, ruleset, mods, starLower, starUpper);

        // These are the native ShearedRangeSlider constraints which run as each bound changes.
        slider.LowerBound.BindValueChanged(min =>
            slider.UpperBound.Value = Math.Max(min.NewValue + 1, slider.UpperBound.Value));
        slider.UpperBound.BindValueChanged(max =>
            slider.LowerBound.Value = Math.Min(max.NewValue - 1, slider.LowerBound.Value));

        slider.LowerBound.Value = 75;
        slider.UpperBound.Value = 100;
        ruleset.Value = new ManiaRuleset().RulesetInfo;
        ruleset.Value = o2Lazer.RulesetInfo;

        Assert.Multiple(() =>
        {
            Assert.That(slider.LowerBound.Value, Is.EqualTo(75));
            Assert.That(slider.UpperBound.Value, Is.EqualTo(100));
            Assert.That(starLower.Value, Is.EqualTo(9));
            Assert.That(starUpper.Value, Is.EqualTo(10.1));
        });
    }

    [Test]
    public void SliderRangeFiltersLevelWithoutFilteringStoredManiaStars()
    {
        var ruleset = new O2LazerRuleset();
        var criteria = new FilterCriteria
        {
            Ruleset = ruleset.RulesetInfo,
            RulesetCriteria = ruleset.CreateRulesetFilterCriteria(),
            Mods = [],
            UserStarDifficulty = new FilterCriteria.OptionalRange<double>
            {
                Min = 7.5,
                Max = 10,
                IsLowerInclusive = true,
                IsUpperInclusive = true,
            },
        };

        O2JamLevelFilterPatch.ApplyLevelRange(criteria, new FilterCriteria.OptionalRange<double>
        {
            Min = 75,
            Max = 100,
            IsLowerInclusive = true,
            IsUpperInclusive = true,
        });

        Assert.Multiple(() =>
        {
            Assert.That(criteria.UserStarDifficulty.HasFilter, Is.False);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(ruleset.RulesetInfo, 75, 1), criteria), Is.True);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(ruleset.RulesetInfo, 100, 20), criteria), Is.True);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(ruleset.RulesetInfo, 74, 8), criteria), Is.False);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(ruleset.RulesetInfo, 101, 8), criteria), Is.False);
        });
    }

    [Test]
    public void ManiaScoreKeepsNativeStarRange()
    {
        var ruleset = new O2LazerRuleset();
        var criteria = new FilterCriteria
        {
            Ruleset = ruleset.RulesetInfo,
            RulesetCriteria = ruleset.CreateRulesetFilterCriteria(),
            Mods = [new O2JamModManiaScore()],
            UserStarDifficulty = new FilterCriteria.OptionalRange<double>
            {
                Min = 3,
                IsLowerInclusive = true,
                IsUpperInclusive = true,
            },
        };

        O2JamLevelFilterPatch.ApplyLevelRange(criteria, new FilterCriteria.OptionalRange<double>
        {
            Min = 75,
            IsLowerInclusive = true,
            IsUpperInclusive = true,
        });

        Assert.That(criteria.UserStarDifficulty.Min, Is.EqualTo(3));
    }

    [Test]
    public void FilterRefreshesOnlyWhenManiaScoreStateChanges()
    {
        var filter = new O2JamFilterCriteria();
        var criteria = new FilterCriteria();
        var maniaScore = new O2JamModManiaScore();

        Assert.Multiple(() =>
        {
            Assert.That(filter.FilterMayChangeFromMods(criteria,
                new ValueChangedEvent<IReadOnlyList<Mod>>([], [new O2JamModEasy()])), Is.False);
            Assert.That(filter.FilterMayChangeFromMods(criteria,
                new ValueChangedEvent<IReadOnlyList<Mod>>([], [maniaScore])), Is.True);
            Assert.That(filter.FilterMayChangeFromMods(criteria,
                new ValueChangedEvent<IReadOnlyList<Mod>>([maniaScore], [])), Is.True);
            Assert.That(filter.FilterMayChangeFromMods(criteria,
                new ValueChangedEvent<IReadOnlyList<Mod>>([maniaScore], [maniaScore, new O2JamModEasy()])), Is.False);
        });
    }

    private static BindableDouble createStarBindable(double value, double defaultValue, double maximum)
    {
        var bindable = new BindableDouble(defaultValue) { MinValue = 0, MaxValue = maximum, Precision = 0.1 };
        bindable.Value = value;
        return bindable;
    }

    private static BeatmapInfo createBeatmap(RulesetInfo ruleset, ushort level, double stars) => new(ruleset,
        metadata: new BeatmapMetadata { Tags = $"o2jam o2lazer-level:{level}" })
    {
        DifficultyName = $"EX {level}",
        StarRating = stars,
    };
}
