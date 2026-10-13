using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Core;

public partial class O2JamGameplayStateTest
{
    [TestCase(O2JamDifficulty.EX, false)]
    [TestCase(O2JamDifficulty.NX, false)]
    [TestCase(O2JamDifficulty.HX, false)]
    [TestCase(O2JamDifficulty.EX, true)]
    [TestCase(O2JamDifficulty.NX, true)]
    [TestCase(O2JamDifficulty.HX, true)]
    public void RestoredSnapshotsPreserveEveryFieldAndFutureResolution(O2JamDifficulty difficulty, bool noFail)
    {
        var baseline = new O2JamGameplayState(difficulty, noFail);
        var restored = new O2JamGameplayState(difficulty, noFail);
        var notifications = new List<O2JamGameplaySnapshot>();
        restored.StateChanged += notifications.Add;
        var accuracies = Enumerable.Repeat(O2JamAccuracy.Cool, 51)
                                  .Concat([O2JamAccuracy.Bad, O2JamAccuracy.Good])
                                  .Concat(Enumerable.Repeat(O2JamAccuracy.Miss, 40))
                                  .Concat(Enumerable.Repeat(O2JamAccuracy.Cool, 15));
        foreach (var accuracy in accuracies)
        {
            // Restore into a dirty state so a missing field cannot pass through a shared prefix.
            restored.Apply(O2JamAccuracy.Bad);
            notifications.Clear();
            var snapshot = baseline.Current;
            restored.Restore(snapshot);
            Assert.That(notifications, Is.EqualTo(new[] { snapshot }));
            Assert.That(restored.Current, Is.EqualTo(snapshot));
            Assert.That(restored.Apply(accuracy), Is.EqualTo(baseline.Apply(accuracy)));
        }
    }

    [TestCase(O2JamDifficulty.EX, false, 20)]
    [TestCase(O2JamDifficulty.NX, false, 25)]
    [TestCase(O2JamDifficulty.HX, false, 34)]
    [TestCase(O2JamDifficulty.EX, true, 20)]
    [TestCase(O2JamDifficulty.NX, true, 25)]
    [TestCase(O2JamDifficulty.HX, true, 34)]
    public void RestoringBeforeDepletionUnlocksLifeAndRestoringAfterRetainsPolicy(O2JamDifficulty difficulty, bool noFail, int misses)
    {
        var state = new O2JamGameplayState(difficulty, noFail);
        for (var i = 0; i < misses - 1; i++)
            state.Apply(O2JamAccuracy.Miss);
        var before = state.Current;
        var depleted = state.Apply(O2JamAccuracy.Miss).State;
        state.Restore(before);
        var recovered = state.Apply(O2JamAccuracy.Cool);
        Assert.Multiple(() =>
        {
            Assert.That(recovered.LifeDelta, Is.EqualTo(O2JamGameplayState.LifeDeltaFor(difficulty, O2JamAccuracy.Cool)));
            Assert.That(recovered.State.ScoringEnabled, Is.True);
            Assert.That(recovered.State.HasFailed, Is.False);
            Assert.That(recovered.State.LifeLockedAtZero, Is.False);
        });
        state.Restore(depleted);
        var next = state.Apply(O2JamAccuracy.Cool);
        Assert.Multiple(() =>
        {
            Assert.That(next.LifeDelta, Is.EqualTo(noFail ? O2JamGameplayState.LifeDeltaFor(difficulty, O2JamAccuracy.Cool) : 0));
            Assert.That(next.State.ScoringEnabled, Is.EqualTo(noFail || difficulty == O2JamDifficulty.EX));
            Assert.That(next.State.HasFailed, Is.EqualTo(!noFail && difficulty != O2JamDifficulty.EX));
            Assert.That(next.State.LifeLockedAtZero, Is.EqualTo(!noFail));
        });
    }
}
