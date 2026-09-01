using System;
using System.Globalization;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

internal static class O2JamStarRatingMetadata
{
    public const string O2JamTagPrefix = "o2lazer-o2jam-stars:";
    public const string ManiaVersionPrefix = "o2lazer-mania-version:";
    private const string o2jam_current_prefix = O2JamTagPrefix + "1:";

    public static string ManiaVersionTag { get; } = $"{ManiaVersionPrefix}1:{O2JamManiaStarRating.Version}";

    public static string CreateO2JamTag(ushort level) =>
        o2jam_current_prefix + O2JamDifficultyRating.FromLevel(level).ToString("R", CultureInfo.InvariantCulture);

    public static double? ReadO2Jam(string tags)
    {
        foreach (var tag in splitTags(tags))
        {
            if (tag.StartsWith(o2jam_current_prefix, StringComparison.Ordinal)
                && double.TryParse(tag.AsSpan(o2jam_current_prefix.Length), NumberStyles.Float, CultureInfo.InvariantCulture, out var stars)
                && double.IsFinite(stars) && stars >= 0 && stars <= O2JamDifficultyRating.FromLevel(ushort.MaxValue))
                return stars;
        }

        return null;
    }

    public static bool HasCurrentManiaVersion(string tags) => Array.IndexOf(splitTags(tags), ManiaVersionTag) >= 0;

    public static double? ReadMania(IBeatmapInfo beatmap) =>
        double.IsFinite(beatmap.StarRating) && beatmap.StarRating >= 0
        && (HasCurrentManiaVersion(beatmap.Metadata.Tags)
            || beatmap.Ruleset is RulesetInfo ruleset && ruleset.LastAppliedDifficultyVersion == O2JamManiaStarRating.CacheVersion)
            ? beatmap.StarRating
            : null;

    public static double GetO2JamStars(IBeatmapInfo beatmap) =>
        ReadO2Jam(beatmap.Metadata.Tags) ?? O2JamDifficultyRating.FromLevel(ResolveLevel(beatmap));

    public static ushort ResolveLevel(IBeatmapInfo beatmap)
    {
        // Only pre-migration entries used native StarRating for level / 10. Never infer an
        // O2Jam level from a mania rating if the difficulty name is absent or has been edited.
        var nativeContainsMania = beatmap.Metadata.Tags.Contains(ManiaVersionPrefix, StringComparison.Ordinal)
                                 || beatmap.Ruleset is RulesetInfo ruleset && ruleset.LastAppliedDifficultyVersion >= O2JamManiaStarRating.CacheVersion;
        var fallback = ReadO2Jam(beatmap.Metadata.Tags) ?? (nativeContainsMania ? -1 : beatmap.StarRating);
        return O2JamDifficultyRating.ResolveLevel(beatmap.DifficultyName, fallback);
    }

    private static string[] splitTags(string tags) => tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
