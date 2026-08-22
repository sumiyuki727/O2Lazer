using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.O2Jam;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public partial class O2LazerHealthProcessor : HealthProcessor
{
    public bool HasEverFailed { get; private set; }

    private bool isO2Jam;
    private OjnDifficulty o2JamDifficulty;

    public override void ApplyBeatmap(IBeatmap beatmap)
    {
        isO2Jam = beatmap is O2LazerBeatmap { LayoutVariant: O2LazerLayoutVariant.O2Jam7K };
        o2JamDifficulty = O2JamScoring.DifficultyFor(beatmap.BeatmapInfo);
        base.ApplyBeatmap(beatmap);
    }

    public void RegisterEmptyPoor(double? eventTime = null)
    {
    }

    public void ApplySyntheticLongNoteEndpoint(JudgementResult result) => ApplyResult(result);

    public void ApplyHellChargeTick(bool holding, double scale = 0.5, double? eventTime = null)
    {
    }

    public bool HasPassedAtEnd() => o2JamDifficulty == OjnDifficulty.EX || Health.Value > 0;

    protected override void Reset(bool storeResults)
    {
        base.Reset(storeResults);
        Health.MinValue = 0;
        Health.MaxValue = 1;
        Health.Value = 1;
        HasEverFailed = false;
    }

    protected override void ApplyResultInternal(JudgementResult result)
    {
        base.ApplyResultInternal(result);

        if (!HasEverFailed && Health.Value <= 0 && o2JamDifficulty != OjnDifficulty.EX)
            HasEverFailed = true;
    }

    protected override HitResult GetSimulatedHitResult(Judgement judgement) => judgement.MaxResult == HitResult.Meh
        ? HitResult.IgnoreMiss
        : base.GetSimulatedHitResult(judgement);

    protected override double GetHealthIncreaseFor(JudgementResult result)
        => isO2Jam ? O2JamScoring.LifeDelta(o2JamDifficulty, result.Type) / O2JamScoring.MaximumLife : 0;

    protected override bool CheckDefaultFailCondition(JudgementResult result)
        => o2JamDifficulty != OjnDifficulty.EX && Health.Value <= Health.MinValue;
}
