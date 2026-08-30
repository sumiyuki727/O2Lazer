using System.IO;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamDifficultyCalculatorTest
{
    [Test]
    public void RulesetUsesO2JamLevelInsteadOfManiaStrain()
    {
        var ruleset = new O2LazerRuleset();
        var info = new BeatmapInfo
        {
            Ruleset = ruleset.RulesetInfo,
            BeatmapSet = new BeatmapSetInfo(),
            Metadata = new BeatmapMetadata(),
            DifficultyName = "HX Lv.119",
            StarRating = -1,
            TotalObjectCount = 3,
            EndTimeObjectCount = 0,
        };

        var calculator = ruleset.CreateDifficultyCalculator(new TestWorkingBeatmap(info));
        var attributes = calculator.Calculate();

        Assert.Multiple(() =>
        {
            Assert.That(calculator, Is.TypeOf<O2JamDifficultyCalculator>());
            Assert.That(attributes.StarRating, Is.EqualTo(11.9).Within(0.000001));
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

    private sealed class TestWorkingBeatmap(BeatmapInfo info) : WorkingBeatmap(info, null!)
    {
        protected override IBeatmap GetBeatmap() => throw new AssertionException("Difficulty calculation decoded the source chart.");

        public override Texture GetBackground() => null!;

        protected override Track GetBeatmapTrack() => null!;

        protected override ISkin GetSkin() => null!;

        public override Stream GetStream(string storagePath) => Stream.Null;
    }
}
