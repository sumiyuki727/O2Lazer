using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using NUnit.Framework;
using O2Jam.Core;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Configuration;
using osu.Game.Replays;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.Mania.Difficulty;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mania.UI;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.HUD.JudgementCounter;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamManiaScoreTest
{
    [Test]
    public void EntryIsImplementedAndDefaultsToRankedSeven()
    {
        _ = new O2LazerRuleset();
        var mod = new O2JamModManiaScore();

        Assert.Multiple(() =>
        {
            Assert.That(mod.HasImplementation, Is.True);
            Assert.That(mod.RequiresConfiguration, Is.False);
            Assert.That(mod.Description.ToString(), Is.EqualTo("For pp farmers."));
            Assert.That(mod.OverallDifficulty.Value, Is.EqualTo(7));
            Assert.That(mod.DrainRate.Value, Is.EqualTo(7));
            Assert.That(mod.UsesDefaultConfiguration, Is.True);
            Assert.That(mod.Ranked, Is.True);
            Assert.That(((IApplicableHealthProcessor)mod).CreateHealthProcessor(0), Is.TypeOf<ManiaHealthProcessor>());
            Assert.That(mod.GetOrderedSettingsSourceProperties().Select(setting => setting.Item1.Label),
                Is.EquivalentTo(new[]
                {
                    O2LazerStrings.ManiaScoreHealthDrain,
                    O2LazerStrings.ManiaScoreOverallDifficulty,
                    O2LazerStrings.ManiaScoreExtendedLimits,
                }));
            Assert.That(new O2JamModEasy().GetOrderedSettingsSourceProperties().Single().Item1.Label,
                Is.EqualTo(O2LazerStrings.ModEasyExtraLives));
            Assert.That(new O2JamModClassic().Description.ToString(), Is.EqualTo(new ManiaModClassic().Description.ToString()));
            Assert.That(O2JamCompatibilityPatches.IsInstalled, Is.True,
                string.Join(", ", O2JamCompatibilityPatches.FailedPatches));
            Assert.That(O2JamCompatibilityPatches.CanPlay, Is.True,
                string.Join(", ", O2JamCompatibilityPatches.FailedGameplayPatches));
        });
    }

    [Test]
    public void PlayableBeatmapUsesNativeManiaObjectsAndHoldChildren()
    {
        var ruleset = new O2LazerRuleset();
        var playable = getPlayable(createBeatmap(ruleset), ruleset, [new O2JamModManiaScore()]);

        Assert.Multiple(() =>
        {
            Assert.That(playable.Difficulty.OverallDifficulty, Is.EqualTo(7));
            Assert.That(playable.Difficulty.DrainRate, Is.EqualTo(7));
            Assert.That(playable.HitObjects[0], Is.TypeOf<Note>());
            Assert.That(playable.HitObjects[1], Is.TypeOf<HoldNote>());
        });

        var note = (Note)playable.HitObjects[0];
        var hold = (HoldNote)playable.HitObjects[1];
        Assert.Multiple(() =>
        {
            Assert.That(note.HitWindows, Is.TypeOf<ManiaHitWindows>());
            Assert.That(hold.Head, Is.TypeOf<HeadNote>());
            Assert.That(hold.Tail, Is.TypeOf<TailNote>());
            Assert.That(hold.Head.HitWindows, Is.TypeOf<ManiaHitWindows>());
            Assert.That(hold.Tail.HitWindows, Is.TypeOf<ManiaHitWindows>());
            Assert.That(hold.Head.Samples, Has.Count.EqualTo(1));
            Assert.That(hold.Tail.Samples, Is.Empty);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EasyUsesManiaWindowsAndHealthRegardlessOfSelectionOrder(bool maniaScoreFirst)
    {
        var ruleset = new O2LazerRuleset();
        var maniaScore = new O2JamModManiaScore();
        var easy = new O2JamModEasy();
        Mod[] mods = maniaScoreFirst ? [maniaScore, easy] : [easy, maniaScore];
        var playable = getPlayable(createBeatmap(ruleset), ruleset, mods);
        var windows = (ManiaHitWindows)((Note)playable.HitObjects[0]).HitWindows;

        Assert.Multiple(() =>
        {
            Assert.That(playable.Difficulty.DrainRate, Is.EqualTo(3.5f));
            Assert.That(playable.Difficulty.OverallDifficulty, Is.EqualTo(7));
            Assert.That(windows.DifficultyMultiplier, Is.EqualTo(ManiaModEasy.HIT_WINDOW_DIFFICULTY_MULTIPLIER));
        });
    }

    [Test]
    public void HardRockUsesManiaWindowsAndHealth()
    {
        var ruleset = new O2LazerRuleset();
        var playable = getPlayable(createBeatmap(ruleset), ruleset, [new O2JamModHardRock(), new O2JamModManiaScore()]);
        var windows = (ManiaHitWindows)((Note)playable.HitObjects[0]).HitWindows;

        Assert.Multiple(() =>
        {
            Assert.That(playable.Difficulty.DrainRate, Is.EqualTo(9.8f).Within(0.0001));
            Assert.That(playable.Difficulty.OverallDifficulty, Is.EqualTo(7));
            Assert.That(windows.DifficultyMultiplier, Is.EqualTo(ManiaModHardRock.HIT_WINDOW_DIFFICULTY_MULTIPLIER));
        });
    }

    [Test]
    public void DifficultyAndPerformanceUseNativeManiaAttributesAndFormula()
    {
        var ruleset = new O2LazerRuleset();
        var maniaScore = new O2JamModManiaScore();
        var attributes = ruleset.CreateDifficultyCalculator(new FlatWorkingBeatmap(createBeatmap(ruleset))).Calculate([maniaScore]);
        var score = new ScoreInfo(ruleset: ruleset.RulesetInfo) { Mods = [maniaScore] };
        score.Statistics[HitResult.Perfect] = 3;
        score.Statistics[HitResult.Great] = 2;
        score.Statistics[HitResult.Good] = 1;

        var actual = ruleset.CreatePerformanceCalculator()!.Calculate(score, attributes);
        var expected = new ManiaPerformanceCalculator().Calculate(score, attributes);

        Assert.Multiple(() =>
        {
            Assert.That(attributes, Is.TypeOf<ManiaDifficultyAttributes>());
            Assert.That(attributes.MaxCombo, Is.EqualTo(5));
            Assert.That(attributes.StarRating, Is.GreaterThanOrEqualTo(0));
            Assert.That(actual.Total, Is.EqualTo(expected.Total).Within(1e-12));
            Assert.That(actual.Total, Is.GreaterThan(0));
        });
    }

    [Test]
    public void ScoreProcessorMatchesNativeMania()
    {
        using var actual = new O2JamScoreProcessor(new O2LazerRuleset());
        using var expected = new ManiaScoreProcessor();
        actual.Mods.Value = [new O2JamModManiaScore()];
        actual.ApplyBeatmap(new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120)));
        expected.ApplyBeatmap(new ManiaBeatmap(new StageDefinition(7)));

        HitResult[] results =
        [
            HitResult.Perfect,
            HitResult.Great,
            HitResult.Good,
            HitResult.Ok,
            HitResult.Meh,
            HitResult.Miss,
            HitResult.Perfect,
        ];
        foreach (var result in results)
        {
            actual.ApplyResult(createResult(result));
            expected.ApplyResult(createResult(result));
        }

        Assert.Multiple(() =>
        {
            Assert.That(actual.TotalScore.Value, Is.EqualTo(expected.TotalScore.Value));
            Assert.That(actual.TotalScoreWithoutMods.Value, Is.EqualTo(expected.TotalScoreWithoutMods.Value));
            Assert.That(actual.Accuracy.Value, Is.EqualTo(expected.Accuracy.Value));
            Assert.That(actual.Combo.Value, Is.EqualTo(expected.Combo.Value));
            Assert.That(actual.HighestCombo.Value, Is.EqualTo(expected.HighestCombo.Value));
            Assert.That(actual.Rank.Value, Is.EqualTo(expected.Rank.Value));
        });
    }

    [Test]
    public void ResultStatisticsSwitchBetweenO2JamAndNativeManiaSets()
    {
        var ruleset = new O2LazerRuleset();
        var normal = new ScoreInfo(ruleset: ruleset.RulesetInfo);
        var maniaScore = new ScoreInfo(ruleset: ruleset.RulesetInfo) { Mods = [new O2JamModManiaScore()] };

        Assert.Multiple(() =>
        {
            Assert.That(normal.GetStatisticsForDisplay().Select(statistic => statistic.Result),
                Is.EqualTo(new[] { HitResult.Perfect, HitResult.Good, HitResult.Meh, HitResult.Miss }));
            Assert.That(maniaScore.GetStatisticsForDisplay().Select(statistic => statistic.Result),
                Is.EqualTo(new[] { HitResult.Perfect, HitResult.Great, HitResult.Good, HitResult.Ok, HitResult.Meh, HitResult.Miss }));
            Assert.That(maniaScore.GetStatisticsForDisplay().Select(statistic => statistic.DisplayName.ToString()),
                Is.EqualTo(new ManiaRuleset().GetHitResultsForDisplay().Select(result => result.displayName.ToString())));
        });
    }

    [Test]
    public void ResultDetailsReuseNativeManiaWithoutNonMsPerformanceBreakdown()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset);
        var normal = new ScoreInfo(ruleset: ruleset.RulesetInfo);
        var maniaScore = new ScoreInfo(ruleset: ruleset.RulesetInfo) { Mods = [new O2JamModManiaScore()] };
        var native = new ManiaRuleset().CreateStatisticsForScore(maniaScore, beatmap);

        var normalStatistics = ruleset.CreateStatisticsForScore(normal, beatmap);
        var maniaScoreStatistics = ruleset.CreateStatisticsForScore(maniaScore, beatmap);

        Assert.Multiple(() =>
        {
            Assert.That(normalStatistics.Select(statistic => statistic.Name.ToString()),
                Is.EqualTo(native.Skip(1).Select(statistic => statistic.Name.ToString())));
            Assert.That(normalStatistics.Select(statistic => statistic.RequiresHitEvents),
                Is.EqualTo(native.Skip(1).Select(statistic => statistic.RequiresHitEvents)));
            Assert.That(maniaScoreStatistics.Select(statistic => statistic.Name.ToString()),
                Is.EqualTo(native.Select(statistic => statistic.Name.ToString())));
        });
    }

    [Test]
    public void ReplayRoundTripPreservesManiaScoreSettingsAndKeyStates()
    {
        var ruleset = new O2LazerRuleset();
        var maniaScore = new O2JamModManiaScore();
        maniaScore.OverallDifficulty.Value = 8.2f;
        maniaScore.DrainRate.Value = 6.4f;
        var beatmapInfo = new BeatmapInfo(ruleset.RulesetInfo) { Hash = "mania-score-replay" };
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(beatmapInfo, ruleset.RulesetInfo) { Mods = [maniaScore] },
            Replay = new Replay
            {
                Frames =
                [
                    new O2JamReplayFrame(1000, ManiaAction.Key1, ManiaAction.Key4),
                    new O2JamReplayFrame(1100, ManiaAction.Key4),
                ],
            },
        };

        var archive = O2JamReplayArchive.Create(score);
        Assert.That(O2JamReplayArchive.TryReadMetadata(archive, out var metadata), Is.True);
        var restoredInfo = new ScoreInfo(beatmapInfo, ruleset.RulesetInfo) { ModsJson = metadata.ModsJson };
        Assert.That(O2JamReplayArchive.TryReadScore(restoredInfo, archive, out var restored), Is.True);
        var restoredManiaScore = restoredInfo.Mods.OfType<O2JamModManiaScore>().Single();

        Assert.Multiple(() =>
        {
            Assert.That(restoredManiaScore.OverallDifficulty.Value, Is.EqualTo(8.2f));
            Assert.That(restoredManiaScore.DrainRate.Value, Is.EqualTo(6.4f));
            Assert.That(restored.Replay.Frames.Cast<O2JamReplayFrame>().Select(frame => frame.Actions),
                Is.EqualTo(new[]
                {
                    new[] { ManiaAction.Key1, ManiaAction.Key4 },
                    new[] { ManiaAction.Key4 },
                }));
        });
    }

    [Test]
    public void GameplayJudgementCounterUsesTheSelectedScoringSystem()
    {
        var ruleset = new O2LazerRuleset();

        Assert.Multiple(() =>
        {
            Assert.That(getCounterNames([]), Is.EqualTo(new[] { "COOL", "GOOD", "BAD", "MISS" }));
            Assert.That(getCounterNames([new O2JamModManiaScore()]),
                Is.EqualTo(new ManiaRuleset().GetHitResultsForDisplay().Select(result => result.displayName.ToString())));
        });

        string[] getCounterNames(Mod[] mods)
        {
            using var processor = new O2JamScoreProcessor(ruleset) { Mods = { Value = mods } };
            using var controller = new JudgementCountController();
            typeof(JudgementCountController).GetProperty("scoreProcessor", BindingFlags.Instance | BindingFlags.NonPublic)!
                                            .SetValue(controller, processor);
            typeof(JudgementCountController).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!
                                            .Invoke(controller, [new Bindable<RulesetInfo>(ruleset.RulesetInfo)]);
            return controller.Counters.Select(counter => counter.DisplayName.ToString()).ToArray();
        }
    }

    private static JudgementResult createResult(HitResult type)
    {
        var note = new Note();
        return new JudgementResult(note, note.CreateJudgement()) { Type = type };
    }

    private static O2JamBeatmap getPlayable(O2JamBeatmap source, O2LazerRuleset ruleset, IReadOnlyList<Mod> mods) =>
        (O2JamBeatmap)new FlatWorkingBeatmap(source).GetPlayableBeatmap(ruleset.RulesetInfo, mods, default);

    private static O2JamBeatmap createBeatmap(O2LazerRuleset ruleset)
    {
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.BeatmapInfo.Ruleset = ruleset.RulesetInfo;
        beatmap.HitObjects.Add(new O2JamNote
        {
            StartTime = 1000,
            Column = 1,
            Samples = [new O2JamHitSampleInfo(1, 100, 0)],
        });
        beatmap.HitObjects.Add(new O2JamHoldNote
        {
            StartTime = 1500,
            Duration = 350,
            Column = 2,
            NodeSamples = [[new O2JamHitSampleInfo(2, 100, 0)], []],
        });
        return beatmap;
    }
}
