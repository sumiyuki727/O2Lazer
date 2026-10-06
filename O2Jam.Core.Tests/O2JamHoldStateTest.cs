using NUnit.Framework;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Core;

[TestFixture]
public class O2JamHoldStateTest
{
    private readonly O2JamTimingMap map = new(120);

    private O2JamHoldJudgementEngine engine => new(new O2JamPositionClock(map));

    [Test]
    public void EmptyHeadResolutionLeavesSnapshotUnchanged()
    {
        var state = new O2JamHoldState();
        Assert.That(state.ResolveHead(O2JamAccuracy.None), Is.EqualTo(state));
        Assert.That(state.ResolveHead(default(O2JamResolvedJudgement)), Is.EqualTo(state));
    }

    [TestCase(false)]
    [TestCase(true)]
    public void WholeHoldCanResolveWithoutAHost(bool disableReleaseTiming)
    {
        var score = new O2JamGameplayState(O2JamDifficulty.EX);
        var state = new O2JamHoldState();
        var requested = engine.InspectHead(state, 1, map.TimeAt(1), true);
        state = state.ResolveHead(score.Apply(requested)).BeginHold();
        Assert.That(state.IsHolding, Is.True);
        requested = engine.InspectTail(state, 2, map.TimeAt(2), !disableReleaseTiming, disableReleaseTiming);
        state = state.ResolveTail(score.Apply(requested).ResolvedAccuracy);
        Assert.Multiple(() =>
        {
            Assert.That(state.IsComplete, Is.True);
            Assert.That(state.IsHolding, Is.False);
            Assert.That(state.TailAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
            Assert.That(score.Current.Score, Is.EqualTo(400));
            Assert.That(engine.InspectTail(state, 2, map.TimeAt(3), true, disableReleaseTiming), Is.EqualTo(O2JamAccuracy.None));
            Assert.That(state.BeginHold(), Is.EqualTo(state));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void BadHeadRejectsHoldEvenWhenPillRescuesItsScore(bool hasPill)
    {
        var score = new O2JamGameplayState(O2JamDifficulty.EX);
        if (hasPill)
            for (var i = 0; i < O2JamGameplayState.CoolHitsPerPill; i++)
                score.Apply(O2JamAccuracy.Cool);

        var requested = engine.InspectHead(default, 1, map.TimeAt(1 + O2JamTimingMap.TicksToPosition(20)), true);
        var resolution = score.Apply(requested);
        var state = new O2JamHoldState().ResolveHead(resolution).BeginHold();
        Assert.Multiple(() =>
        {
            Assert.That(resolution.PillConsumed, Is.EqualTo(hasPill));
            Assert.That(state.HeadAccuracy, Is.EqualTo(hasPill ? O2JamAccuracy.Cool : O2JamAccuracy.Bad));
            Assert.That(state.RequestedHeadAccuracy, Is.EqualTo(O2JamAccuracy.Bad));
            Assert.That(state.IsHolding, Is.False);
            Assert.That(state.RequiresTailMiss, Is.True);
            Assert.That(state.CanBeginHold, Is.False);
            Assert.That(engine.InspectTail(state, 2, map.TimeAt(1), false, false),
                Is.EqualTo(O2JamAccuracy.Miss));
        });
        var tail = score.Apply(engine.InspectTail(state, 2, map.TimeAt(1), false, true));
        var complete = state.ResolveTail(tail.ResolvedAccuracy);
        Assert.Multiple(() =>
        {
            Assert.That(complete.IsComplete, Is.True);
            Assert.That(complete.TailAccuracy, Is.EqualTo(O2JamAccuracy.Miss));
            Assert.That(complete.RequestedHeadAccuracy, Is.EqualTo(O2JamAccuracy.Bad));
            Assert.That(score.Current.Score, Is.EqualTo(hasPill ? 3190 : 0));
            Assert.That(score.Current.Combo, Is.EqualTo(-1));
            Assert.That(score.Current.JamCombo, Is.Zero);
            Assert.That(complete.ResolveHead(O2JamAccuracy.Cool), Is.EqualTo(complete));
            Assert.That(state.RequiresTailMiss, Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void EarlyReleaseStillMissesWithReleaseTimingDisabled(bool disabled)
    {
        var state = new O2JamHoldState().ResolveHead(O2JamAccuracy.Cool).BeginHold();
        Assert.That(engine.InspectTail(state, 2, map.TimeAt(1), true, disabled), Is.EqualTo(O2JamAccuracy.Miss));
    }

    [Test]
    public void PassiveInspectionDoesNotHitAndTimeoutMisses()
    {
        Assert.That(engine.InspectHead(default, 1, map.TimeAt(1), false), Is.EqualTo(O2JamAccuracy.None));
        Assert.That(engine.InspectHead(default, 1, map.TimeAt(2), false), Is.EqualTo(O2JamAccuracy.Miss));
        var state = new O2JamHoldState().ResolveHead(O2JamAccuracy.Good).BeginHold();
        Assert.That(engine.InspectTail(state, 2, map.TimeAt(2), false, false), Is.EqualTo(O2JamAccuracy.None));
        Assert.That(engine.InspectTail(state, 2, map.TimeAt(3), false, false), Is.EqualTo(O2JamAccuracy.Miss));
        Assert.That(engine.InspectTail(default, 2, map.TimeAt(2), true, true), Is.EqualTo(O2JamAccuracy.None));
    }

    [TestCase(-25, O2JamAccuracy.Bad)]
    [TestCase(25, O2JamAccuracy.Bad)]
    [TestCase(-25.01, O2JamAccuracy.Miss)]
    [TestCase(-18, O2JamAccuracy.Good)]
    [TestCase(6, O2JamAccuracy.Cool)]
    [TestCase(24.01, O2JamAccuracy.Bad)]
    [TestCase(25.01, O2JamAccuracy.Bad)]
    [TestCase(26, O2JamAccuracy.Miss)]
    [TestCase(6.5, O2JamAccuracy.Cool)]
    [TestCase(-6.5, O2JamAccuracy.Good)]
    public void ReleaseUsesIntegratedBpmWindow(double ticks, O2JamAccuracy expected)
    {
        var changingMap = new O2JamTimingMap(120, [new O2JamBpmEvent(1.95, 60)]);
        var changingEngine = new O2JamHoldJudgementEngine(new O2JamPositionClock(changingMap));
        var state = new O2JamHoldState().ResolveHead(O2JamAccuracy.Cool).BeginHold();
        var time = changingMap.TimeAt(2 + O2JamTimingMap.TicksToPosition(ticks));
        Assert.That(changingEngine.InspectTail(state, 2, time, true, false), Is.EqualTo(expected));
    }

    [Test]
    public void CompletedSnapshotCannotScoreAgainAndEarlierSnapshotCanBeRestored()
    {
        var holding = new O2JamHoldState().ResolveHead(O2JamAccuracy.Cool).BeginHold();
        var complete = holding.ResolveTail(O2JamAccuracy.Bad);
        Assert.That(complete.ResolveTail(O2JamAccuracy.Cool), Is.EqualTo(complete));
        Assert.That(complete.TailAccuracy, Is.EqualTo(O2JamAccuracy.Bad));
        Assert.That(engine.InspectHead(complete, 1, map.TimeAt(1), true), Is.EqualTo(O2JamAccuracy.None));
        Assert.That(engine.InspectTail(complete, 2, map.TimeAt(2), true, false), Is.EqualTo(O2JamAccuracy.None));
        Assert.That(engine.InspectTail(holding, 2, map.TimeAt(2), true, false), Is.EqualTo(O2JamAccuracy.Cool));
    }
}
