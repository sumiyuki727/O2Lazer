using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamModTest
{
    [TestCase(ModType.DifficultyReduction)]
    [TestCase(ModType.DifficultyIncrease)]
    [TestCase(ModType.Conversion)]
    [TestCase(ModType.Fun)]
    public void MatchesManiaPresentationOrderingAndMultipliers(ModType type)
    {
        var ruleset = new O2LazerRuleset();
        var mania = new ManiaRuleset();
        var actual = ModUtils.FlattenMods(ruleset.GetModsFor(type))
                            .Where(mod => mod is O2JamModNoFail or O2JamModRandom or O2JamModMirror or O2JamModNoRelease
                                or O2JamModHalfTime or O2JamModDaycore or O2JamModSuddenDeath or O2JamModPerfect
                                or O2JamModDoubleTime or O2JamModNightcore or O2JamModFadeIn or O2JamModHidden
                                or O2JamModCover or O2JamModFlashlight or O2JamModAccuracyChallenge
                                or O2JamModInvert or O2JamModConstantSpeed or O2JamModWindUp or O2JamModWindDown
                                or O2JamModMuted or O2JamModAdaptiveSpeed)
                            .ToArray();
        var expected = ModUtils.FlattenMods(mania.GetModsFor(type))
                            .Where(mod => mod is ManiaModNoFail or ManiaModRandom or ManiaModMirror or ManiaModNoRelease
                                or ManiaModHalfTime or ManiaModDaycore or ManiaModSuddenDeath or ManiaModPerfect
                                or ManiaModDoubleTime or ManiaModNightcore or ManiaModFadeIn or ManiaModHidden
                                or ManiaModCover or ManiaModFlashlight or ModAccuracyChallenge
                                or ManiaModInvert or ManiaModConstantSpeed or ModWindUp or ModWindDown
                                or ManiaModMuted or ModAdaptiveSpeed)
                            .ToArray();
        var context = new ScoreMultiplierContext(new BeatmapDifficulty());

        Assert.That(actual.Select(mod => mod.Acronym), Is.EqualTo(expected.Select(mod => mod.Acronym)));
        foreach (var (mod, native) in actual.Zip(expected))
        {
            Assert.Multiple(() =>
            {
                Assert.That(mod.Name, Is.EqualTo(native.Name));
                Assert.That(mod.Description.ToString(), Is.EqualTo(native.Description.ToString()));
                Assert.That(mod.Icon, Is.EqualTo(native.Icon));
                Assert.That(mod.Ranked, Is.EqualTo(native.Ranked));
                Assert.That(mod.IncompatibleMods, Is.EqualTo(native.IncompatibleMods));
                Assert.That(ruleset.CreateScoreMultiplierCalculator(context).CalculateFor([mod]),
                    Is.EqualTo(mania.CreateScoreMultiplierCalculator(context).CalculateFor([native])));
            });
        }

        var actualAcronyms = actual.Select(mod => mod.Acronym).ToHashSet();
        var expectedAcronyms = expected.Select(mod => mod.Acronym).ToHashSet();
        var actualGroups = ruleset.GetModsFor(type)
                                  .Where(entry => ModUtils.FlattenMods([entry]).Any(mod => actualAcronyms.Contains(mod.Acronym)))
                                  .Select(entry => string.Join('/', ModUtils.FlattenMods([entry]).Select(mod => mod.Acronym)));
        var expectedGroups = mania.GetModsFor(type)
                                  .Where(entry => ModUtils.FlattenMods([entry]).Any(mod => expectedAcronyms.Contains(mod.Acronym)))
                                  .Select(entry => string.Join('/', ModUtils.FlattenMods([entry]).Select(mod => mod.Acronym)));
        Assert.That(actualGroups, Is.EqualTo(expectedGroups));
    }

    [TestCase(typeof(O2JamModNoRelease), typeof(ManiaModNoRelease))]
    [TestCase(typeof(O2JamModFadeIn), typeof(ManiaModFadeIn))]
    [TestCase(typeof(O2JamModHidden), typeof(ManiaModHidden))]
    [TestCase(typeof(O2JamModCover), typeof(ManiaModCover))]
    [TestCase(typeof(O2JamModFlashlight), typeof(ManiaModFlashlight))]
    [TestCase(typeof(O2JamModAccuracyChallenge), typeof(ModAccuracyChallenge))]
    [TestCase(typeof(O2JamModInvert), typeof(ManiaModInvert))]
    [TestCase(typeof(O2JamModWindUp), typeof(ModWindUp))]
    [TestCase(typeof(O2JamModWindDown), typeof(ModWindDown))]
    [TestCase(typeof(O2JamModMuted), typeof(ManiaModMuted))]
    [TestCase(typeof(O2JamModAdaptiveSpeed), typeof(ModAdaptiveSpeed))]
    public void SettingsMenuMatchesMania(Type modType, Type maniaType)
    {
        var mod = (Mod)Activator.CreateInstance(modType)!;
        var mania = (Mod)Activator.CreateInstance(maniaType)!;

        Assert.That(mod.GetOrderedSettingsSourceProperties().Select(setting =>
                (setting.Item2.Name, setting.Item1.Label.ToString(), setting.Item1.Description.ToString(), setting.Item1.SettingControlType)),
            Is.EqualTo(mania.GetOrderedSettingsSourceProperties().Select(setting =>
                (setting.Item2.Name, setting.Item1.Label.ToString(), setting.Item1.Description.ToString(), setting.Item1.SettingControlType))));
    }

    [Test]
    public void NoReleasePreservesO2JamHoldObjectsAndEndpointMetadata()
    {
        var ruleset = new O2LazerRuleset();
        var source = createBeatmap(ruleset);
        var playable = (O2JamBeatmap)new FlatWorkingBeatmap(source).GetPlayableBeatmap(
            ruleset.RulesetInfo, [new O2JamModNoRelease()], default);

        Assert.That(playable.HitObjects, Has.Count.EqualTo(source.HitObjects.Count));
        foreach (var hold in playable.HitObjects.OfType<O2JamHoldNote>())
        {
            Assert.Multiple(() =>
            {
                Assert.That(hold.ReleaseTimingDisabled, Is.True);
                Assert.That(hold.Tail, Is.TypeOf<O2JamHoldTail>());
                Assert.That(((O2JamHoldTail)hold.Tail).ReleaseTimingDisabled, Is.True);
                Assert.That(((O2JamHoldTail)hold.Tail).ChartPosition, Is.EqualTo(hold.TailChartPosition));
                Assert.That(hold.Head.Samples.Single(), Is.TypeOf<O2JamHitSampleInfo>());
                Assert.That(hold.Tail.Samples, Is.Empty);
            });
        }
    }

    [Test]
    public void InvertCreatesOnlyO2JamHoldsWithoutMutatingTheSource()
    {
        var ruleset = new O2LazerRuleset();
        var source = createBeatmap(ruleset);
        var sourceObjects = source.HitObjects.ToArray();
        var playable = (O2JamBeatmap)new FlatWorkingBeatmap(source).GetPlayableBeatmap(
            ruleset.RulesetInfo, [new O2JamModInvert()], default);

        Assert.Multiple(() =>
        {
            Assert.That(playable.HitObjects, Has.Count.EqualTo(O2JamBeatmap.ColumnCount));
            Assert.That(playable.HitObjects, Has.All.TypeOf<O2JamHoldNote>());
            Assert.That(playable.AutomaticAudioEvents, Is.EqualTo(source.AutomaticAudioEvents));
            Assert.That(playable.MeasureLineTimes, Is.EqualTo(source.MeasureLineTimes));
            Assert.That(source.HitObjects, Is.EqualTo(sourceObjects));
        });

        foreach (var hold in playable.HitObjects.Cast<O2JamHoldNote>())
        {
            Assert.Multiple(() =>
            {
                Assert.That(hold.Duration, Is.EqualTo(125));
                Assert.That(hold.Head, Is.TypeOf<O2JamHoldHead>());
                Assert.That(hold.Tail, Is.TypeOf<O2JamHoldTail>());
                Assert.That(hold.HeadChartPosition, Is.EqualTo(playable.TimingMap.PositionAt(hold.StartTime)));
                Assert.That(hold.TailChartPosition, Is.EqualTo(playable.TimingMap.PositionAt(hold.EndTime)));
                Assert.That(hold.Head.Samples.Single(), Is.TypeOf<O2JamHitSampleInfo>());
                Assert.That(hold.Tail.Samples, Is.Empty);
            });
        }
    }

    [TestCase(typeof(O2JamModWindUp), 1.2)]
    [TestCase(typeof(O2JamModWindDown), 0.8)]
    [TestCase(typeof(O2JamModAdaptiveSpeed), 1.3)]
    public void DynamicRateDrivesScrollCompensationWithoutASecondAudioTarget(Type modType, double speed)
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        var mod = (Mod)Activator.CreateInstance(modType)!;
        using var drawableRuleset = new O2JamDrawableRuleset(ruleset, beatmap, [mod]);
        var speedChange = mod switch
        {
            ModTimeRamp ramp => ramp.SpeedChange,
            ModAdaptiveSpeed adaptive => adaptive.SpeedChange,
            _ => throw new InvalidOperationException(),
        };
        var visualRate = (osu.Framework.Bindables.BindableDouble)typeof(O2JamDrawableRuleset)
                                                               .GetField("speedAdjustment", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!
                                                               .GetValue(drawableRuleset)!;

        speedChange.Value = speed;

        Assert.That(visualRate.Value, Is.EqualTo(speed));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeColumnModsPreserveSourceTimingSamplesAndHoldEndpoints(bool random)
    {
        var ruleset = new O2LazerRuleset();
        var source = createBeatmap(ruleset);
        var columns = source.HitObjects.Select(note => note.Column).ToArray();
        var expected = createBeatmap(ruleset);
        Mod mod = random ? new O2JamModRandom { Seed = { Value = 12345 } } : new O2JamModMirror();
        IApplicableToBeatmap native = random ? new ManiaModRandom { Seed = { Value = 12345 } } : new ManiaModMirror();
        native.ApplyToBeatmap(expected);
        var working = new FlatWorkingBeatmap(source);
        var playable = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, [mod], default);
        var repeated = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, [mod], default);
        var unmodified = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, [], default);

        Assert.Multiple(() =>
        {
            Assert.That(playable.HitObjects.Select(note => note.Column), Is.EqualTo(expected.HitObjects.Select(note => note.Column)));
            Assert.That(repeated.HitObjects.Select(note => note.Column), Is.EqualTo(playable.HitObjects.Select(note => note.Column)));
            Assert.That(source.HitObjects.Select(note => note.Column), Is.EqualTo(columns));
            Assert.That(unmodified.HitObjects.Select(note => note.Column), Is.EqualTo(columns));
            Assert.That(playable.AutomaticAudioEvents, Is.EqualTo(source.AutomaticAudioEvents));
            Assert.That(playable.MeasureLineTimes, Is.EqualTo(source.MeasureLineTimes));
        });

        foreach (var (note, original) in playable.HitObjects.Zip(source.HitObjects))
        {
            Assert.Multiple(() =>
            {
                Assert.That(note, Is.Not.SameAs(original));
                Assert.That(note.StartTime, Is.EqualTo(original.StartTime));
                Assert.That(note.Samples, Is.Not.SameAs(original.Samples));
                Assert.That(note.Samples.Single(), Is.TypeOf<O2JamHitSampleInfo>());
                Assert.That(note.Samples, Is.EqualTo(original.Samples));
                Assert.That(((O2JamHitSampleInfo)note.Samples.Single()).Pan,
                    Is.EqualTo(((O2JamHitSampleInfo)original.Samples.Single()).Pan));
            });

            if (note is O2JamHoldNote hold && original is O2JamHoldNote originalHold)
            {
                Assert.Multiple(() =>
                {
                    Assert.That(hold.Head.Column, Is.EqualTo(hold.Column));
                    Assert.That(hold.Tail.Column, Is.EqualTo(hold.Column));
                    Assert.That(hold.Duration, Is.EqualTo(originalHold.Duration));
                    Assert.That(((O2JamHoldHead)hold.Head).ChartPosition, Is.EqualTo(originalHold.HeadChartPosition));
                    Assert.That(((O2JamHoldTail)hold.Tail).ChartPosition, Is.EqualTo(originalHold.TailChartPosition));
                    Assert.That(hold.Head.Samples.Single(), Is.TypeOf<O2JamHitSampleInfo>());
                    Assert.That(hold.Tail.Samples, Is.Empty);
                });
            }
            else
                Assert.That(((O2JamNote)note).ChartPosition, Is.EqualTo(((O2JamNote)original).ChartPosition));
        }
    }

    [Test]
    public void RandomSeedSurvivesReplayArchiveAndRecreatesCombinedColumnMods()
    {
        var ruleset = new O2LazerRuleset();
        var working = new FlatWorkingBeatmap(createBeatmap(ruleset));
        var random = new O2JamModRandom();
        Mod[] mods = [new O2JamModNoFail(), random, new O2JamModMirror()];
        var playable = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);
        var replay = new O2JamAutoGenerator(playable).Generate();
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(working.BeatmapInfo, ruleset.RulesetInfo) { Mods = mods },
            Replay = replay,
        };

        Assert.That(random.Seed.Value, Is.Not.Null);
        Assert.That(O2JamReplayArchive.TryReadMetadata(O2JamReplayArchive.Create(score), out var metadata), Is.True);
        var restored = new ScoreInfo(ruleset: ruleset.RulesetInfo) { ModsJson = metadata.ModsJson };
        var restoredRandom = restored.Mods.OfType<O2JamModRandom>().Single();
        var replayBeatmap = (O2JamBeatmap)working.GetPlayableBeatmap(ruleset.RulesetInfo, restored.Mods, default);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Mods.Select(mod => mod.Acronym), Is.EqualTo(mods.Select(mod => mod.Acronym)));
            Assert.That(restoredRandom.Seed.Value, Is.EqualTo(random.Seed.Value));
            Assert.That(replayBeatmap.HitObjects.Select(note => note.Column), Is.EqualTo(playable.HitObjects.Select(note => note.Column)));
            Assert.That(replay.Frames.Cast<O2JamReplayFrame>().First().Actions,
                Is.EqualTo(new[] { ManiaAction.Key1 + playable.HitObjects[0].Column }));
        });
    }

    [TestCase(O2JamDifficulty.EX)]
    [TestCase(O2JamDifficulty.NX)]
    [TestCase(O2JamDifficulty.HX)]
    public void NoFailKeepsScoringAndLifeRecoveryWithNativeMultiplier(O2JamDifficulty difficulty)
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new O2JamBeatmap(difficulty, new O2JamTimingMap(120));
        var noFail = new O2JamModNoFail();
        var processor = new O2JamScoreProcessor(ruleset) { Mods = { Value = [noFail] } };
        processor.ApplyBeatmap(beatmap);
        var health = new O2JamHealthProcessor();
        health.ApplyBeatmap(beatmap);
        health.Failed += noFail.PerformFail;

        for (var i = 0; i < 40; i++)
            apply(O2JamAccuracy.Miss);

        Assert.Multiple(() =>
        {
            Assert.That(health.Health.Value, Is.Zero);
            Assert.That(health.HasFailed, Is.False);
            Assert.That(processor.GameplayState.Current.HasFailed, Is.False);
            Assert.That(processor.GameplayState.Current.ScoringEnabled, Is.True);
        });

        for (var i = 0; i < 25; i++)
            apply(O2JamAccuracy.Cool);

        var score = new ScoreInfo(ruleset: ruleset.RulesetInfo);
        processor.PopulateScore(score);
        Assert.Multiple(() =>
        {
            Assert.That(processor.GameplayState.Current.Score, Is.EqualTo(5000));
            Assert.That(score.TotalScore, Is.EqualTo(2500));
            Assert.That(score.TotalScoreWithoutMods, Is.EqualTo(5000));
            Assert.That(score.MaxCombo, Is.EqualTo(24));
            Assert.That(processor.GameplayState.Current.JamCombo, Is.EqualTo(1));
            Assert.That(processor.GameplayState.Current.Pills, Is.EqualTo(1));
            Assert.That(health.Health.Value, Is.EqualTo(processor.GameplayState.Current.Life / 1000d).Within(0.000001));
            Assert.That(health.Health.Value, Is.GreaterThan(0));
        });

        processor.Mods.Value = [];
        processor.ApplyBeatmap(beatmap);
        for (var i = 0; i < 40; i++)
            processor.Resolve(createResult(), O2JamAccuracy.Miss);
        Assert.That(processor.GameplayState.Current.ScoringEnabled, Is.False);

        void apply(O2JamAccuracy accuracy)
        {
            var result = createResult();
            processor.Resolve(result, accuracy);
            health.ApplyResult(result);
            processor.ApplyResult(result);
        }
    }

    private static O2JamJudgementResult createResult()
    {
        var note = new O2JamNote();
        return new O2JamJudgementResult(note, note.CreateJudgement());
    }

    private static O2JamBeatmap createBeatmap(O2LazerRuleset ruleset)
    {
        var timing = new O2JamTimingMap(120);
        var beatmap = new O2JamBeatmap(O2JamDifficulty.HX, timing);
        beatmap.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        beatmap.BeatmapInfo.Hash = "mod-test-chart";
        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 500 });
        beatmap.MeasureLineTimes.AddRange([0, 2000, 4000]);
        beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(0, 1000, 80, 0.25f));

        for (var column = 0; column < O2JamBeatmap.ColumnCount; column++)
        {
            var start = 1000 + column * 500;
            beatmap.HitObjects.Add(new O2JamNote
            {
                StartTime = start,
                Column = column,
                ChartPosition = timing.PositionAt(start),
                TimingMap = timing,
                Samples = [new O2JamHitSampleInfo(column, 75, -0.5f)],
            });
            beatmap.HitObjects.Add(new O2JamHoldNote
            {
                StartTime = start + 250,
                Duration = 100,
                Column = column,
                HeadChartPosition = timing.PositionAt(start + 250),
                TailChartPosition = timing.PositionAt(start + 350),
                TimingMap = timing,
                Samples = [new O2JamHitSampleInfo(column + 10, 90, 0.5f)],
            });
        }

        return beatmap;
    }
}
