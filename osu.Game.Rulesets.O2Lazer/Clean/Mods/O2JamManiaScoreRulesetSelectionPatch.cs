using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.O2Lazer.Mods;

internal static class O2JamManiaScoreRulesetSelectionPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ManiaScoreRulesetSelection";
    private static readonly object installLock = new();
    private static FieldInfo gameSelectedModsField = null!;

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            var harmony = new Harmony(harmony_id);
            try
            {
                var rulesetChangedTarget = AccessTools.Method(typeof(OsuGameBase), "onRulesetChanged");
                var postfix = AccessTools.Method(typeof(O2JamManiaScoreRulesetSelectionPatch), nameof(enforceSelection));
                gameSelectedModsField = AccessTools.Field(typeof(OsuGameBase), "SelectedMods");
                if (rulesetChangedTarget == null || postfix == null || gameSelectedModsField == null)
                    throw new MissingMemberException("The native ruleset mod-conversion API has changed.");

                harmony.Patch(rulesetChangedTarget, postfix: new HarmonyMethod(postfix));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not normalise Mania Score dependencies after a ruleset switch.");
                return false;
            }
        }
    }

    private static void enforceSelection(OsuGameBase __instance)
    {
        var selectedMods = (Bindable<IReadOnlyList<Mod>>)gameSelectedModsField.GetValue(__instance)!;
        if (selectedMods.Disabled)
            return;

        var resolved = ApplySelection(selectedMods.Value,
            __instance.AvailableMods.Value.SelectMany(entry => entry.Value));
        if (!ReferenceEquals(resolved, selectedMods.Value))
            selectedMods.Value = resolved;
    }

    internal static IReadOnlyList<Mod> ApplySelection(IReadOnlyList<Mod> selection, IEnumerable<Mod> availableMods)
    {
        var maniaScore = availableMods.OfType<O2JamModManiaScore>().SingleOrDefault();
        return maniaScore == null
            ? selection
            : O2JamManiaScoreDependencyPolicy.EnsureManiaScore(selection, maniaScore.DeepClone());
    }
}
