using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Mods;

internal static class O2JamGameplayProfile
{
    public static IReadOnlyList<Mod> Flatten(IEnumerable<Mod>? mods) =>
        mods == null ? [] : ModUtils.FlattenMods(mods).ToArray();

    public static bool UsesManiaScore(IEnumerable<Mod>? mods) =>
        Flatten(mods).Any(mod => mod is O2JamModManiaScore);

    public static bool RequiresStarCalculation(IEnumerable<Mod>? mods) =>
        Flatten(mods).Any(mod => mod is IApplicableToRate or O2JamModInvert);

    public static bool UsesLevelPresentation(IBeatmapInfo? beatmap, IEnumerable<Mod>? mods) =>
        beatmap?.Ruleset.ShortName == O2LazerIdentity.ShortName && !UsesManiaScore(mods);
}
