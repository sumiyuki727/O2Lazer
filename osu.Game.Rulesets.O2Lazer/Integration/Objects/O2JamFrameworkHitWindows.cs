using O2Jam.Core;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Objects;

internal sealed class O2JamFrameworkHitWindows(IO2JamJudgedObject hitObject) : HitWindows
{
    public override bool IsHitResultAllowed(HitResult result) => result is
        HitResult.Perfect or HitResult.Good or HitResult.Meh or HitResult.Miss;

    public override void SetDifficulty(double difficulty)
    {
    }

    public override double WindowFor(HitResult result)
    {
        var accuracy = result switch
        {
            HitResult.Perfect => O2JamAccuracy.Cool,
            HitResult.Good => O2JamAccuracy.Good,
            HitResult.Meh or HitResult.Miss => O2JamAccuracy.Bad,
            _ => O2JamAccuracy.None,
        };
        return accuracy == O2JamAccuracy.None ? 0
            : O2JamHitObjectTiming.MaximumOffset(hitObject, O2JamJudgementEngine.WindowFor(accuracy, hitObject.EndpointKind));
    }
}
