namespace osu.Game.Rulesets.O2Lazer.Core;

public enum O2JamHoldHeadOutcome
{
    Ignore,
    BeginHold,
    EndWithMiss,
}

/// <summary>
/// Keeps hold sequencing policy independent from the mania drawable used to present it.
/// Pill conversion happens first, so a rescued BAD arrives here as COOL and may begin the hold.
/// </summary>
public static class O2JamHoldRules
{
    public static O2JamHoldHeadOutcome ResolveHead(O2JamAccuracy resolvedAccuracy) => resolvedAccuracy switch
    {
        O2JamAccuracy.Cool or O2JamAccuracy.Good => O2JamHoldHeadOutcome.BeginHold,
        O2JamAccuracy.Bad or O2JamAccuracy.Miss => O2JamHoldHeadOutcome.EndWithMiss,
        _ => O2JamHoldHeadOutcome.Ignore,
    };
}
