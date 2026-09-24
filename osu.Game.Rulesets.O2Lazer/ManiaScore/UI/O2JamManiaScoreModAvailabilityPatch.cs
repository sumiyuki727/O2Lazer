using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamManiaScoreModAvailabilityPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ManiaScoreModAvailability";
    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<ModSelectOverlay, UpdateGuard> updateGuards = new();
    private static PropertyInfo selectedModsProperty = null!;

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
                var selectionPostfix = AccessTools.Method(typeof(O2JamManiaScoreModAvailabilityPatch), nameof(updateAvailability));
                var colourPostfix = AccessTools.Method(typeof(O2JamManiaScoreModAvailabilityPatch), nameof(showAsDependent));
                var externalSelectionTarget = AccessTools.Method(typeof(ModSelectOverlay), "updateFromExternalSelection");
                var dependencyTarget = AccessTools.Method(typeof(UserModSelectOverlay), "ComputeNewModsFromSelection");
                var dependencyPostfix = AccessTools.Method(typeof(O2JamManiaScoreModAvailabilityPatch), nameof(enforceDependency));
                var colourTarget = AccessTools.Method(typeof(IncompatibilityDisplayingModPanel), "updateIncompatibility");
                selectedModsProperty = AccessTools.Property(typeof(IncompatibilityDisplayingModPanel), "selectedMods");
                if (selectionPostfix == null || dependencyPostfix == null || colourPostfix == null
                    || externalSelectionTarget == null || dependencyTarget == null || colourTarget == null || selectedModsProperty == null)
                    throw new MissingMemberException("The native mod selection API has changed.");

                harmony.Patch(externalSelectionTarget, postfix: new HarmonyMethod(selectionPostfix));
                harmony.Patch(dependencyTarget, postfix: new HarmonyMethod(dependencyPostfix));
                harmony.Patch(colourTarget, postfix: new HarmonyMethod(colourPostfix));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not restrict mania-only mods to Mania Score.");
                return false;
            }
        }
    }

    private static void updateAvailability(ModSelectOverlay __instance) => UpdateAvailability(__instance);

    private static void enforceDependency(UserModSelectOverlay __instance, IReadOnlyList<Mod> oldSelection,
                                          IReadOnlyList<Mod> newSelection, ref IReadOnlyList<Mod> __result)
    {
        if (!__instance.AllAvailableMods.Any(state => state.Mod is O2JamModManiaScore))
            return;

        var maniaScore = __instance.AllAvailableMods.Single(state => state.Mod is O2JamModManiaScore).Mod;
        __result = ApplyDependency(oldSelection, newSelection, __result, maniaScore);
    }

    private static void showAsDependent(IncompatibilityDisplayingModPanel __instance, BindableBool ___incompatible)
    {
        var selectedMods = (Bindable<IReadOnlyList<Mod>>)selectedModsProperty.GetValue(__instance)!;
        if (ShouldDisplayAsDependent(__instance.Mod, selectedMods.Value))
            ___incompatible.Value = true;
    }

    internal static bool ShouldDisplayAsDependent(Mod mod, IReadOnlyList<Mod> selectedMods) =>
        O2JamManiaScoreDependencyPolicy.IsDependent(mod) && !selectedMods.Any(selected => selected is O2JamModManiaScore);

    internal static void UpdateAvailability(ModSelectOverlay overlay)
    {
        var hasO2JamMods = overlay.AllAvailableMods.Any(state => state.Mod is O2JamModManiaScore);
        if (!hasO2JamMods)
            return;

        var guard = updateGuards.GetValue(overlay, _ => new UpdateGuard());
        if (guard.Active)
            return;

        guard.Active = true;
        try
        {
            var maniaScore = overlay.AllAvailableMods.Single(state => state.Mod is O2JamModManiaScore);
            var resolved = O2JamManiaScoreDependencyPolicy.EnsureManiaScore(overlay.SelectedMods.Value, maniaScore.Mod);
            if (!ReferenceEquals(resolved, overlay.SelectedMods.Value))
                overlay.SelectedMods.Value = resolved;

            // External selection updates suppress the panel's ordinary Active callbacks. Keep
            // the native state aligned with the committed list before the next user selection.
            maniaScore.Active.Value = overlay.SelectedMods.Value.Any(mod => mod is O2JamModManiaScore);
        }
        finally
        {
            guard.Active = false;
        }
    }

    internal static IReadOnlyList<Mod> ApplyDependency(IReadOnlyList<Mod> oldSelection, IReadOnlyList<Mod> newSelection,
                                                       IReadOnlyList<Mod> resolvedSelection, Mod maniaScore)
    {
        return O2JamManiaScoreDependencyPolicy.ApplySelection(oldSelection, newSelection, resolvedSelection, maniaScore);
    }

    private sealed class UpdateGuard
    {
        public bool Active;
    }
}
