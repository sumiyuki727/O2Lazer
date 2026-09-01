using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamDisplayedDifficulty
{
    public static bool UsesManiaStars(IEnumerable<Mod>? mods) => mods?.Any(mod => mod is O2JamModManiaScore) == true;

    public static double GetStars(IBeatmapInfo beatmap, IEnumerable<Mod>? mods) =>
        UsesManiaStars(mods) ? O2JamStarRatingMetadata.ReadMania(beatmap) ?? -1 : O2JamStarRatingMetadata.GetO2JamStars(beatmap);
}
