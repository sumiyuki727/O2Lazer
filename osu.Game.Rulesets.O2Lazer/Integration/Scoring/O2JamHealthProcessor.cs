using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public sealed partial class O2JamHealthProcessor : HealthProcessor
{
    private O2JamDifficulty difficulty = O2JamDifficulty.EX;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        if (beatmap is O2JamBeatmap o2JamBeatmap)
            difficulty = o2JamBeatmap.O2JamDifficulty;

        base.ApplyBeatmap(beatmap);
    }

    protected override double GetHealthIncreaseFor(JudgementResult result)
    {
        if (result is O2JamJudgementResult { ResolutionApplied: true } o2JamResult)
            return o2JamResult.Resolution.LifeDelta / (double)O2JamGameplayState.MaximumLife;

        var accuracy = O2JamResultMapper.FromFramework(result.Type);
        return O2JamGameplayState.LifeDeltaFor(difficulty, accuracy) / (double)O2JamGameplayState.MaximumLife;
    }

    protected override bool CheckDefaultFailCondition(JudgementResult result) =>
        difficulty != O2JamDifficulty.EX && base.CheckDefaultFailCondition(result);
}
