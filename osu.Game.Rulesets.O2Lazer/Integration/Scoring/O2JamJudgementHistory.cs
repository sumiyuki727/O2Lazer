using System.Collections.Generic;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

/// <summary>
/// Owns the association between native result identities and core resolutions. Native rollback
/// decides which results to remove; replaying requested accuracies restores pills and Jam without
/// inventing inverse operations for rules which depend on earlier hits.
/// </summary>
internal sealed class O2JamJudgementHistory(O2JamDifficulty difficulty, bool continueAfterLifeDepletion = false)
{
    private readonly List<AppliedResolution> history = [];

    public O2JamGameplayState State { get; } = new(difficulty, continueAfterLifeDepletion);

    public O2JamResolvedJudgement Resolve(O2JamJudgementResult result, O2JamAccuracy accuracy)
    {
        if (result.ResolutionApplied)
            return result.Resolution;
        if (accuracy == O2JamAccuracy.None)
            return State.Apply(accuracy);

        var resolution = State.Apply(accuracy);
        history.Add(new AppliedResolution(result, accuracy));
        result.RequestedAccuracy = accuracy;
        result.Resolution = resolution;
        result.ResolutionApplied = true;
        return resolution;
    }

    public void Revert(O2JamJudgementResult result)
    {
        if (history.RemoveAll(entry => ReferenceEquals(entry.Result, result)) == 0)
            return;

        result.ClearResolution();
        State.Reset();
        foreach (var entry in history)
            State.Apply(entry.RequestedAccuracy);
    }

    public void Reset()
    {
        foreach (var entry in history)
            entry.Result.ClearResolution();
        history.Clear();
        State.Reset();
    }

    private readonly record struct AppliedResolution(O2JamJudgementResult Result, O2JamAccuracy RequestedAccuracy);
}
