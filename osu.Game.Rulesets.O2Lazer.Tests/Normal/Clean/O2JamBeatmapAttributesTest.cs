using System.Linq;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Graphics.Shapes;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamBeatmapAttributesTest
{
    [Test]
    public void ReplacesDefaultAttributesWithIdentifierThenLevel()
    {
        var attributes = new O2LazerRuleset().GetBeatmapAttributesForDisplay(createBeatmap(), []).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(attributes.Select(attribute => attribute.Label), Is.EqualTo(new[] { O2LazerStrings.O2Ma, O2LazerStrings.O2JamLevel }));
            Assert.That(attributes.Select(attribute => attribute.Acronym), Is.EqualTo(new[] { "o2ma", "LV" }));
            Assert.That(attributes.Select(attribute => attribute.AdjustedValue), Is.EqualTo(new[] { 100, 75 }));
        });
    }

    [TestCase("o2jam o2ma100", "100")]
    [TestCase("o2jam O2MA1000", "1000")]
    [TestCase("\to2ma1234\n", "1234")]
    [TestCase("o2ma0", "0")]
    [TestCase("o2ma100-extra xo2ma123 o2ma200", "200")]
    public void IdentifierRendersWithAFullBar(string tags, string expected)
    {
        var beatmap = createBeatmap();
        beatmap.Metadata.Tags = tags;
        var attribute = new O2LazerRuleset().GetBeatmapAttributesForDisplay(beatmap, []).First();
        var (text, width) = render(attribute);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(width, Is.EqualTo(1));
        });
    }

    [TestCase("EX Lv.0", "0", 0)]
    [TestCase("EX Lv.30", "30", 0.2f)]
    [TestCase("NX 等级 75", "75", 0.5f)]
    [TestCase("HX 150", "150", 1)]
    [TestCase("HX 151", "151", 1)]
    [TestCase("HX 200", "200", 1)]
    [TestCase("HX 65535", "65535", 1)]
    public void LevelRendersProportionallyWithoutCappingTheDisplayedNumber(string difficultyName, string expected, float expectedWidth)
    {
        var beatmap = createBeatmap();
        beatmap.DifficultyName = difficultyName;
        var attribute = new O2LazerRuleset().GetBeatmapAttributesForDisplay(beatmap, [new O2JamModAutoplay()]).Last();
        var (text, width) = render(attribute);

        Assert.Multiple(() =>
        {
            Assert.That(text, Is.EqualTo(expected));
            Assert.That(width, Is.EqualTo(expectedWidth).Within(0.000001));
        });
    }

    [Test]
    public void MissingDifficultyLevelUsesTheExistingRatingFallback()
    {
        var beatmap = createBeatmap();
        beatmap.DifficultyName = "HX";
        beatmap.StarRating = 11.9;
        var attribute = new O2LazerRuleset().GetBeatmapAttributesForDisplay(beatmap, []).Last();

        Assert.That(render(attribute).Text, Is.EqualTo("119"));
    }

    private static BeatmapInfo createBeatmap() => new()
    {
        DifficultyName = "NX 75",
        StarRating = 1,
        Metadata = new BeatmapMetadata { Tags = "o2jam o2ma100" },
    };

    private static (string Text, float Width) render(RulesetBeatmapAttribute attribute)
    {
        using var statistic = new BeatmapTitleWedge.StatisticDifficulty { Value = new BeatmapTitleWedge.StatisticDifficulty.Data(attribute) };
        var type = typeof(BeatmapTitleWedge.StatisticDifficulty);

        // Verify osu!'s own rounding and bar clamping without copying its rendering formula.
        type.GetMethod("updateDisplay", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(statistic, null);
        statistic.FinishTransforms(true);
        var text = (OsuSpriteText)type.GetField("valueText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(statistic)!;
        var bar = (Circle)type.GetField("bar", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(statistic)!;
        return (text.Text.ToString(), bar.Width);
    }
}
