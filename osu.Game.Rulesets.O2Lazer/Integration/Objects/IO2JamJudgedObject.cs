using System;
using O2Jam.Core;

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
        return MaximumOffset(hitObject, O2JamJudgementEngine.WindowFor(O2JamAccuracy.Bad, hitObject.EndpointKind));
    }

    public static double MaximumOffset(IO2JamJudgedObject hitObject, O2JamJudgementWindow window)
    {
        // Native lifetime/input APIs accept one symmetric envelope. The Core still decides
        // each endpoint against its separate early/late bounds, including BPM changes.
        var targetTime = hitObject.TimingMap.TimeAt(hitObject.ChartPosition);
        var early = targetTime - hitObject.TimingMap.TimeAt(hitObject.ChartPosition - O2JamTimingMap.TicksToPosition(window.EarlyTicks));
        var late = hitObject.TimingMap.TimeAt(hitObject.ChartPosition + O2JamTimingMap.TicksToPosition(window.LateTicks)) - targetTime;
        return Math.Max(early, late);
    }
}
