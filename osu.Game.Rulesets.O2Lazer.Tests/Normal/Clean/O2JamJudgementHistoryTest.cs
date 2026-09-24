using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamJudgementHistoryTest
{
    [Test]
    public void RewindingPillConsumptionRestoresPreviousState()
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.HX);
        for (var i = 0; i < O2JamGameplayState.CoolHitsPerPill; i++)
            history.Resolve(result(), O2JamAccuracy.Cool);
        var before = history.State.Current;
        var bad = result();
        Assert.That(history.Resolve(bad, O2JamAccuracy.Bad).PillConsumed, Is.True);
        history.Revert(bad);
        Assert.That(history.State.Current, Is.EqualTo(before));
        Assert.That(bad.ResolutionApplied, Is.False);
        Assert.That(history.Resolve(bad, O2JamAccuracy.Bad).PillConsumed, Is.True);
    }

    [TestCase(false)]
    [TestCase(true)]
    public void NativeRollbackRestoresSnapshotsAcrossLifeDepletion(bool noFail)
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.HX, noFail);
        var entries = new List<(O2JamJudgementResult Result, O2JamGameplaySnapshot Before)>();
        for (var i = 0; i < 36; i++)
        {
            var item = result();
            entries.Add((item, history.State.Current));
            history.Resolve(item, O2JamAccuracy.Miss);
        }
        Assert.That(history.State.Current.HasFailed, Is.EqualTo(!noFail));
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            history.Revert(entries[i].Result);
            Assert.That(history.State.Current, Is.EqualTo(entries[i].Before));
        }
    }

    [Test]
    public void ResetClearsReservationsAndAllowsRetry()
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.EX);
        var note = result();
        history.Resolve(note, O2JamAccuracy.Cool);
        history.Reset();
        Assert.That(note.ResolutionApplied, Is.False);
        Assert.That(history.State.Current.Combo, Is.EqualTo(-1));
        Assert.That(history.Resolve(note, O2JamAccuracy.Good).State.Score, Is.EqualTo(100));
    }

    [Test]
    public void EmptyAndRepeatedAttemptsDoNotReserveOrScoreTwice()
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.EX);
        var note = result();
        history.Resolve(note, O2JamAccuracy.None);
        Assert.That(note.ResolutionApplied, Is.False);
        var first = history.Resolve(note, O2JamAccuracy.Cool);
        Assert.That(history.Resolve(note, O2JamAccuracy.Bad), Is.EqualTo(first));
        Assert.That(history.State.Current.Score, Is.EqualTo(200));
        Assert.That(note.HasResult, Is.False);
    }

    private static O2JamJudgementResult result()
    {
        var note = new O2JamNote();
        return new O2JamJudgementResult(note, note.CreateJudgement());
    }
}
