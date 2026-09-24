using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public sealed partial class O2JamScoreProcessor : ScoreProcessor
{
    private readonly ManiaScoreProcessorAdapter mania = new();

    private O2JamJudgementHistory judgementHistory = new(O2JamDifficulty.EX);

    internal bool IsResettingComboSentinel { get; private set; }

    internal bool UsesManiaScoring => O2JamGameplayProfile.UsesManiaScore(Mods.Value);

    public IO2JamGameplayStateSource GameplayState => judgementHistory.State;

    public O2JamScoreProcessor(Ruleset ruleset)
        : base(ruleset)
    {
        ApplyNewJudgementsWhenFailed = true;
    }

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        ApplyNewJudgementsWhenFailed = !UsesManiaScoring;

        if (!UsesManiaScoring && beatmap is O2JamBeatmap o2JamBeatmap)
        {
            // The native fail override prevents exiting gameplay; the domain must also keep
            // scoring and recovering life instead of entering O2Jam's frozen post-depletion state.
            judgementHistory = new O2JamJudgementHistory(o2JamBeatmap.O2JamDifficulty, continueAfterLifeDepletion: Mods.Value.Any(mod => mod is ModNoFail));
        }

        base.ApplyBeatmap(beatmap);
    }

    protected override IEnumerable<HitObject> EnumerateHitObjects(IBeatmap beatmap) =>
        UsesManiaScoring ? mania.Enumerate(beatmap) : base.EnumerateHitObjects(beatmap);

    public O2JamResolvedJudgement Resolve(O2JamJudgementResult result, O2JamAccuracy requestedAccuracy)
    {
        var resolution = ResolveForApplication(result, requestedAccuracy);
        result.Type = O2JamResultMapper.ToFramework(resolution.ResolvedAccuracy);
        return resolution;
    }

    internal O2JamResolvedJudgement ResolveForApplication(O2JamJudgementResult result, O2JamAccuracy requestedAccuracy)
    {
        // DrawableHitObject.ApplyResult() owns the one legal transition from None to a framework
        // result, so domain state must be resolved without making HasResult true beforehand.
        var resolution = judgementHistory.Resolve(result, requestedAccuracy);

        // Successful judgements are allowed to advance the framework combo once inside
        // ScoreProcessor.ApplyResultInternal(). Breaks must be exposed before that method because
        // framework Ok is a hit while O2Jam Bad breaks combo.
        if (resolution.ResolvedAccuracy is O2JamAccuracy.Bad or O2JamAccuracy.Miss)
            syncCombo();

        return resolution;
    }

    protected override JudgementResult CreateResult(HitObject hitObject, Judgement judgement) =>
        judgement is O2JamJudgementDefinition
            ? new O2JamJudgementResult(hitObject, judgement)
            : base.CreateResult(hitObject, judgement);

    protected override void ApplyScoreChange(JudgementResult result)
    {
        if (UsesManiaScoring)
        {
            base.ApplyScoreChange(result);
            return;
        }

        if (result is not O2JamJudgementResult o2JamResult)
            return;

        if (!o2JamResult.ResolutionApplied)
            ResolveForApplication(o2JamResult, O2JamResultMapper.FromFramework(result.Type));

        syncCombo();
    }

    protected override void RemoveScoreChange(JudgementResult result)
    {
        if (UsesManiaScoring)
        {
            base.RemoveScoreChange(result);
            return;
        }

        if (result is not O2JamJudgementResult o2JamResult)
            return;

        judgementHistory.Revert(o2JamResult);
        syncCombo();
    }

    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion) =>
        UsesManiaScoring
            ? mania.Compute(comboProgress, accuracyProgress, bonusPortion, Accuracy.Value)
            : judgementHistory.State.Current.Score;

    protected override double GetComboScoreChange(JudgementResult result) =>
        UsesManiaScoring ? mania.ComboScoreChange(result) : base.GetComboScoreChange(result);

    public override int GetBaseScoreForResult(HitResult result)
    {
        if (UsesManiaScoring)
            return mania.GetBaseScoreForResult(result);

        return result switch
        {
            HitResult.Perfect => 200,
            HitResult.Good => 100,
            HitResult.Ok => 4,
            _ => 0,
        };
    }

    public override ScoreRank RankFromScore(double accuracy, IReadOnlyDictionary<HitResult, int> results)
    {
        return UsesManiaScoring
            ? mania.RankFromScore(accuracy, results)
            : base.RankFromScore(accuracy, results);
    }

    protected override void Reset(bool storeResults)
    {
        IsResettingComboSentinel = true;

        try
        {
            base.Reset(storeResults);
            judgementHistory.Reset();
            if (!UsesManiaScoring)
                syncCombo();
        }
        finally
        {
            IsResettingComboSentinel = false;
        }
    }

    public override void PopulateScore(ScoreInfo score)
    {
        base.PopulateScore(score);
        if (UsesManiaScoring)
            return;

        score.Combo = System.Math.Max(0, judgementHistory.State.Current.Combo);
        score.MaxCombo = System.Math.Max(0, judgementHistory.State.Current.MaximumCombo);
    }

    protected override void Dispose(bool isDisposing)
    {
        if (isDisposing)
            mania.Dispose();

        base.Dispose(isDisposing);
    }

    private void syncCombo()
    {
        Combo.Value = judgementHistory.State.Current.Combo;
        HighestCombo.Value = judgementHistory.State.Current.MaximumCombo;
    }
}
