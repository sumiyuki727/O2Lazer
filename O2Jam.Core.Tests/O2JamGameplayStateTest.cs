using NUnit.Framework;
using O2Jam.Core;

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
        Assert.That(rescued.RequestedAccuracy, Is.EqualTo(O2JamAccuracy.Bad));
        Assert.That(rescued.State.ConsecutiveCoolProgress, Is.Zero);
        Assert.That(rescued.State.Combo, Is.EqualTo(15));
        Assert.That(rescued.LifeDelta, Is.EqualTo(1));

        for (var i = 0; i < 14; i++)
            state.Apply(O2JamAccuracy.Cool);
        Assert.That(state.Current.Pills, Is.Zero);
        Assert.That(state.Current.ConsecutiveCoolProgress, Is.EqualTo(14));
        state.Apply(O2JamAccuracy.Cool);
        Assert.That(state.Current.Pills, Is.EqualTo(1));
        Assert.That(state.Current.ConsecutiveCoolProgress, Is.Zero);
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

        Assert.That(state.Current.JamCombo, Is.Zero);
        Assert.That(state.Current.JamProgress, Is.EqualTo(100));
        Assert.That(state.Current.Score, Is.EqualTo(5000));

        var promotion = state.Apply(O2JamAccuracy.Cool);
        Assert.That(promotion.ScoreDelta, Is.EqualTo(200));
        Assert.That(promotion.State.Score, Is.EqualTo(5200));
        Assert.That(promotion.State.JamCombo, Is.EqualTo(1));
        Assert.That(promotion.State.JamProgress, Is.EqualTo(4));
        var next = state.Apply(O2JamAccuracy.Cool);
        Assert.That(next.ScoreDelta, Is.EqualTo(210));
        Assert.That(next.State.Score, Is.EqualTo(5410));
    }

    [TestCase(O2JamAccuracy.Cool, 51, 2, 4, 10450)]
    [TestCase(O2JamAccuracy.Cool, 76, 3, 4, 15950)]
    [TestCase(O2JamAccuracy.Good, 50, 0, 100, 5000)]
    [TestCase(O2JamAccuracy.Good, 51, 1, 2, 5100)]
    [TestCase(O2JamAccuracy.Good, 101, 2, 2, 10350)]
    public void LaterJamPromotionsPreserveCumulativeThresholds(O2JamAccuracy accuracy, int count,
                                                            int jam, int progress, long score)
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < count; i++)
            state.Apply(accuracy);

        Assert.Multiple(() =>
        {
            Assert.That(state.Current.JamCombo, Is.EqualTo(jam));
            Assert.That(state.Current.MaximumJamCombo, Is.EqualTo(jam));
            Assert.That(state.Current.JamProgress, Is.EqualTo(progress));
            Assert.That(state.Current.Score, Is.EqualTo(score));
        });
    }

    [Test]
    public void MixedCoolsAndGoodsMustExceedTheJamThreshold()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < 24; i++)
            state.Apply(O2JamAccuracy.Cool);
        state.Apply(O2JamAccuracy.Good);
        state.Apply(O2JamAccuracy.Good);
        Assert.That(state.Current.JamCombo, Is.Zero);
        Assert.That(state.Current.JamProgress, Is.EqualTo(100));

        var promotion = state.Apply(O2JamAccuracy.Good);
        Assert.That(promotion.ScoreDelta, Is.EqualTo(100));
        Assert.That(promotion.State.JamCombo, Is.EqualTo(1));
        Assert.That(promotion.State.JamProgress, Is.EqualTo(2));
        Assert.That(state.Apply(O2JamAccuracy.Cool).State.Score, Is.EqualTo(5310));
    }

    [Test]
    public void RescuedBadCanPromoteJamWithoutStartingAnotherPillStreak()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < 25; i++)
            state.Apply(O2JamAccuracy.Cool);

        var rescued = state.Apply(O2JamAccuracy.Bad);
        Assert.Multiple(() =>
        {
            Assert.That(rescued.PillConsumed, Is.True);
            Assert.That(rescued.ScoreDelta, Is.EqualTo(200));
            Assert.That(rescued.State.Score, Is.EqualTo(5200));
            Assert.That(rescued.State.JamCombo, Is.EqualTo(1));
            Assert.That(rescued.State.JamProgress, Is.EqualTo(4));
            Assert.That(rescued.State.ConsecutiveCoolProgress, Is.Zero);
        });
    }

    [Test]
    public void BreakResetsCurrentJamButPreservesMaximum()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.HX);
        for (var i = 0; i < 51; i++)
            state.Apply(O2JamAccuracy.Good);

        state.Apply(O2JamAccuracy.Bad);

        Assert.That(state.Current.JamCombo, Is.Zero);
        Assert.That(state.Current.JamProgress, Is.Zero);
        Assert.That(state.Current.MaximumJamCombo, Is.EqualTo(1));
    }

    [Test]
    public void ExRecordsScoreComboJamAndPillsWithLifeLockedAtZero()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.EX);
        for (var i = 0; i < 20; i++)
            state.Apply(O2JamAccuracy.Miss);

        Assert.Multiple(() =>
        {
            Assert.That(state.Current.Life, Is.Zero);
            Assert.That(state.Current.LifeLockedAtZero, Is.True);
            Assert.That(state.Current.ScoringEnabled, Is.True);
            Assert.That(state.Current.HasFailed, Is.False);
        });

        for (var i = 0; i < 51; i++)
            Assert.That(state.Apply(O2JamAccuracy.Cool).LifeDelta, Is.Zero);

        Assert.Multiple(() =>
        {
            Assert.That(state.Current.Score, Is.EqualTo(10450));
            Assert.That(state.Current.Combo, Is.EqualTo(50));
            Assert.That(state.Current.MaximumCombo, Is.EqualTo(50));
            Assert.That(state.Current.JamCombo, Is.EqualTo(2));
            Assert.That(state.Current.MaximumJamCombo, Is.EqualTo(2));
            Assert.That(state.Current.JamProgress, Is.EqualTo(4));
            Assert.That(state.Current.Pills, Is.EqualTo(3));
            Assert.That(state.Current.ConsecutiveCoolProgress, Is.EqualTo(6));
        });

        var rescued = state.Apply(O2JamAccuracy.Bad);
        Assert.Multiple(() =>
        {
            Assert.That(rescued.PillConsumed, Is.True);
            Assert.That(rescued.ResolvedAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
            Assert.That(rescued.ScoreDelta, Is.EqualTo(220));
            Assert.That(rescued.LifeDelta, Is.Zero);
            Assert.That(rescued.State.Pills, Is.EqualTo(2));
            Assert.That(rescued.State.ConsecutiveCoolProgress, Is.Zero);
            Assert.That(rescued.State.MaximumCombo, Is.EqualTo(51));
        });

        var missed = state.Apply(O2JamAccuracy.Miss);
        Assert.Multiple(() =>
        {
            Assert.That(missed.ScoreDelta, Is.EqualTo(-10));
            Assert.That(missed.State.Combo, Is.EqualTo(-1));
            Assert.That(missed.State.JamCombo, Is.Zero);
            Assert.That(missed.State.JamProgress, Is.Zero);
            Assert.That(missed.State.MaximumJamCombo, Is.EqualTo(2));
            Assert.That(missed.State.Life, Is.Zero);
            Assert.That(missed.State.HasFailed, Is.False);
        });
    }

    [Test]
    public void ResetUnlocksExLifeForTheNextAttempt()
    {
        var state = new O2JamGameplayState(O2JamDifficulty.EX);
        for (var i = 0; i < 20; i++)
            state.Apply(O2JamAccuracy.Miss);
        state.Apply(O2JamAccuracy.Cool);
        state.Reset();

        Assert.Multiple(() =>
        {
            Assert.That(state.Current.Life, Is.EqualTo(O2JamGameplayState.MaximumLife));
            Assert.That(state.Current.LifeLockedAtZero, Is.False);
            Assert.That(state.Current.Score, Is.Zero);
            Assert.That(state.Current.MaximumCombo, Is.Zero);
            Assert.That(state.Current.Pills, Is.Zero);
        });
        state.Apply(O2JamAccuracy.Miss);
        Assert.That(state.Apply(O2JamAccuracy.Cool).LifeDelta, Is.EqualTo(3));
    }

    [TestCase(O2JamDifficulty.NX, 25)]
    [TestCase(O2JamDifficulty.HX, 34)]
    public void NxAndHxStopScoringWhenLifeReachesZero(O2JamDifficulty difficulty, int misses)
    {
        var state = new O2JamGameplayState(difficulty);
        for (var i = 0; i < misses; i++)
            state.Apply(O2JamAccuracy.Miss);

        var failed = state.Current;
        Assert.Multiple(() =>
        {
            Assert.That(failed.Life, Is.Zero);
            Assert.That(failed.LifeLockedAtZero, Is.True);
            Assert.That(failed.ScoringEnabled, Is.False);
            Assert.That(failed.HasFailed, Is.True);
            Assert.That(state.Apply(O2JamAccuracy.Cool).State, Is.EqualTo(failed));
        });
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
