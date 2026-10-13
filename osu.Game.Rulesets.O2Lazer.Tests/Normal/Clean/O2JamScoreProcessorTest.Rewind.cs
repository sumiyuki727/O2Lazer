using System.Collections.Generic;
using NUnit.Framework;
using O2Jam.Core;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamScoreProcessorTest
{
    [Test]
    public void NativeUnorderedUndoRetainsAppliedStatisticsAndRestoresTheCorePrefix()
    {
        var processor = createProcessor(O2JamDifficulty.HX);
        var health = new O2JamHealthProcessor();
        health.ApplyBeatmap(processor.Beatmap.Value!);
        for (var i = 0; i < 14; i++)
            apply(O2JamAccuracy.Cool);
        var prefix = processor.GameplayState.Current;
        var earningPill = apply(O2JamAccuracy.Cool);
        var rescued = apply(O2JamAccuracy.Bad);
        var originalResolution = rescued.Resolution;
        var good = apply(O2JamAccuracy.Good);
        var score = new ScoreInfo(ruleset: processor.Ruleset.RulesetInfo);
        var source = processor.GameplayState;
        var notifications = new List<O2JamGameplaySnapshot>();
        source.StateChanged += notifications.Add;

        revert(earningPill);
        processor.PopulateScore(score);
        Assert.Multiple(() =>
        {
            Assert.That(rescued.Resolution, Is.EqualTo(originalResolution));
            Assert.That(rescued.Type, Is.EqualTo(HitResult.Perfect));
            Assert.That(score.Statistics[HitResult.Perfect], Is.EqualTo(15));
            Assert.That(score.Statistics[HitResult.Good], Is.EqualTo(1));
            Assert.That(processor.GameplayState.Current.Score, Is.EqualTo(2904));
            Assert.That(processor.GameplayState.Current.Combo, Is.Zero);
        });

        revert(good);
        revert(rescued);
        processor.PopulateScore(score);
        Assert.Multiple(() =>
        {
            Assert.That(processor.GameplayState.Current, Is.EqualTo(prefix));
            Assert.That(processor.Combo.Value, Is.EqualTo(prefix.Combo));
            Assert.That(score.TotalScoreWithoutMods, Is.EqualTo(prefix.Score));
            Assert.That(score.Statistics[HitResult.Perfect], Is.EqualTo(14));
            Assert.That(score.Statistics[HitResult.Good], Is.Zero);
            Assert.That(health.Health.Value, Is.EqualTo(prefix.Life / 1000d));
        });
        Assert.That(apply(O2JamAccuracy.Bad).Type, Is.EqualTo(HitResult.Meh));
        Assert.That(processor.GameplayState.Current.Life, Is.EqualTo(995));

        O2JamJudgementResult apply(O2JamAccuracy accuracy)
        {
            var item = createResult();
            processor.Resolve(item, accuracy);
            health.ApplyResult(item);
            processor.ApplyResult(item);
            return item;
        }

        void revert(O2JamJudgementResult item)
        {
            notifications.Clear();
            health.RevertResult(item);
            processor.RevertResult(item);
            Assert.That(processor.GameplayState, Is.SameAs(source));
            Assert.That(notifications, Is.EqualTo(new[] { source.Current }));
        }
    }
}
