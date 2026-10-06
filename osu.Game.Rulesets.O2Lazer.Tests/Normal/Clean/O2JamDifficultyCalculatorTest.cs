using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using NUnit.Framework;
using O2Jam.Core;
using O2Jam.Formats.Ojn;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Presentation.DifficultyLevels.Policy;
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
    public void MetadataOnlyModsDoNotChangeNativeStarsOrDecodeTheChart(double stars)
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
        var metadataOnly = calculator.Calculate([new O2JamModMirror(), new O2JamModNoRelease()]);
        var after = calculator.Calculate();

        Assert.Multiple(() =>
        {
            Assert.That(before.StarRating, Is.EqualTo(stars));
            Assert.That(metadataOnly.StarRating, Is.EqualTo(stars));
            Assert.That(metadataOnly.MaxCombo, Is.EqualTo(13));
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
        var beatmap = new OjnBeatmapFactory().Create(new OjnReader().Read(OjnTestData.CreateChart()), O2JamDifficulty.EX);
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
        Assert.That(source.DecodeCount, Is.EqualTo(1), "Switching MS must reuse the calculated baseline.");
    }

    [TestCase(false)]
    [TestCase(true)]
    public void DeferredProjectionCalculatesStarsAndManiaMaxComboOnDemand(bool nativeAlreadyWroteStars)
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new OjnBeatmapFactory().Create(new OjnReader().Read(OjnTestData.CreateChart()), O2JamDifficulty.EX);
        var expected = O2JamManiaStarRating.CalculateAttributes(beatmap, [], false);
        beatmap.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        beatmap.BeatmapInfo.Ruleset.LastAppliedDifficultyVersion = O2JamManiaStarRating.CacheVersion;
        beatmap.BeatmapInfo.StarRating = nativeAlreadyWroteStars ? expected.StarRating : -1;
        beatmap.BeatmapInfo.TotalObjectCount = 1;
        beatmap.BeatmapInfo.EndTimeObjectCount = 1;
        var plan = new O2JamImportPlan("", "", "", [], "", "", 0, "", "", "", 120, [], []);
        beatmap.Metadata.Tags = O2JamImportMetadata.Create(plan, new O2JamImportChart(O2JamDifficulty.EX, 5, "", 1000, 1, 1, -1, -1));
        var source = new PreparedWorkingBeatmap(beatmap);
        var calculator = ruleset.CreateDifficultyCalculator(source);
        var attributes = calculator.Calculate([new O2JamModManiaScore()]);
        Assert.That(attributes.StarRating, Is.EqualTo(expected.StarRating));
        Assert.That(attributes.MaxCombo, Is.EqualTo(expected.MaxCombo));
        Assert.That(source.DecodeCount, Is.EqualTo(1));
    }
    private sealed class PreparedWorkingBeatmap(IBeatmap beatmap) : FlatWorkingBeatmap(beatmap)
    {
        public int DecodeCount { get; private set; }

        public override IBeatmap GetPlayableBeatmap(IRulesetInfo ruleset, IReadOnlyList<Mod> mods, CancellationToken token)
        {
            DecodeCount++;
            return base.GetPlayableBeatmap(ruleset, mods, token);
        }
    }

    [Test]
    public void RateModsCalculateManiaStarsAndRestoreTheStoredBaseline()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new OjnBeatmapFactory().Create(new OjnReader().Read(OjnTestData.CreateChart()), O2JamDifficulty.EX);
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 250, Column = 1 });
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 500, Column = 2 });
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 750, Column = 3 });
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 1000, Column = 4 });
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 1250, Column = 5 });
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 1500, Column = 6 });
        var baseline = O2JamManiaStarRating.Calculate(beatmap);
        beatmap.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        beatmap.BeatmapInfo.DifficultyName = "EX Lv.5";
        beatmap.BeatmapInfo.StarRating = baseline;
        beatmap.Metadata.Tags = O2JamStarRatingMetadata.ManiaVersionTag;
        var source = new PreparedWorkingBeatmap(beatmap);
        var calculator = ruleset.CreateDifficultyCalculator(source);

        var noMod = calculator.Calculate();
        var doubleTime = calculator.Calculate([new O2JamModDoubleTime()]);
        var halfTime = calculator.Calculate([new O2JamModHalfTime()]);
        var restored = calculator.Calculate();

        Assert.Multiple(() =>
        {
            Assert.That(noMod.StarRating, Is.EqualTo(baseline).Within(1e-12));
            Assert.That(Math.Abs(doubleTime.StarRating - noMod.StarRating), Is.GreaterThan(1e-6));
            Assert.That(Math.Abs(halfTime.StarRating - noMod.StarRating), Is.GreaterThan(1e-6));
            Assert.That(Math.Abs(doubleTime.StarRating - halfTime.StarRating), Is.GreaterThan(1e-6));
            Assert.That(restored.StarRating, Is.EqualTo(noMod.StarRating).Within(1e-12));
            Assert.That(beatmap.BeatmapInfo.StarRating, Is.EqualTo(baseline), "A display calculation must not overwrite the persisted baseline.");
            Assert.That(source.DecodeCount, Is.EqualTo(3), "Incomplete test metadata decodes once for the baseline and once per rate mod.");
        });
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
