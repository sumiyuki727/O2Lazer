using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamJudgementResolverTest
{
    [Test]
    public void BridgeAcceptsAHostResolverWithoutTheConcreteO2JamProcessor()
    {
        using var processor = new AlternateHostResolver();
        var result = createResult();
        Assert.That(O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.Bad), Is.EqualTo(HitResult.Perfect));
        Assert.That(processor.Calls, Is.EqualTo(1));
        Assert.That(result.HasResult, Is.False, "The native drawable retains ownership of result submission.");
    }

    [Test]
    public void PassiveAndCommittedResultsDoNotInvokeTheHostResolver()
    {
        using var processor = new AlternateHostResolver();
        var result = createResult();
        Assert.That(O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.None), Is.EqualTo(HitResult.None));
        result.Type = HitResult.Perfect;
        Assert.That(O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.Bad), Is.EqualTo(HitResult.None));
        Assert.That(processor.Calls, Is.Zero);
    }

    [Test]
    public void ManiaScoreRouteDoesNotChangeTheO2JamStateDuringPreparation()
    {
        using var processor = new O2JamScoreProcessor(new O2LazerRuleset());
        processor.Mods.Value = [new O2JamModManiaScore()];
        processor.ApplyBeatmap(new O2JamBeatmap(O2JamDifficulty.HX, new O2JamTimingMap(120)));
        var before = processor.GameplayState.Current;
        var result = createResult();
        Assert.That(O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.Bad), Is.EqualTo(HitResult.Ok));
        Assert.That(result.ResolutionApplied, Is.False);
        Assert.That(result.HasResult, Is.False);
        Assert.That(processor.GameplayState.Current, Is.EqualTo(before));
    }

    private static O2JamJudgementResult createResult()
    {
        var note = new O2JamNote();
        return new O2JamJudgementResult(note, note.CreateJudgement());
    }

    private sealed partial class AlternateHostResolver() : ScoreProcessor(new O2LazerRuleset()), IO2JamJudgementResolver
    {
        public int Calls { get; private set; }

        public O2JamAccuracy ResolveAccuracyForApplication(O2JamJudgementResult result, O2JamAccuracy requestedAccuracy)
        {
            Calls++;
            return O2JamAccuracy.Cool;
        }
    }
}
