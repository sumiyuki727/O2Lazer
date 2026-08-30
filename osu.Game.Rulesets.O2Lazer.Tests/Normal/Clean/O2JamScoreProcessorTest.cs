using System.Collections.Generic;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamScoreProcessorTest
{
    [Test]
    public void DrawableResolutionDoesNotCommitFrameworkResultEarly()
    {
        var processor = createProcessor(O2JamDifficulty.HX);
        var result = createResult();

        var resolution = processor.ResolveForApplication(result, O2JamAccuracy.Cool);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.ResolvedAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
            Assert.That(result.ResolutionApplied, Is.True);
            Assert.That(result.HasResult, Is.False);
            Assert.That(result.Type, Is.EqualTo(HitResult.None));
        });
    }

    [Test]
    public void DrawableResolutionUsesAuthoritativeComboState()
    {
        var processor = createProcessor(O2JamDifficulty.HX);

        var first = createResult();
        var second = createResult();

        Assert.That(processor.Combo.Value, Is.EqualTo(-1));

        processor.Resolve(first, O2JamAccuracy.Cool);
        processor.ApplyResult(first);

        Assert.Multiple(() =>
        {
            Assert.That(processor.GameplayState.Current.Combo, Is.Zero);
            Assert.That(processor.Combo.Value, Is.Zero);
        });

        processor.Resolve(second, O2JamAccuracy.Good);
        processor.ApplyResult(second);

        Assert.Multiple(() =>
        {
            Assert.That(first.Type, Is.EqualTo(HitResult.Perfect));
            Assert.That(processor.GameplayState.Current.Combo, Is.EqualTo(1));
            Assert.That(processor.Combo.Value, Is.EqualTo(1));
            Assert.That(processor.Accuracy.Value, Is.EqualTo(0.75).Within(0.000001));
        });
    }

    [Test]
    public void StoredScoreNeverExposesComboSentinel()
    {
        var processor = createProcessor(O2JamDifficulty.HX);
        var score = new ScoreInfo(ruleset: new O2LazerRuleset().RulesetInfo);

        processor.PopulateScore(score);

        Assert.Multiple(() =>
        {
            Assert.That(processor.Combo.Value, Is.EqualTo(-1));
            Assert.That(score.Combo, Is.Zero);
            Assert.That(score.MaxCombo, Is.Zero);
        });
    }

    [Test]
    public void ComboHudAdapterPreservesNativeIncrementAndBreakTransitions()
    {
        var source = new O2JamGameplayState(O2JamDifficulty.HX);
        var adapter = new O2JamDisplayedComboAdapter(source);
        var visibleChanges = new List<int>();
        adapter.Current.BindValueChanged(change => visibleChanges.Add(change.NewValue));

        source.Apply(O2JamAccuracy.Cool);
        source.Apply(O2JamAccuracy.Good);
        source.Apply(O2JamAccuracy.Cool);
        source.Apply(O2JamAccuracy.Miss);

        Assert.Multiple(() =>
        {
            Assert.That(visibleChanges, Is.EqualTo(new[] { 1, 2, 0 }));
            Assert.That(adapter.Current.Value, Is.Zero);
        });
    }

    [Test]
    public void SuccessfulJudgementsEmitNoFrameworkComboRollback()
    {
        var processor = createProcessor(O2JamDifficulty.HX);
        var comboChanges = new List<int>();
        processor.Combo.BindValueChanged(change => comboChanges.Add(change.NewValue));

        var first = createResult();
        processor.Resolve(first, O2JamAccuracy.Cool);
        processor.ApplyResult(first);

        var second = createResult();
        processor.Resolve(second, O2JamAccuracy.Good);
        processor.ApplyResult(second);

        Assert.Multiple(() =>
        {
            Assert.That(comboChanges, Is.EqualTo(new[] { 0, 1 }));
            Assert.That(processor.Combo.Value, Is.EqualTo(1));
        });
    }

    [Test]
    public void PillConversionChangesFrameworkResultBeforeHealthProcessing()
    {
        var processor = createProcessor(O2JamDifficulty.HX);

        for (var i = 0; i < 15; i++)
            processor.Resolve(createResult(), O2JamAccuracy.Cool);

        var bad = createResult();
        var resolution = processor.Resolve(bad, O2JamAccuracy.Bad);

        Assert.Multiple(() =>
        {
            Assert.That(resolution.PillConsumed, Is.True);
            Assert.That(resolution.ResolvedAccuracy, Is.EqualTo(O2JamAccuracy.Cool));
            Assert.That(bad.Type, Is.EqualTo(HitResult.Perfect));
        });
    }

    [Test]
    public void RevertedResultCanBeResolvedAgain()
    {
        var processor = createProcessor(O2JamDifficulty.HX);
        var result = createResult();

        processor.Resolve(result, O2JamAccuracy.Cool);
        processor.ApplyResult(result);
        processor.RevertResult(result);

        Assert.Multiple(() =>
        {
            Assert.That(result.ResolutionApplied, Is.False);
            Assert.That(processor.GameplayState.Current.Score, Is.Zero);
            Assert.That(processor.GameplayState.Current.Combo, Is.EqualTo(-1));
        });

        processor.Resolve(result, O2JamAccuracy.Good);
        Assert.Multiple(() =>
        {
            Assert.That(result.ResolutionApplied, Is.True);
            Assert.That(result.RequestedAccuracy, Is.EqualTo(O2JamAccuracy.Good));
            Assert.That(processor.GameplayState.Current.Score, Is.EqualTo(100));
        });
    }

    private static O2JamScoreProcessor createProcessor(O2JamDifficulty difficulty)
    {
        var processor = new O2JamScoreProcessor(new O2LazerRuleset());
        processor.ApplyBeatmap(new O2JamBeatmap(difficulty, new O2JamTimingMap(120)));
        return processor;
    }

    private static O2JamJudgementResult createResult()
    {
        var note = new O2JamNote();
        return new O2JamJudgementResult(note, note.CreateJudgement());
    }
}
