using System;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Logging;
using osu.Framework.Threading;
using osu.Game.Graphics.Carousel;
using osu.Game.Beatmaps;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamCarouselTransitionPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.CarouselTransition";
    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<BeatmapCarousel, TransitionState> states = new();
    private static readonly ConditionalWeakTable<FilterControl, CriteriaUpdate> criteriaUpdates = new();
    private static MethodInfo updateCriteria = null!;
    private static MethodInfo rulesetGetter = null!;
    private static FieldInfo currentCriteriaField = null!;
    private static MethodInfo schedulerGetter = null!;
    private static FieldInfo loadingDebounceField = null!;

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
                var filter = AccessTools.Method(typeof(BeatmapCarousel), nameof(BeatmapCarousel.Filter));
                var acquire = AccessTools.Method(typeof(BeatmapCarousel), "GetDrawableForDisplay");
                var completion = FindCompletion();
                updateCriteria = AccessTools.Method(typeof(FilterControl), "updateCriteria");
                rulesetGetter = AccessTools.PropertyGetter(typeof(FilterControl), "ruleset");
                currentCriteriaField = AccessTools.Field(typeof(FilterControl), "currentCriteria");
                schedulerGetter = AccessTools.PropertyGetter(typeof(Drawable), "Scheduler");
                loadingDebounceField = AccessTools.Field(typeof(BeatmapCarousel), "loadingDebounce");
                if (filter == null || acquire == null || completion == null
                    || updateCriteria == null || rulesetGetter == null || currentCriteriaField == null
                    || schedulerGetter == null || loadingDebounceField == null)
                    throw new MissingMemberException("The native carousel transition API has changed.");

                harmony.Patch(filter, prefix: new HarmonyMethod(typeof(O2JamCarouselTransitionPatch), nameof(beginFilter)));
                harmony.Patch(completion, prefix: new HarmonyMethod(typeof(O2JamCarouselTransitionPatch), nameof(completeFilter)));
                harmony.Patch(acquire, postfix: new HarmonyMethod(typeof(O2JamCarouselTransitionPatch), nameof(prepareEntrance)));
                harmony.Patch(updateCriteria, prefix: new HarmonyMethod(typeof(O2JamCarouselTransitionPatch), nameof(coalesceCriteria)));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its carousel transitions.");
                return false;
            }
        }
    }

    internal static MethodInfo? FindCompletion() => AccessTools.GetDeclaredMethods(typeof(BeatmapCarousel))
        .SingleOrDefault(method => method.Name.StartsWith("<Filter>b__", StringComparison.Ordinal)
                                   && method.GetParameters().Length == 0
                                   && PatchProcessor.GetOriginalInstructions(method)
                                       .Any(instruction => instruction.operand is MethodInfo called && called.Name == "Hide"));

    private static bool coalesceCriteria(FilterControl __instance, bool __0)
    {
        var state = criteriaUpdates.GetOrCreateValue(__instance);
        if (state.Executing)
            return true;

        var previous = (currentCriteriaField.GetValue(__instance) as FilterCriteria)?.Ruleset?.ShortName;
        var current = ((IBindable<RulesetInfo>)rulesetGetter.Invoke(__instance, null)!).Value?.ShortName;
        if (!state.Queued && !(previous != null && previous != current
                              && (previous == O2LazerIdentity.ShortName || current == O2LazerIdentity.ShortName)))
            return true;

        state.ClearScopedSet |= __0;
        if (state.Queued)
            return false;

        state.Queued = true;
        // Ruleset, available groups, mods and slider bindings change in the same frame.
        // Publish only their final combination; do not restart the list for each intermediate one.
        ((Scheduler)schedulerGetter.Invoke(__instance, null)!).Add(() =>
        {
            var clearScopedSet = state.ClearScopedSet;
            state.Queued = state.ClearScopedSet = false;
            state.Executing = true;
            try
            {
                updateCriteria.Invoke(__instance, [clearScopedSet]);
            }
            finally
            {
                state.Executing = false;
            }
        });
        return false;
    }

    private static void beginFilter(BeatmapCarousel __instance, FilterCriteria __0, ref bool __1)
    {
        var state = states.GetOrCreateValue(__instance);
        var previous = __instance.Criteria?.Ruleset?.ShortName;
        var current = __0.Ruleset?.ShortName;
        var switching = previous != null && previous != current
                        && (previous == O2LazerIdentity.ShortName || current == O2LazerIdentity.ShortName);
        state.Pending |= switching;
        state.AnimateNewPanels = state.Pending;
        if (state.Pending)
        {
            __1 = true;
            if (switching)
            {
                // A previous search may still own the delayed indicator. A mode switch
                // must not inherit that delay before the native loading feedback starts.
                (loadingDebounceField.GetValue(__instance) as ScheduledDelegate)?.Cancel();
                loadingDebounceField.SetValue(__instance, null);
            }
        }
    }

    private static bool completeFilter(BeatmapCarousel __instance)
    {
        if (!states.TryGetValue(__instance, out var state) || !state.Pending)
            return true;

        // A cancelled native request also schedules this callback. Only the current filter
        // may hide the shared spinner and restore the list's brightness.
        if (__instance.IsFiltering)
            return false;

        state.Pending = false;
        return true;
    }

    private static void prepareEntrance(BeatmapCarousel __instance, Drawable __result)
    {
        if (states.TryGetValue(__instance, out var state) && state.AnimateNewPanels
            && __result is PanelBeatmap or PanelBeatmapStandalone or PanelBeatmapSet)
        {
            // New pool entries start opaque, unlike recycled entries. Let PrepareForUse
            // run the same native fade-in for both, without disturbing retained panels.
            __result.Alpha = 0;
        }
    }

    private sealed class TransitionState
    {
        public bool Pending;
        public bool AnimateNewPanels;
    }

    private sealed class CriteriaUpdate
    {
        public bool Queued;
        public bool Executing;
        public bool ClearScopedSet;
    }
}
