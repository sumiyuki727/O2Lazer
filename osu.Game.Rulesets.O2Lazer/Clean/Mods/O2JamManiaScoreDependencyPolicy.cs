using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.O2Lazer.Mods;

internal static class O2JamManiaScoreDependencyPolicy
{
    public static bool IsDependent(Mod mod) => mod is IO2JamManiaScoreDependentMod;

    public static IReadOnlyList<Mod> ApplySelection(IReadOnlyList<Mod> oldSelection, IReadOnlyList<Mod> newSelection,
                                                   IReadOnlyList<Mod> resolvedSelection, Mod maniaScore)
    {
        var removedManiaScore = oldSelection.Any(mod => mod is O2JamModManiaScore)
                                && !newSelection.Any(mod => mod is O2JamModManiaScore);
        if (removedManiaScore)
            return resolvedSelection.Where(mod => !IsDependent(mod)).ToArray();

        return EnsureManiaScore(resolvedSelection, maniaScore);
    }

    public static IReadOnlyList<Mod> EnsureManiaScore(IReadOnlyList<Mod> selection, Mod maniaScore) =>
        selection.Any(IsDependent) && !selection.Any(mod => mod is O2JamModManiaScore)
            ? selection.Append(maniaScore).ToArray()
            : selection;
}
