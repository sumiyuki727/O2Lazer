using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania;
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
}
