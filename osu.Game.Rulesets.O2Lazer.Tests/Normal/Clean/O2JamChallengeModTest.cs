using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.Timing;
using osu.Game.Rulesets.UI.Scrolling.Algorithms;
using osu.Game.Scoring;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamChallengeModTest
{
    [TestCase(false, O2JamAccuracy.Cool, false)]
    [TestCase(false, O2JamAccuracy.Good, false)]
    [TestCase(false, O2JamAccuracy.Bad, false)]
    [TestCase(false, O2JamAccuracy.Miss, true)]
    [TestCase(true, O2JamAccuracy.Cool, false)]
    [TestCase(true, O2JamAccuracy.Good, true)]
    [TestCase(true, O2JamAccuracy.Bad, true)]
    [TestCase(true, O2JamAccuracy.Miss, true)]
    public void NativeFailConditionsApplyToO2JamJudgements(bool perfect, O2JamAccuracy accuracy, bool fails)
    {
        foreach (var difficulty in Enum.GetValues<O2JamDifficulty>())
        {
            var ruleset = new O2LazerRuleset();
            var beatmap = new O2JamBeatmap(difficulty, new O2JamTimingMap(120));
            using var score = new O2JamScoreProcessor(ruleset);
            using var health = new O2JamHealthProcessor();
            score.ApplyBeatmap(beatmap);
            health.ApplyBeatmap(beatmap);
            ModFailCondition mod = perfect ? new O2JamModPerfect() : new O2JamModSuddenDeath();
            mod.ApplyToHealthProcessor(health);
            health.Failed += mod.PerformFail;

            var note = new O2JamNote();
            var result = new O2JamJudgementResult(note, note.CreateJudgement());
            score.Resolve(result, accuracy);
            health.ApplyResult(result);

            Assert.That(health.HasFailed, Is.EqualTo(fails), $"{difficulty}: {mod.Acronym}, {accuracy}");
            Assert.That(health.Health.Value, Is.GreaterThan(0), "The mod failure must not depend on life depletion.");
            Assert.That(mod.RestartOnFail, Is.EqualTo(perfect));
            mod.Restart.Value = !perfect;
            Assert.That(mod.RestartOnFail, Is.EqualTo(!perfect));
        }
    }

    [TestCase(false)]
    [TestCase(true)]
    public void PerfectUsesResolvedPillJudgementsAndIndependentHoldEndpoints(bool requirePerfectHits)
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        using var score = new O2JamScoreProcessor(ruleset);
        using var health = new O2JamHealthProcessor();
        score.ApplyBeatmap(beatmap);
        health.ApplyBeatmap(beatmap);
        var mod = new O2JamModPerfect { RequirePerfectHits = { Value = requirePerfectHits } };
        mod.ApplyToHealthProcessor(health);

        for (var i = 0; i < O2JamGameplayState.CoolHitsPerPill; i++)
            apply(new O2JamNote(), O2JamAccuracy.Cool);

        var rescuedHead = apply(new O2JamHoldHead(), O2JamAccuracy.Bad);
        Assert.That(rescuedHead.Type, Is.EqualTo(HitResult.Perfect));
        Assert.That(rescuedHead.Resolution.PillConsumed, Is.True);
        Assert.That(health.HasFailed, Is.False);

        var body = new O2JamHoldBody();
        health.ApplyResult(new JudgementResult(body, body.CreateJudgement()) { Type = HitResult.IgnoreMiss });
        Assert.That(health.HasFailed, Is.False, "The visual hold body must not trigger Perfect.");

        apply(new O2JamHoldTail(), O2JamAccuracy.Good);
        Assert.That(health.HasFailed, Is.True, "The hold release has its own accuracy judgement.");

        O2JamJudgementResult apply(ManiaHitObject note, O2JamAccuracy accuracy)
        {
            var result = new O2JamJudgementResult(note, note.CreateJudgement());
            score.Resolve(result, accuracy);
            health.ApplyResult(result);
            return result;
        }
    }

    [TestCase("SD,NF", false)]
    [TestCase("PF,NF", false)]
    [TestCase("SD,PF", false)]
    [TestCase("SD,CS", true)]
    [TestCase("PF,CS,MR", true)]
    public void NativeCompatibilityRulesApplyToLocalModTypes(string selection, bool compatible)
    {
        var available = new O2LazerRuleset().CreateAllMods().ToDictionary(mod => mod.Acronym);
        Assert.That(ModUtils.CheckCompatibleSet(selection.Split(',').Select(acronym => available[acronym])), Is.EqualTo(compatible));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void ChallengeSettingsAndConstantSpeedSurviveReplayArchive(bool perfect)
    {
        var ruleset = new O2LazerRuleset();
        ModFailCondition challenge = perfect
            ? new O2JamModPerfect { RequirePerfectHits = { Value = true } }
            : new O2JamModSuddenDeath();
        challenge.Restart.Value = !perfect;
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(new BeatmapInfo(ruleset.RulesetInfo) { Hash = "challenge-mod-replay" }, ruleset.RulesetInfo)
            {
                Mods = [challenge, new O2JamModConstantSpeed()],
            },
        };
        score.Replay.Frames.Add(new O2JamReplayFrame(0));
        Assert.That(O2JamReplayArchive.TryReadMetadata(O2JamReplayArchive.Create(score), out var metadata), Is.True);
        var restored = new ScoreInfo(ruleset: ruleset.RulesetInfo) { ModsJson = metadata.ModsJson };
        Assert.That(restored.Mods.Select(mod => mod.Acronym), Is.EqualTo(new[] { challenge.Acronym, "CS" }));
        Assert.That(restored.Mods.OfType<ModFailCondition>().Single().RestartOnFail, Is.EqualTo(!perfect));
        if (perfect)
            Assert.That(restored.Mods.OfType<O2JamModPerfect>().Single().RequirePerfectHits.Value, Is.True);
    }

    [Test]
    public void ConstantSpeedUsesNativeScrollingWithoutChangingChartTimingOrAnotherPlayfield()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, new O2JamTimingMap(120));
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 1500 });
        beatmap.HitObjects.Add(new O2JamHoldNote { StartTime = 2500, Duration = 500 });
        using var constant = new ScrollProbe(ruleset, beatmap);
        using var normal = new ScrollProbe(ruleset, beatmap);
        var sequential = normal.ScrollingInfo.Algorithm.Value;
        Assert.That(sequential.PositionAt(1500, 1000, 2000, 1000),
            Is.Not.EqualTo(sequential.PositionAt(3000, 2500, 2000, 1000)));

        IApplicableToDrawableRuleset<ManiaHitObject> mod = new O2JamModConstantSpeed();
        mod.ApplyToDrawableRuleset(constant);
        var algorithm = constant.ScrollingInfo.Algorithm.Value;
        Assert.Multiple(() =>
        {
            Assert.That(algorithm, Is.TypeOf<ConstantScrollAlgorithm>());
            Assert.That(normal.VisualisationMethod, Is.EqualTo(ScrollVisualisationMethod.Sequential));
            Assert.That(normal.ScrollingInfo.Algorithm.Value, Is.SameAs(sequential));
            Assert.That(algorithm.PositionAt(1500, 1000, 2000, 1000), Is.EqualTo(250));
            Assert.That(algorithm.PositionAt(3000, 2500, 2000, 1000), Is.EqualTo(250));
            Assert.That(algorithm.GetLength(1750, 2250, 2000, 1000), Is.EqualTo(250));
            Assert.That(beatmap.HitObjects.Select(note => note.StartTime), Is.EqualTo(new[] { 1500d, 2500d }));
            Assert.That(((O2JamHoldNote)beatmap.HitObjects[1]).Duration, Is.EqualTo(500));
        });
    }

    private partial class ScrollProbe : O2JamDrawableRuleset
    {
        public ScrollProbe(O2LazerRuleset ruleset, O2JamBeatmap beatmap)
            : base(ruleset, beatmap)
        {
            // Different BPM regions expose accidental reuse of sequential scrolling by CS.
            ControlPoints.Add(new MultiplierControlPoint(0) { BaseBeatLength = 500, TimingPoint = new TimingControlPoint { BeatLength = 500 } });
            ControlPoints.Add(new MultiplierControlPoint(2000) { BaseBeatLength = 500, TimingPoint = new TimingControlPoint { BeatLength = 250 } });
            VisualisationMethod = ScrollVisualisationMethod.Sequential;
        }
    }
}
