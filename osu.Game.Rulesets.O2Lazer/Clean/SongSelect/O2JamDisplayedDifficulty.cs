using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Difficulty;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamDisplayedDifficulty
{
    public static double GetStars(IBeatmapInfo beatmap) =>
        O2JamStarRatingMetadata.ReadMania(beatmap) ?? -1;
}
