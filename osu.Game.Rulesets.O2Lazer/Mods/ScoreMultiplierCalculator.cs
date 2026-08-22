using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Mods;

internal sealed class O2LazerScoreMultiplierCalculator : ScoreMultiplierCalculator
{
    public O2LazerScoreMultiplierCalculator(ScoreMultiplierContext context)
        : base(context)
    {
        // O2Jam's native score is determined only by judgements and jam combo.
    }
}
