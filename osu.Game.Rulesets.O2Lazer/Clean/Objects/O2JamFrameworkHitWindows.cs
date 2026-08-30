using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Objects;

internal sealed class O2JamFrameworkHitWindows(IO2JamJudgedObject hitObject) : HitWindows
{
    public override bool IsHitResultAllowed(HitResult result) => result is
        HitResult.Perfect or HitResult.Good or HitResult.Ok or HitResult.Miss;

    public override void SetDifficulty(double difficulty)
    {
    }

    public override double WindowFor(HitResult result)
    {
        var ticks = result switch
        {
            HitResult.Perfect => O2JamJudgementEngine.CoolTicks,
            HitResult.Good => O2JamJudgementEngine.GoodTicks,
            HitResult.Ok or HitResult.Miss => O2JamJudgementEngine.BadTicksFor(hitObject.EndpointKind),
            _ => 0,
        };

        var distance = O2JamTimingMap.TicksToPosition(ticks);
        var targetTime = hitObject.TimingMap.TimeAt(hitObject.ChartPosition);
        var early = targetTime - hitObject.TimingMap.TimeAt(hitObject.ChartPosition - distance);
        var late = hitObject.TimingMap.TimeAt(hitObject.ChartPosition + distance) - targetTime;
        return System.Math.Max(early, late);
    }
}
