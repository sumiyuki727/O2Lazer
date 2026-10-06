using System;
using O2Jam.Core;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

internal sealed record O2JamNativeDifficultyResult(
    Guid BeatmapId, Guid SetId, string BeatmapHash, string Md5Hash, string SourceHash,
    O2JamDifficulty Difficulty, double StarRating, int MaxCombo);

/// <summary>
/// Carries the attributes discarded by native startup processing to its synchronous star write.
/// </summary>
internal static class O2JamNativeDifficultyHandoff
{
    // Keep only one value per calculation thread, never a Realm model or decoded chart.
    // Unrelated writes cannot consume it and a later calculation replaces it.
    [ThreadStatic]
    private static O2JamNativeDifficultyResult? pending;

    internal static void Publish(O2JamNativeDifficultyResult? result) => pending = result;

    internal static O2JamNativeDifficultyResult? Take(Guid beatmapId)
    {
        if (pending?.BeatmapId != beatmapId)
            return null;
        var result = pending;
        pending = null;
        return result;
    }
}
