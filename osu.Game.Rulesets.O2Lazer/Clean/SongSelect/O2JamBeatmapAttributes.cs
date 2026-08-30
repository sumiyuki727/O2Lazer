using System;
using System.Collections.Generic;
using System.Globalization;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Difficulty;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamBeatmapAttributes
{
    public static IEnumerable<RulesetBeatmapAttribute> Create(IBeatmapInfo beatmap)
    {
        // The identifier has no difficulty scale. Epsilon still displays as zero in osu!'s
        // numeric formatter, but keeps even o2ma0's bar full without patching its renderer.
        var songId = Math.Max(float.Epsilon, getSongId(beatmap.Metadata.Tags));
        var level = O2JamDifficultyRating.ResolveLevel(beatmap.DifficultyName, beatmap.StarRating);

        yield return new RulesetBeatmapAttribute(O2LazerStrings.O2Ma, O2LazerStrings.O2Ma.ToString(), songId, songId, songId);
        yield return new RulesetBeatmapAttribute(O2LazerStrings.O2JamLevel, O2LazerStrings.O2JamLevelAcronym.ToString(), level, level, 150);
    }

    private static uint getSongId(string tags)
    {
        foreach (var tag in tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries))
        {
            if (tag.StartsWith("o2ma", StringComparison.OrdinalIgnoreCase)
                && uint.TryParse(tag.AsSpan(4), NumberStyles.None, CultureInfo.InvariantCulture, out var songId))
                return songId;
        }

        return 0;
    }
}
