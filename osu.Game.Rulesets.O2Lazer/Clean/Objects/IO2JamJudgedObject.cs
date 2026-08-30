using System;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Objects;

public interface IO2JamJudgedObject
{
    double ChartPosition { get; set; }

    O2JamTimingMap TimingMap { get; set; }

    O2JamEndpointKind EndpointKind { get; }

    O2JamJudgement Judge(double sourceTime, bool explicitAttempt);
}

internal static class O2JamHitObjectTiming
{
    public static O2JamJudgement Judge(IO2JamJudgedObject hitObject, double sourceTime, bool explicitAttempt)
    {
        var engine = new O2JamJudgementEngine(new O2JamPositionClock(hitObject.TimingMap));
        return explicitAttempt
            ? engine.Judge(hitObject.ChartPosition, sourceTime, hitObject.EndpointKind)
            : engine.Inspect(hitObject.ChartPosition, sourceTime, hitObject.EndpointKind);
    }

    public static double MaximumJudgementOffset(IO2JamJudgedObject hitObject)
    {
        var badPosition = O2JamTimingMap.TicksToPosition(O2JamJudgementEngine.BadTicksFor(hitObject.EndpointKind));
        var targetTime = hitObject.TimingMap.TimeAt(hitObject.ChartPosition);
        var early = targetTime - hitObject.TimingMap.TimeAt(hitObject.ChartPosition - badPosition);
        var late = hitObject.TimingMap.TimeAt(hitObject.ChartPosition + badPosition) - targetTime;
        return Math.Max(early, late);
    }
}
