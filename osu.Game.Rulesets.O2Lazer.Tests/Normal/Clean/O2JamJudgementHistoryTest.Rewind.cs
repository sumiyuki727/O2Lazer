using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;
using osu.Game.Rulesets.O2Lazer.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamJudgementHistoryTest
{
    [Test]
    public void TailRevertsPublishOnlyTheRestoredState()
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.HX);
        var source = history.State;
        var entries = new List<(O2JamJudgementResult Result, O2JamGameplaySnapshot Before)>();
        for (var i = 0; i < 100; i++)
        {
            var item = result();
            entries.Add((item, source.Current));
            history.Resolve(item, O2JamAccuracy.Cool);
        }

        var notifications = new List<O2JamGameplaySnapshot>();
        source.StateChanged += notifications.Add;
        for (var i = entries.Count - 1; i >= 0; i--)
        {
            notifications.Clear();
            history.Revert(entries[i].Result);
            Assert.Multiple(() =>
            {
                Assert.That(history.State, Is.SameAs(source));
                Assert.That(source.Current, Is.EqualTo(entries[i].Before));
                Assert.That(notifications, Is.EqualTo(new[] { entries[i].Before }), $"Revert at {i}");
            });
        }
    }

    [Test]
    public void UnorderedRevertRebuildsRequestedAccuraciesWithoutChangingNativeResults()
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.HX);
        var cools = Enumerable.Range(0, 15).Select(_ => result()).ToArray();
        foreach (var cool in cools)
            history.Resolve(cool, O2JamAccuracy.Cool);
        var bad = result();
        var rescued = history.Resolve(bad, O2JamAccuracy.Bad);
        var tail = result();
        history.Resolve(tail, O2JamAccuracy.Good);
        var notifications = new List<O2JamGameplaySnapshot>();
        history.State.StateChanged += notifications.Add;

        history.Revert(cools[7]);
        var expected = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < 14; i++)
            expected.Apply(O2JamAccuracy.Cool);
        var beforeBad = expected.Current;
        expected.Apply(O2JamAccuracy.Bad);
        var afterBad = expected.Current;
        expected.Apply(O2JamAccuracy.Good);
        Assert.Multiple(() =>
        {
            Assert.That(rescued.PillConsumed, Is.True);
            Assert.That(bad.Resolution, Is.EqualTo(rescued), "Native undo needs the originally applied deltas.");
            Assert.That(bad.ResolutionApplied, Is.True);
            Assert.That(history.State.Current, Is.EqualTo(expected.Current));
            Assert.That(notifications, Is.EqualTo(new[] { expected.Current }));
        });

        history.Revert(tail);
        Assert.That(history.State.Current, Is.EqualTo(afterBad));
        history.Revert(bad);
        Assert.That(history.State.Current, Is.EqualTo(beforeBad));
        Assert.That(history.Resolve(bad, O2JamAccuracy.Bad).PillConsumed, Is.False);
        history.Revert(bad);
        Assert.That(history.State.Current, Is.EqualTo(beforeBad));
    }

    [TestCase(O2JamDifficulty.EX, false)]
    [TestCase(O2JamDifficulty.NX, false)]
    [TestCase(O2JamDifficulty.HX, false)]
    [TestCase(O2JamDifficulty.EX, true)]
    [TestCase(O2JamDifficulty.NX, true)]
    [TestCase(O2JamDifficulty.HX, true)]
    public void MixedRollbackOrderMatchesAFullReplay(O2JamDifficulty difficulty, bool noFail)
    {
        var history = new O2JamJudgementHistory(difficulty, noFail);
        var entries = new List<(O2JamJudgementResult Result, O2JamAccuracy Accuracy, O2JamResolvedJudgement Resolution)>();
        var accuracies = Enumerable.Repeat(O2JamAccuracy.Cool, 30)
                                  .Concat([O2JamAccuracy.Bad, O2JamAccuracy.Good])
                                  .Concat(Enumerable.Repeat(O2JamAccuracy.Miss, 40))
                                  .Concat(Enumerable.Repeat(O2JamAccuracy.Cool, 51))
                                  .Concat([O2JamAccuracy.Bad, O2JamAccuracy.Miss]);
        foreach (var accuracy in accuracies)
        {
            var item = result();
            entries.Add((item, accuracy, history.Resolve(item, accuracy)));
        }

        var notifications = new List<O2JamGameplaySnapshot>();
        history.State.StateChanged += notifications.Add;
        // Native rollback may remove simultaneous column/hold results in a different order.
        int[] rollbackIndices = [15, entries.Count - 2, 45, 0, 1, 60, 20];
        foreach (var index in rollbackIndices)
            revert(index);

        var newTail = result();
        entries.Add((newTail, O2JamAccuracy.Good, history.Resolve(newTail, O2JamAccuracy.Good)));
        while (entries.Count > 0)
            revert(entries.Count - 1);

        void revert(int index)
        {
            var removed = entries[index].Result;
            entries.RemoveAt(index);
            notifications.Clear();
            history.Revert(removed);
            var expected = new O2JamGameplayState(difficulty, noFail);
            foreach (var entry in entries)
            {
                expected.Apply(entry.Accuracy);
                Assert.That(entry.Result.Resolution, Is.EqualTo(entry.Resolution));
            }
            Assert.Multiple(() =>
            {
                Assert.That(removed.ResolutionApplied, Is.False);
                Assert.That(history.State.Current, Is.EqualTo(expected.Current));
                Assert.That(notifications, Is.EqualTo(new[] { expected.Current }));
            });
        }
    }

    [Test]
    public void ResetDiscardsRebuiltSnapshotsBeforeReusingNativeResultIdentities()
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.HX);
        var entries = Enumerable.Range(0, 3).Select(_ => result()).ToArray();
        foreach (var item in entries)
            history.Resolve(item, O2JamAccuracy.Cool);
        history.Revert(entries[0]);
        history.Reset();
        Assert.That(entries.All(item => !item.ResolutionApplied), Is.True);

        history.Resolve(entries[1], O2JamAccuracy.Good);
        var before = history.State.Current;
        history.Resolve(entries[2], O2JamAccuracy.Miss);
        history.Revert(entries[2]);
        Assert.That(history.State.Current, Is.EqualTo(before));
        history.Revert(entries[1]);
        Assert.That(history.State.Current, Is.EqualTo(new O2JamGameplayState(O2JamDifficulty.HX).Current));
    }

    [Test]
    public void UnknownRepeatedAndEmptyRevertsDoNotNotifyOrChangeReservations()
    {
        var history = new O2JamJudgementHistory(O2JamDifficulty.HX);
        var item = result();
        history.Resolve(item, O2JamAccuracy.Cool);
        var notifications = 0;
        history.State.StateChanged += _ => notifications++;
        var before = history.State.Current;
        history.Revert(result());
        Assert.That(history.State.Current, Is.EqualTo(before));
        Assert.That(item.ResolutionApplied, Is.True);
        Assert.That(notifications, Is.Zero);
        history.Revert(item);
        history.Revert(item);
        history.Revert(result());
        Assert.That(notifications, Is.EqualTo(1));
    }
}
