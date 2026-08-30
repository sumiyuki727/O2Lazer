namespace osu.Game.Rulesets.O2Lazer.Core;

/// <summary>
/// Supplies integrated chart position to judgement. Future rate-changing mods replace or wrap
/// this clock instead of applying a multiplier to an already-computed millisecond hit window.
/// </summary>
public interface IO2JamPositionClock
{
    double PositionAt(double elapsedRealTime);

    double EffectiveBpmAt(double elapsedRealTime);
}

public sealed class O2JamPositionClock(O2JamTimingMap timingMap, double playbackRate = 1) : IO2JamPositionClock
{
    public double PositionAt(double elapsedRealTime) => timingMap.PositionAt(elapsedRealTime, playbackRate);

    public double EffectiveBpmAt(double elapsedRealTime) => timingMap.EffectiveBpmAtTime(elapsedRealTime, playbackRate);
}

/// <summary>
/// Transforms an immutable chart before gameplay. Mirror and Random can implement this contract
/// without introducing mod branches into judgement or drawable code.
/// </summary>
public interface IO2JamChartTransform<TChart>
{
    TChart Transform(TChart chart);
}
