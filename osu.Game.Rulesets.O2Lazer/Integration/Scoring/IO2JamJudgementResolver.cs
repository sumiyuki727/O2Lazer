using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

/// <summary>
/// Lets the bridge obtain pill-adjusted accuracy without owning the host's scoring route.
/// The native drawable must still commit the returned result through ApplyResult.
/// </summary>
internal interface IO2JamJudgementResolver
{
    O2JamAccuracy ResolveAccuracyForApplication(O2JamJudgementResult result, O2JamAccuracy requestedAccuracy);
}
