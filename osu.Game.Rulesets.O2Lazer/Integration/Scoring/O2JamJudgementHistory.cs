using System.Collections.Generic;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

/// <summary>
/// Associates native result identities with core resolutions. Native rollback decides which
/// results to remove; tail rollback restores a snapshot, while unordered rollback replays
/// only the affected suffix using requested accuracies.
/// </summary>
internal sealed class O2JamJudgementHistory(O2JamDifficulty difficulty, bool continueAfterLifeDepletion = false)
{
    private readonly List<AppliedResolution> history = [];
    private Dictionary<O2JamJudgementResult, O2JamGameplaySnapshot>? rebuiltSnapshots;

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
        if (history.Count == 0)
            return;

        var index = history.Count - 1;
        if (!ReferenceEquals(history[index].Result, result))
            index = history.FindLastIndex(entry => ReferenceEquals(entry.Result, result));
        if (index < 0)
            return;

        history.RemoveAt(index);
        rebuiltSnapshots?.Remove(result);
        if (rebuiltSnapshots is { Count: 0 })
            rebuiltSnapshots = null;
        result.ClearResolution();

        if (history.Count == 0)
        {
            State.Reset();
            return;
        }

        if (index == history.Count)
        {
            State.Restore(snapshotAfter(history[^1]));
            return;
        }

        // Same-frame native results need not unwind in strict reverse order. Keep their original
        // deltas for native undo; only cache snapshots invalidated by the earlier removal.
        rebuiltSnapshots ??= new Dictionary<O2JamJudgementResult, O2JamGameplaySnapshot>(ReferenceEqualityComparer.Instance);
        var rebuilt = new O2JamGameplayState(difficulty, continueAfterLifeDepletion);
        if (index > 0)
            rebuilt.Restore(snapshotAfter(history[index - 1]));
        for (var i = index; i < history.Count; i++)
        {
            var entry = history[i];
            rebuiltSnapshots[entry.Result] = rebuilt.Apply(entry.RequestedAccuracy).State;
        }

        // HUD subscriptions stay on the authoritative state and see only the completed rollback.
        State.Restore(rebuilt.Current);
    }

    public void Reset()
    {
        foreach (var entry in history)
            entry.Result.ClearResolution();
        history.Clear();
        rebuiltSnapshots = null;
        State.Reset();
    }

    private O2JamGameplaySnapshot snapshotAfter(AppliedResolution entry) =>
        rebuiltSnapshots != null && rebuiltSnapshots.TryGetValue(entry.Result, out var snapshot)
            ? snapshot
            : entry.Result.Resolution.State;

    private readonly record struct AppliedResolution(O2JamJudgementResult Result, O2JamAccuracy RequestedAccuracy);
}
