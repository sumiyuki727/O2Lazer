namespace O2Jam.Core;

public enum O2JamHoldHeadOutcome
{
    Ignore,
    BeginHold,
    EndWithMiss,
}

/// <summary>
/// Keeps hold sequencing policy independent from the mania drawable used to present it.
/// Hold continuation uses the original head accuracy even if a pill changes its score to COOL.
/// </summary>
public static class O2JamHoldRules
{
    public static O2JamHoldHeadOutcome ResolveHead(O2JamAccuracy requestedAccuracy) => requestedAccuracy switch
    {
        O2JamAccuracy.Cool or O2JamAccuracy.Good => O2JamHoldHeadOutcome.BeginHold,
        O2JamAccuracy.Bad or O2JamAccuracy.Miss => O2JamHoldHeadOutcome.EndWithMiss,
        _ => O2JamHoldHeadOutcome.Ignore,
    };
}
