using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.UI;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamModSelectAttributesTest
{
    [Test]
    public void ModSelectOmitsAllDifficultyAttributesForO2Lazer()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo)
        {
            DifficultyName = "EX Lv.75",
            StarRating = 3.25,
            Metadata = new BeatmapMetadata { Tags = "o2ma100" },
        };

        var attributes = O2JamModSelectAttributesPatch.GetAttributes(ruleset, beatmap, []).ToArray();
        Assert.Multiple(() =>
        {
            Assert.That(O2JamModSelectAttributesPatch.IsInstalled, Is.True);
            Assert.That(attributes, Is.Empty);
        });
    }

    [Test]
    public void OtherRulesetsKeepNativeAttributes()
    {
        _ = new O2LazerRuleset();
        var ruleset = new ManiaRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo);

        var expected = ruleset.GetBeatmapAttributesForDisplay(beatmap, []).ToArray();
        var actual = O2JamModSelectAttributesPatch.GetAttributes(ruleset, beatmap, []).ToArray();
        Assert.That(actual.Select(attribute => (attribute.Label.ToString(), attribute.Acronym, attribute.OriginalValue)),
            Is.EqualTo(expected.Select(attribute => (attribute.Label.ToString(), attribute.Acronym, attribute.OriginalValue))));
    }

    [Test]
    public void ManiaScoreShowsOnlyOdAndHpFromSeven()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new BeatmapInfo(ruleset.RulesetInfo, new BeatmapDifficulty
        {
            OverallDifficulty = 2,
            DrainRate = 4,
            CircleSize = 7,
        });
        var maniaScore = new O2JamModManiaScore();

        var defaults = O2JamModSelectAttributesPatch.GetAttributes(ruleset, beatmap, [maniaScore]).ToArray();
        maniaScore.OverallDifficulty.Value = 8.5f;
        maniaScore.DrainRate.Value = 6.5f;
        var adjusted = O2JamModSelectAttributesPatch.GetAttributes(ruleset, beatmap, [maniaScore]).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(defaults.Select(attribute => attribute.Acronym), Is.EqualTo(new[] { "OD", "HP" }));
            Assert.That(defaults.Select(attribute => attribute.OriginalValue), Is.EqualTo(new[] { 7, 7 }));
            Assert.That(defaults.Select(attribute => attribute.AdjustedValue), Is.EqualTo(new[] { 7, 7 }));
            Assert.That(adjusted.Select(attribute => attribute.AdjustedValue), Is.EqualTo(new[] { 8.5f, 6.5f }));
        });
    }
}
