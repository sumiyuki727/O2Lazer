using System;
using System.Globalization;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Presentation.DifficultyLevels.Policy;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

internal static class O2JamStarRatingMetadata
{
    public const string O2JamTagPrefix = "o2lazer-o2jam-stars:";
    public const string ManiaVersionPrefix = "o2lazer-mania-version:";
    public const string ManiaMaxComboPrefix = "o2lazer-mania-max-combo:";
    private const string o2jam_current_prefix = O2JamTagPrefix + "1:";

    public static string ManiaVersionTag { get; } = $"{ManiaVersionPrefix}1:{O2JamManiaStarRating.Version}";

    private static string maniaMaxComboCurrentPrefix => $"{ManiaMaxComboPrefix}1:{O2JamManiaStarRating.CacheVersion}:";

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

    public static bool HasCurrentManiaVersion(string tags)
    {
        var tokens = Array.FindAll(splitTags(tags), tag => tag.StartsWith(ManiaVersionPrefix, StringComparison.Ordinal));
        return tokens.Length == 1 && tokens[0] == ManiaVersionTag;
    }

    public static string CreateManiaMaxComboTag(int maxCombo) =>
        maniaMaxComboCurrentPrefix + Math.Max(0, maxCombo).ToString(CultureInfo.InvariantCulture);

    public static int? ReadManiaMaxCombo(string tags)
    {
        var tokens = Array.FindAll(splitTags(tags), tag => tag.StartsWith(ManiaMaxComboPrefix, StringComparison.Ordinal));
        return tokens.Length == 1 && tokens[0].StartsWith(maniaMaxComboCurrentPrefix, StringComparison.Ordinal)
               && int.TryParse(tokens[0].AsSpan(maniaMaxComboCurrentPrefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var maxCombo)
            ? maxCombo : null;
    }

    public static int ResolveManiaMaxCombo(IBeatmapInfo beatmap) =>
        ReadManiaMaxCombo(beatmap.Metadata.Tags)
        ?? (beatmap.TotalObjectCount < 0 || beatmap.EndTimeObjectCount < 0
            ? 0
            : Math.Max(0, beatmap.TotalObjectCount + beatmap.EndTimeObjectCount - 1));

    public static double? ReadMania(IBeatmapInfo beatmap) =>
        double.IsFinite(beatmap.StarRating) && beatmap.StarRating >= 0
        && (HasCurrentManiaVersion(beatmap.Metadata.Tags)
            || beatmap.Ruleset is RulesetInfo ruleset && ruleset.LastAppliedDifficultyVersion == O2JamManiaStarRating.CacheVersion)
            ? beatmap.StarRating
            : null;

    public static double GetO2JamStars(IBeatmapInfo beatmap) =>
        O2JamImportMetadata.Read(beatmap.Metadata.Tags, out var metadata) == O2JamImportMetadataStatus.Valid
            ? O2JamDifficultyRating.FromLevel(metadata!.Level)
            : ReadO2Jam(beatmap.Metadata.Tags) ?? O2JamDifficultyRating.FromLevel(resolveLegacyLevel(beatmap));

    public static ushort ResolveLevel(IBeatmapInfo beatmap) =>
        O2JamImportMetadata.Read(beatmap.Metadata.Tags, out var metadata) == O2JamImportMetadataStatus.Valid
            ? metadata!.Level : resolveLegacyLevel(beatmap);

    private static ushort resolveLegacyLevel(IBeatmapInfo beatmap)
    {
        // Only pre-migration entries used native StarRating for level / 10. Never infer an
        // O2Jam level from a mania rating if the difficulty name is absent or has been edited.
        var nativeContainsMania = beatmap.Metadata.Tags.Contains(ManiaVersionPrefix, StringComparison.Ordinal)
                                 || beatmap.Ruleset is RulesetInfo ruleset && ruleset.LastAppliedDifficultyVersion >= O2JamManiaStarRating.CacheVersion;
        var fallback = ReadO2Jam(beatmap.Metadata.Tags) ?? (nativeContainsMania ? -1 : beatmap.StarRating);
        return O2JamDifficultyRating.ResolveLevel(beatmap.DifficultyName, fallback);
    }

    public static int ResolveChartOrder(IBeatmapInfo beatmap)
    {
        if (O2JamImportMetadata.Read(beatmap.Metadata.Tags, out var metadata) == O2JamImportMetadataStatus.Valid)
            return (int)metadata!.Difficulty;

        var name = beatmap.DifficultyName.TrimStart();
        if (name.StartsWith("EX", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (name.StartsWith("NX", StringComparison.OrdinalIgnoreCase))
            return 1;
        if (name.StartsWith("HX", StringComparison.OrdinalIgnoreCase))
            return 2;

        return 3;
    }

    private static string[] splitTags(string tags) => tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);
}
