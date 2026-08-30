using System;

namespace osu.Game.Rulesets.O2Lazer.Core;

public readonly record struct O2JamJudgement(
    O2JamAccuracy Accuracy,
    O2JamEndpointKind Endpoint,
    double TargetPosition,
    double InputPosition,
    double OffsetTicks,
    double EffectiveBpm);

/// <summary>
/// Native O2Jam distance judgement. Windows are expressed in OJN ticks so BPM changes and future
/// playback-rate transforms are handled by the position clock rather than cached per-note times.
/// </summary>
public sealed class O2JamJudgementEngine(IO2JamPositionClock clock)
{
    public const double CoolTicks = 6;
    public const double GoodTicks = 18;
    public const double TapAndHeadBadTicks = 25;
    public const double ReleaseBadTicks = 24;

    private const double boundary_epsilon = 1e-7;

    public O2JamJudgement Judge(double targetPosition, double inputTime, O2JamEndpointKind endpoint)
    {
        var inputPosition = clock.PositionAt(inputTime);
        var offsetTicks = O2JamTimingMap.PositionToTicks(inputPosition - targetPosition);
        var accuracy = accuracyForOffset(offsetTicks, endpoint, explicitAttempt: true);

        return new O2JamJudgement(
            accuracy,
            endpoint,
            targetPosition,
            inputPosition,
            offsetTicks,
            clock.EffectiveBpmAt(inputTime));
    }

    public O2JamJudgement Inspect(double targetPosition, double currentTime, O2JamEndpointKind endpoint)
    {
        var inputPosition = clock.PositionAt(currentTime);
        var offsetTicks = O2JamTimingMap.PositionToTicks(inputPosition - targetPosition);
        var accuracy = accuracyForOffset(offsetTicks, endpoint, explicitAttempt: false);

        return new O2JamJudgement(
            accuracy,
            endpoint,
            targetPosition,
            inputPosition,
            offsetTicks,
            clock.EffectiveBpmAt(currentTime));
    }

    public static double BadTicksFor(O2JamEndpointKind endpoint) =>
        endpoint == O2JamEndpointKind.HoldRelease ? ReleaseBadTicks : TapAndHeadBadTicks;

    private static O2JamAccuracy accuracyForOffset(double offsetTicks, O2JamEndpointKind endpoint, bool explicitAttempt)
    {
        var absolute = Math.Abs(offsetTicks);

        if (absolute <= CoolTicks + boundary_epsilon)
            return O2JamAccuracy.Cool;
        if (absolute <= GoodTicks + boundary_epsilon)
            return O2JamAccuracy.Good;
        if (absolute <= BadTicksFor(endpoint) + boundary_epsilon)
            return O2JamAccuracy.Bad;

        if (offsetTicks > 0 || (explicitAttempt && endpoint == O2JamEndpointKind.HoldRelease))
            return O2JamAccuracy.Miss;

        return O2JamAccuracy.None;
    }
}
