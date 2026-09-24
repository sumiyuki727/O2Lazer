using System.Collections.Generic;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

/// <summary>
/// Keeps access to protected native Mania scoring hooks in the Mania Score route.
/// </summary>
internal sealed partial class ManiaScoreProcessorAdapter : ManiaScoreProcessor
{
    public IEnumerable<HitObject> Enumerate(IBeatmap beatmap) => base.EnumerateHitObjects(beatmap);

    public double Compute(double comboProgress, double accuracyProgress, double bonusPortion, double accuracy)
    {
        Accuracy.Value = accuracy;
        return base.ComputeTotalScore(comboProgress, accuracyProgress, bonusPortion);
    }

    public double ComboScoreChange(JudgementResult result) => base.GetComboScoreChange(result);
}
