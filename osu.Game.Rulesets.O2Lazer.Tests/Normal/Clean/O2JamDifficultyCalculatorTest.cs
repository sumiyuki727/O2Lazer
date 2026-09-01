using System.IO;
using System.Collections.Generic;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamDifficultyCalculatorTest
{
    [Test]
    public void NativeCalculatorKeepsManiaStarsForPersistence()
    {
        var ruleset = new O2LazerRuleset();
        var info = new BeatmapInfo
        {
            Ruleset = ruleset.RulesetInfo,
            BeatmapSet = new BeatmapSetInfo(),
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.ManiaVersionTag },
            DifficultyName = "HX Lv.119",
            StarRating = 3.25,
            TotalObjectCount = 3,
            EndTimeObjectCount = 0,
        };

        var calculator = ruleset.CreateDifficultyCalculator(new TestWorkingBeatmap(info));
        var attributes = calculator.Calculate();

        Assert.Multiple(() =>
        {
            Assert.That(calculator, Is.TypeOf<O2JamDifficultyCalculator>());
            Assert.That(attributes.StarRating, Is.EqualTo(3.25));
            Assert.That(calculator.Version, Is.GreaterThan(260829));
            Assert.That(attributes.MaxCombo, Is.EqualTo(2));
        });
    }

    [TestCase("EX Lv.41", 41)]
    [TestCase("NX 等级 105", 105)]
    [TestCase("HX 119", 119)]
    public void ReadsLevelFromLocalisedDifficultyName(string difficultyName, int expected)
    {
        Assert.That(O2JamDifficultyRating.TryParseLevel(difficultyName, out var level), Is.True);
        Assert.That(level, Is.EqualTo(expected));
    }

    [TestCase(0)]
    [TestCase(4.123456789012345)]
    public void ChangingDisplayModsDoesNotChangeNativeStarsOrDecodeTheChart(double stars)
    {
        var ruleset = new O2LazerRuleset();
        var info = new BeatmapInfo
        {
            Ruleset = ruleset.RulesetInfo,
            Metadata = new BeatmapMetadata { Tags = O2JamStarRatingMetadata.ManiaVersionTag },
            DifficultyName = "HX Lv.119",
            StarRating = stars,
            TotalObjectCount = 10,
            EndTimeObjectCount = 4,
        };
        var calculator = ruleset.CreateDifficultyCalculator(new TestWorkingBeatmap(info));
        var before = calculator.Calculate();
        var mania = calculator.Calculate([new O2JamModManiaScore(), new O2JamModMirror()]);
        var after = calculator.Calculate();

        Assert.Multiple(() =>
        {
            Assert.That(before.StarRating, Is.EqualTo(stars));
            Assert.That(mania.StarRating, Is.EqualTo(stars));
            Assert.That(mania.MaxCombo, Is.EqualTo(13));
            Assert.That(after.StarRating, Is.EqualTo(before.StarRating));
            Assert.That(info.StarRating, Is.EqualTo(stars));
            Assert.That(info.Metadata.Tags, Is.EqualTo(O2JamStarRatingMetadata.ManiaVersionTag));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void LegacyValuesAndNativeVersionResetsRecalculateMania(bool reset)
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new OjnBeatmapFactory().Create(new OjnReader().Read(OjnReaderTest.CreateChart()), O2JamDifficulty.EX);
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 1500, Column = 1 });
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 1750, Column = 2 });
        var expected = O2JamManiaStarRating.Calculate(beatmap);
        Assert.That(expected, Is.GreaterThan(0));
        beatmap.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        beatmap.BeatmapInfo.DifficultyName = "EX Lv.5";
        beatmap.BeatmapInfo.StarRating = reset ? -1 : 0.5;
        if (reset)
            beatmap.Metadata.Tags = O2JamStarRatingMetadata.ManiaVersionTag;
        var source = new PreparedWorkingBeatmap(beatmap);
        var calculator = ruleset.CreateDifficultyCalculator(source);
        Assert.That(calculator.Calculate().StarRating, Is.EqualTo(expected));
        Assert.That(calculator.Calculate([new O2JamModManiaScore()]).StarRating, Is.EqualTo(expected));
        Assert.That(source.DecodeCount, Is.EqualTo(1));
    }

    private sealed class PreparedWorkingBeatmap(IBeatmap beatmap) : FlatWorkingBeatmap(beatmap)
    {
        public int DecodeCount { get; private set; }

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            DecodeCount++;
            return Beatmap;
        }
    }

    private sealed class TestWorkingBeatmap(BeatmapInfo info) : WorkingBeatmap(info, null!)
    {
        protected override IBeatmap GetBeatmap() => throw new AssertionException("Difficulty calculation decoded the source chart.");

        public override Texture GetBackground() => null!;

        protected override Track GetBeatmapTrack() => null!;

        protected override ISkin GetSkin() => null!;

        public override Stream GetStream(string storagePath) => Stream.Null;
    }
}
