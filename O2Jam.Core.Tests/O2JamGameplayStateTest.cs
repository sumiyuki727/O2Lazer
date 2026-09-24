using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Core;

[TestFixture]
public class O2JamGameplayStateTest
{
    [Test]
    public void FirstSuccessfulEndpointDisplaysZeroCombo()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);

        Assert.That(state.Current.Combo, Is.EqualTo(-1));
        Assert.That(state.Apply(O2JamAccuracy.Cool).State.Combo, Is.Zero);
        Assert.That(state.Apply(O2JamAccuracy.Good).State.Combo, Is.EqualTo(1));
        Assert.That(state.Current.MaximumCombo, Is.EqualTo(1));

        state.Apply(O2JamAccuracy.Bad);
        Assert.That(state.Current.Combo, Is.EqualTo(-1));
    }

    [TestCase(O2JamDifficulty.EX, O2JamAccuracy.Cool, 3)]
    [TestCase(O2JamDifficulty.EX, O2JamAccuracy.Good, 2)]
    [TestCase(O2JamDifficulty.EX, O2JamAccuracy.Bad, -10)]
    [TestCase(O2JamDifficulty.EX, O2JamAccuracy.Miss, -50)]
    [TestCase(O2JamDifficulty.NX, O2JamAccuracy.Cool, 2)]
    [TestCase(O2JamDifficulty.NX, O2JamAccuracy.Good, 1)]
    [TestCase(O2JamDifficulty.NX, O2JamAccuracy.Bad, -7)]
    [TestCase(O2JamDifficulty.NX, O2JamAccuracy.Miss, -40)]
    [TestCase(O2JamDifficulty.HX, O2JamAccuracy.Cool, 1)]
    [TestCase(O2JamDifficulty.HX, O2JamAccuracy.Good, 0)]
    [TestCase(O2JamDifficulty.HX, O2JamAccuracy.Bad, -5)]
    [TestCase(O2JamDifficulty.HX, O2JamAccuracy.Miss, -30)]
    public void NativeLifeDeltaTable(O2JamDifficulty difficulty, O2JamAccuracy accuracy, int expected)
    {
        var state = new O2JamGameplayState(difficulty);
        if (expected > 0)
            state.Apply(O2JamAccuracy.Miss);

        Assert.That(state.Apply(accuracy).LifeDelta, Is.EqualTo(expected));
    }

    [Test]
    public void FifteenCoolsAwardPillAndBadConsumesItAsCool()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        state.Apply(O2JamAccuracy.Miss);

        for (var i = 0; i < 15; i++)
            state.Apply(O2JamAccuracy.Cool);

        Assert.That(state.Current.Pills, Is.EqualTo(1));
        Assert.That(state.Current.ConsecutiveCoolProgress, Is.Zero);

        var rescued = state.Apply(O2JamAccuracy.Bad);

        Assert.That(rescued.PillConsumed, Is.True);
        Assert.That(rescued.ResolvedAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
        Assert.That(rescued.State.Pills, Is.Zero);
        Assert.That(rescued.State.Combo, Is.EqualTo(15));
        Assert.That(rescued.LifeDelta, Is.EqualTo(1));
    }

    [Test]
    public void GoodResetsPillProgressButAdvancesJam()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < 10; i++)
            state.Apply(O2JamAccuracy.Cool);

        state.Apply(O2JamAccuracy.Good);

        Assert.That(state.Current.ConsecutiveCoolProgress, Is.Zero);
        Assert.That(state.Current.JamProgress, Is.EqualTo(42));
    }

    [Test]
    public void JamBonusUsesComboActiveBeforeMeterFills()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < 25; i++)
            state.Apply(O2JamAccuracy.Cool);

        Assert.That(state.Current.JamCombo, Is.EqualTo(1));
        Assert.That(state.Current.JamProgress, Is.Zero);
        Assert.That(state.Current.Score, Is.EqualTo(5000));

        var next = state.Apply(O2JamAccuracy.Cool);
        Assert.That(next.ScoreDelta, Is.EqualTo(210));
        Assert.That(next.State.Score, Is.EqualTo(5210));
    }

    [Test]
    public void BreakResetsCurrentJamButPreservesMaximum()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < 50; i++)
            state.Apply(O2JamAccuracy.Good);

        state.Apply(O2JamAccuracy.Bad);

        Assert.That(state.Current.JamCombo, Is.Zero);
        Assert.That(state.Current.JamProgress, Is.Zero);
        Assert.That(state.Current.MaximumJamCombo, Is.EqualTo(1));
    }

    [Test]
    public void ExContinuesComboButFreezesScoreAndJamAfterLifeDepletion()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.EX);
        var maximumBeforeDeath = state.Current.MaximumCombo;
        for (var i = 0; i < 20; i++)
            state.Apply(O2JamAccuracy.Miss);

        Assert.That(state.Current.Life, Is.Zero);
        Assert.That(state.Current.ScoringEnabled, Is.False);
        Assert.That(state.Current.HasFailed, Is.False);

        var firstAfterDeath = state.Apply(O2JamAccuracy.Cool);
        var secondAfterDeath = state.Apply(O2JamAccuracy.Cool);

        Assert.That(firstAfterDeath.ScoreDelta, Is.Zero);
        Assert.That(firstAfterDeath.LifeDelta, Is.Zero);
        Assert.That(firstAfterDeath.State.Combo, Is.Zero);
        Assert.That(secondAfterDeath.State.Combo, Is.EqualTo(1));
        Assert.That(secondAfterDeath.State.MaximumCombo, Is.EqualTo(maximumBeforeDeath));
        Assert.That(secondAfterDeath.State.JamProgress, Is.Zero);
    }

    [Test]
    public void NxFailsWhenLifeReachesZero()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.NX);
        for (var i = 0; i < 25; i++)
            state.Apply(O2JamAccuracy.Miss);

        Assert.That(state.Current.Life, Is.Zero);
        Assert.That(state.Current.ScoringEnabled, Is.False);
        Assert.That(state.Current.HasFailed, Is.True);
    }

    [TestCase(O2JamAccuracy.None, O2JamHoldHeadOutcome.Ignore)]
    [TestCase(O2JamAccuracy.Cool, O2JamHoldHeadOutcome.BeginHold)]
    [TestCase(O2JamAccuracy.Good, O2JamHoldHeadOutcome.BeginHold)]
    [TestCase(O2JamAccuracy.Bad, O2JamHoldHeadOutcome.EndWithMiss)]
    [TestCase(O2JamAccuracy.Miss, O2JamHoldHeadOutcome.EndWithMiss)]
    public void HoldHeadOutcomeIsIndependentFromPresentation(O2JamAccuracy accuracy, O2JamHoldHeadOutcome expected)
    {
        Assert.That(O2JamHoldRules.ResolveHead(accuracy), Is.EqualTo(expected));
    }
}
