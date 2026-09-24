using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public static class O2JamResultMapper
{
    public static HitResult ToFramework(O2JamAccuracy accuracy) => accuracy switch
    {
        O2JamAccuracy.Cool => HitResult.Perfect,
        O2JamAccuracy.Good => HitResult.Good,
        O2JamAccuracy.Bad => HitResult.Ok,
        O2JamAccuracy.Miss => HitResult.Miss,
        _ => HitResult.None,
    };

    public static O2JamAccuracy FromFramework(HitResult result) => result switch
    {
        HitResult.Perfect => O2JamAccuracy.Cool,
        HitResult.Good => O2JamAccuracy.Good,
        HitResult.Ok => O2JamAccuracy.Bad,
        HitResult.Miss => O2JamAccuracy.Miss,
        _ => O2JamAccuracy.None,
    };
}
