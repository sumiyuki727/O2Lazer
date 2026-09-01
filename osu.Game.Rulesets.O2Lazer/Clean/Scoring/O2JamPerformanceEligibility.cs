using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.Scoring;

public static class O2JamPerformanceEligibility
{
    // Eligibility belongs to a selection/score, not a global MS switch. Keeping each mod's
    // native Ranked property intact also preserves setting-dependent mania restrictions.
    public static bool IsEligible(IReadOnlyList<Mod> mods) =>
        mods.Any(mod => mod is O2JamModManiaScore) && mods.All(mod => mod.Ranked);
}
