using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public sealed class O2JamJudgementResult(HitObject hitObject, Judgement judgement) : JudgementResult(hitObject, judgement)
{
    public O2JamAccuracy RequestedAccuracy { get; internal set; }

    public O2JamResolvedJudgement Resolution { get; internal set; }

    public bool ResolutionApplied { get; internal set; }

    internal void ClearResolution()
    {
        RequestedAccuracy = O2JamAccuracy.None;
        Resolution = default;
        ResolutionApplied = false;
    }
}
