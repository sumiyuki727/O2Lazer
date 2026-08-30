using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public sealed partial class O2JamScoreProcessor : ScoreProcessor
{
    private readonly List<AppliedResolution> resolutionHistory = [];

    private O2JamDifficulty difficulty = O2JamDifficulty.EX;
    private O2JamGameplayState gameplayState = new(O2JamDifficulty.EX);

    internal bool IsResettingComboSentinel { get; private set; }

    public IO2JamGameplayStateSource GameplayState => gameplayState;

    public O2JamScoreProcessor(Ruleset ruleset)
        : base(ruleset)
    {
        ApplyNewJudgementsWhenFailed = true;
    }

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        if (beatmap is O2JamBeatmap o2JamBeatmap)
        {
            difficulty = o2JamBeatmap.O2JamDifficulty;
            gameplayState = new O2JamGameplayState(difficulty);
        }

        base.ApplyBeatmap(beatmap);
    }

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
        if (result.ResolutionApplied)
            return result.Resolution;

        var resolution = gameplayState.Apply(requestedAccuracy);
        resolutionHistory.Add(new AppliedResolution(result, requestedAccuracy));

        result.RequestedAccuracy = requestedAccuracy;
        result.Resolution = resolution;
        result.ResolutionApplied = true;

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
        if (result is not O2JamJudgementResult o2JamResult)
            return;

        if (!o2JamResult.ResolutionApplied)
            ResolveForApplication(o2JamResult, O2JamResultMapper.FromFramework(result.Type));

        syncCombo();
    }

    protected override void RemoveScoreChange(JudgementResult result)
    {
        if (result is not O2JamJudgementResult o2JamResult)
            return;

        resolutionHistory.RemoveAll(entry => ReferenceEquals(entry.Result, o2JamResult));
        o2JamResult.ClearResolution();
        rebuildGameplayState();
    }

    protected override double ComputeTotalScore(double comboProgress, double accuracyProgress, double bonusPortion) =>
        gameplayState.Current.Score;

    public override int GetBaseScoreForResult(HitResult result) => result switch
    {
        HitResult.Perfect => 200,
        HitResult.Good => 100,
        HitResult.Ok => 4,
        _ => 0,
    };

    protected override void Reset(bool storeResults)
    {
        IsResettingComboSentinel = true;

        try
        {
            base.Reset(storeResults);
            foreach (var resolution in resolutionHistory)
                resolution.Result.ClearResolution();

            resolutionHistory.Clear();
            gameplayState.Reset();
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
        score.Combo = System.Math.Max(0, gameplayState.Current.Combo);
        score.MaxCombo = System.Math.Max(0, gameplayState.Current.MaximumCombo);
        score.TotalScore = score.TotalScoreWithoutMods = gameplayState.Current.Score;
    }

    private void rebuildGameplayState()
    {
        gameplayState.Reset();

        foreach (var resolution in resolutionHistory)
            gameplayState.Apply(resolution.RequestedAccuracy);

        syncCombo();
    }

    private void syncCombo()
    {
        Combo.Value = gameplayState.Current.Combo;
        HighestCombo.Value = gameplayState.Current.MaximumCombo;
    }

    private readonly record struct AppliedResolution(O2JamJudgementResult Result, O2JamAccuracy RequestedAccuracy);
}
