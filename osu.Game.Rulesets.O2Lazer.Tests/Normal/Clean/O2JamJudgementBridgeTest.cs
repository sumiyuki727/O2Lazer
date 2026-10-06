using System.Collections.Generic;
using NUnit.Framework;
using O2Jam.Core;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamJudgementBridgeTest
{
    [Test]
    public void NativeLifetimeEnvelopeRetainsTheExtraLateTick(
        [Values(O2JamEndpointKind.Tap, O2JamEndpointKind.HoldHead, O2JamEndpointKind.HoldRelease)] O2JamEndpointKind endpoint,
        [Values(false, true)] bool changesBpm)
    {
        var map = changesBpm ? new O2JamTimingMap(240, [new O2JamBpmEvent(1, 120)]) : new O2JamTimingMap(160);
        IO2JamJudgedObject judged = endpoint switch
        {
            O2JamEndpointKind.Tap => new O2JamNote(),
            O2JamEndpointKind.HoldHead => new O2JamHoldHead(),
            _ => new O2JamHoldTail(),
        };
        judged.ChartPosition = changesBpm ? 1.003 : 1;
        judged.TimingMap = map;
        var note = (osu.Game.Rulesets.Mania.Objects.Note)judged;
        note.StartTime = map.TimeAt(judged.ChartPosition);
        note.ApplyDefaults(new osu.Game.Beatmaps.ControlPoints.ControlPointInfo(), new osu.Game.Beatmaps.BeatmapDifficulty());
        var lastHit = map.TimeAt(judged.ChartPosition + O2JamTimingMap.TicksToPosition(25.5));
        var deadline = map.TimeAt(judged.ChartPosition + O2JamTimingMap.TicksToPosition(26));
        Assert.That(note.HitWindows!.CanBeHit(lastHit - note.StartTime), Is.True);
        Assert.That(note.MaximumJudgementOffset, Is.GreaterThan(lastHit - note.StartTime));
        Assert.That(judged.Judge(lastHit, true).Accuracy, Is.EqualTo(O2JamAccuracy.Bad));
        Assert.That(judged.Judge(deadline, false).Accuracy, Is.EqualTo(O2JamAccuracy.Miss));
        Assert.That(note.MaximumJudgementOffset, Is.EqualTo(deadline - note.StartTime).Within(1e-8));
        Assert.That(note.HitWindows.WindowFor(HitResult.Miss), Is.EqualTo(note.MaximumJudgementOffset));
    }

    [TestCase(O2JamAccuracy.None, false)]
    [TestCase(O2JamAccuracy.Cool, true)]
    [TestCase(O2JamAccuracy.Good, true)]
    [TestCase(O2JamAccuracy.Bad, true)]
    [TestCase(O2JamAccuracy.Miss, false)]
    public void TailHitClassificationBelongsToNativeProjection(O2JamAccuracy accuracy, bool expected)
    {
        var state = new O2JamHoldState(O2JamAccuracy.Cool, accuracy, false);
        Assert.That(O2JamJudgementBridge.IsTailHit(state), Is.EqualTo(expected));
        Assert.That(state.TailAccuracy, Is.EqualTo(accuracy));
    }

    [Test]
    public void PassiveInspectionDoesNotReserveAResult()
    {
        using var processor = createProcessor();
        var note = new O2JamNote { ChartPosition = 1 };
        var result = new O2JamJudgementResult(note, note.CreateJudgement());
        var time = note.TimingMap.TimeAt(1);
        Assert.That(O2JamJudgementBridge.CheckNote(note, result, processor, time, false), Is.EqualTo(HitResult.None));
        Assert.That(result.ResolutionApplied, Is.False);
        Assert.That(O2JamJudgementBridge.CheckNote(note, result, processor, time, true), Is.EqualTo(HitResult.Perfect));
        Assert.That(result.HasResult, Is.False);
        Assert.That(processor.GameplayState.Current.Score, Is.EqualTo(200));
    }

    [Test]
    public void RepeatedPreparationAndCommittedResultDoNotScoreTwice()
    {
        using var processor = createProcessor();
        var result = createResult();
        var prepared = O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.Cool);
        Assert.That(O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.Miss), Is.EqualTo(prepared));
        Assert.That(processor.GameplayState.Current.Score, Is.EqualTo(200));
        result.Type = prepared;
        processor.ApplyResult(result);
        Assert.That(O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.Cool), Is.EqualTo(HitResult.None));
        Assert.That(processor.GameplayState.Current.Score, Is.EqualTo(200));
    }

    [Test]
    public void PillConversionScoresCoolButKeepsTheRejectedHoldHead()
    {
        using var processor = createProcessor();
        for (var i = 0; i < O2JamGameplayState.CoolHitsPerPill; i++)
            commit(processor, createResult(), O2JamAccuracy.Cool);

        var head = new O2JamHoldHead { ChartPosition = 1 };
        var result = new O2JamJudgementResult(head, head.CreateJudgement());
        var time = head.TimingMap.TimeAt(1 + O2JamTimingMap.TicksToPosition(20));
        Assert.That(O2JamJudgementBridge.CheckHead(head, default, result, processor, time, true), Is.EqualTo(HitResult.Perfect));
        Assert.That(result.RequestedAccuracy, Is.EqualTo(O2JamAccuracy.Bad));
        Assert.That(result.Resolution.PillConsumed, Is.True);
        Assert.That(result.HasResult, Is.False);
        var state = O2JamJudgementBridge.ReadHoldState(result, null, false);
        Assert.That(state.HeadAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
        Assert.That(state.RequestedHeadAccuracy, Is.EqualTo(O2JamAccuracy.Bad));
        Assert.That(state.CanBeginHold, Is.False);
        Assert.That(state.RequiresTailMiss, Is.True);
        result.Type = HitResult.Perfect;
        processor.ApplyResult(result);

        var tail = new O2JamHoldTail { ChartPosition = 2, ReleaseTimingDisabled = true };
        var tailResult = new O2JamJudgementResult(tail, tail.CreateJudgement());
        var prepared = O2JamJudgementBridge.CheckTail(tail, state, tailResult, processor, time, false);
        Assert.That(prepared, Is.EqualTo(HitResult.Miss));
        tailResult.Type = prepared;
        processor.ApplyResult(tailResult);
        var completed = processor.GameplayState.Current;
        Assert.That(completed.Score, Is.EqualTo(3190));
        Assert.That(completed.Combo, Is.EqualTo(-1));
        Assert.That(completed.JamCombo, Is.Zero);
        Assert.That(completed.ConsecutiveCoolProgress, Is.Zero);
        Assert.That(O2JamJudgementBridge.CheckTail(tail,
            O2JamJudgementBridge.ReadHoldState(result, tailResult, false), tailResult, processor, time, false), Is.EqualTo(HitResult.None));
        Assert.That(processor.GameplayState.Current, Is.EqualTo(completed));

        processor.RevertResult(tailResult);
        tailResult.Type = HitResult.None;
        Assert.That(O2JamJudgementBridge.ReadHoldState(result, tailResult, false).RequiresTailMiss, Is.True);
        processor.RevertResult(result);
        result.Type = HitResult.None;
        Assert.That(O2JamJudgementBridge.ReadHoldState(result, tailResult, false).HeadResolved, Is.False);
        Assert.That(processor.GameplayState.Current.Pills, Is.EqualTo(1));
        Assert.That(O2JamJudgementBridge.CheckHead(head, default, result, processor, time, true), Is.EqualTo(HitResult.Perfect));
        Assert.That(O2JamJudgementBridge.ReadHoldState(result, null, false).RequiresTailMiss, Is.True);
    }

    [Test]
    public void RejectedHeadForcesTailMissThroughTheSameScoringPath()
    {
        using var processor = createProcessor();
        var head = createResult();
        commit(processor, head, O2JamAccuracy.Bad);
        var tail = new O2JamHoldTail { ChartPosition = 2 };
        var result = new O2JamJudgementResult(tail, tail.CreateJudgement());
        var state = O2JamJudgementBridge.ReadHoldState(head, result, false);
        Assert.That(O2JamJudgementBridge.CheckTail(tail, state, result, processor, 0, false), Is.EqualTo(HitResult.Miss));
        Assert.That(result.RequestedAccuracy, Is.EqualTo(O2JamAccuracy.Miss));
        Assert.That(processor.GameplayState.Current.Life, Is.EqualTo(965));
    }

    [Test]
    public void NativeComboKeepsSentinelAndNeverRollsBackOnSuccessfulHits()
    {
        using var processor = createProcessor();
        var changes = new List<int>();
        processor.Combo.BindValueChanged(change => changes.Add(change.NewValue));
        Assert.That(processor.Combo.Value, Is.EqualTo(-1));
        commit(processor, createResult(), O2JamAccuracy.Cool);
        commit(processor, createResult(), O2JamAccuracy.Good);
        Assert.That(changes, Is.EqualTo(new[] { 0, 1 }));
        commit(processor, createResult(), O2JamAccuracy.Miss);
        Assert.That(processor.Combo.Value, Is.EqualTo(-1));
        changes.Clear();
        commit(processor, createResult(), O2JamAccuracy.Cool);
        Assert.That(changes, Is.EqualTo(new[] { 0 }));
    }

    [Test]
    public void RevertedResultCanBeJudgedAgain()
    {
        using var processor = createProcessor();
        var result = createResult();
        commit(processor, result, O2JamAccuracy.Cool);
        processor.RevertResult(result);
        // DrawableHitObject clears the native result when returning to Idle.
        result.Type = HitResult.None;
        Assert.That(O2JamJudgementBridge.ReadHoldState(result, null, false).HeadResolved, Is.False);
        Assert.That(O2JamJudgementBridge.Prepare(result, processor, O2JamAccuracy.Good), Is.EqualTo(HitResult.Good));
        Assert.That(processor.GameplayState.Current.Score, Is.EqualTo(100));
    }

    [Test]
    public void PreviewWithoutScoreProcessorOnlyMapsTheResult()
    {
        var result = createResult();
        Assert.That(O2JamJudgementBridge.Prepare(result, null, O2JamAccuracy.Bad), Is.EqualTo(HitResult.Meh));
        Assert.That(result.ResolutionApplied, Is.False);
        Assert.That(result.HasResult, Is.False);
    }

    private static void commit(O2JamScoreProcessor processor, O2JamJudgementResult result, O2JamAccuracy accuracy)
    {
        result.Type = O2JamJudgementBridge.Prepare(result, processor, accuracy);
        processor.ApplyResult(result);
    }

    private static O2JamScoreProcessor createProcessor()
    {
        var processor = new O2JamScoreProcessor(new O2LazerRuleset());
        processor.ApplyBeatmap(new O2JamBeatmap(O2JamDifficulty.HX, new O2JamTimingMap(120)));
        return processor;
    }

    private static O2JamJudgementResult createResult()
    {
        var note = new O2JamNote();
        return new O2JamJudgementResult(note, note.CreateJudgement());
    }
}
