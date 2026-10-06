using O2Jam.Core;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

internal static class O2JamHitErrorProjection
{
    internal static double OffsetTicks(JudgementResult result)
    {
        var hitObject = (IO2JamJudgedObject)result.HitObject;
        // Native offsets already use the chart clock. Multiplying by GameplayRate again would
        // distort DT/HT, and using only the endpoint BPM would lose changes inside the window.
        var inputTime = result.HitObject.GetEndTime() + result.TimeOffset;
        return O2JamTimingMap.PositionToTicks(hitObject.TimingMap.PositionAt(inputTime) - hitObject.ChartPosition);
    }
}
