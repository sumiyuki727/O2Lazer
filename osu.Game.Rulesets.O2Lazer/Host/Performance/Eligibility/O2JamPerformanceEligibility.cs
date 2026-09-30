using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public static class O2JamPerformanceEligibility
{
    // Eligibility belongs to a selection/score, not a global MS switch. Keeping each mod's
    // native Ranked property intact also preserves setting-dependent mania restrictions.
    public static bool IsEligible(IReadOnlyList<Mod> mods)
    {
        var flattened = ModUtils.FlattenMods(mods).ToArray();
        return flattened.Any(mod => mod is O2JamModManiaScore) && flattened.All(mod => mod.Ranked);
    }
}
