using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public sealed class O2JamScoreMultiplierCalculator : ScoreMultiplierCalculator
{
    public O2JamScoreMultiplierCalculator(ScoreMultiplierContext context)
        : base(context)
    {
        // Native multiplier lookup uses exact types, so localised wrappers need their own entries.
        Single<O2JamModNoFail>(hasMultiplier: 0.5);
        Single<O2JamModHalfTime>(hasMultiplier: halfTime => rateAdjustMultiplier(halfTime.SpeedChange.Value));
        Single<O2JamModDaycore>(hasMultiplier: daycore => rateAdjustMultiplier(daycore.SpeedChange.Value));
        Single<O2JamModNoRelease>(hasMultiplier: 0.9);
        Single<O2JamModConstantSpeed>(hasMultiplier: 0.9);
        Single<O2JamModWindUp>(hasMultiplier: 0.5);
        Single<O2JamModWindDown>(hasMultiplier: 0.5);
        Single<O2JamModAdaptiveSpeed>(hasMultiplier: 0.5);
    }

    private static double rateAdjustMultiplier(double speedChange)
    {
        var value = (int)(speedChange * 10) / 10.0 - 1;
        return speedChange >= 1 ? 1 + value / 5 : 0.6 + value;
    }
}
