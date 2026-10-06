using System;
using System.Linq;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Audio;

internal static class O2JamResolvedKeySoundPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ResolvedKeySound";
    private static readonly object installLock = new();

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
                var target = AccessTools.Method(typeof(GameplaySampleTriggerSource), "GetMostValidObject", Type.EmptyTypes);
                if (target == null)
                    throw new MissingMethodException("The native gameplay sample selector is unavailable.");

                harmony.Patch(target, postfix: new HarmonyMethod(
                    AccessTools.Method(typeof(O2JamResolvedKeySoundPatch), nameof(suppressResolvedObject))));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony.Id);
                Logger.Error(exception, "O2Lazer could not install its resolved object keysound adapter.");
                return false;
            }
        }
    }

    private static void suppressResolvedObject(GameplaySampleTriggerSource __instance,
                                               HitObjectLifetimeEntry? ___mostValidObject, ref HitObject? __result)
    {
        if (__instance.Parent is not O2JamManiaColumn || !ShouldSuppress(__result, ___mostValidObject))
            return;

        __result = null;
    }

    internal static bool ShouldSuppress(HitObject? selectedObject, HitObjectLifetimeEntry? selectedEntry)
    {
        if (selectedEntry == null || !ReferenceEquals(selectedEntry.HitObject, selectedObject))
            return false;

        // Mania can fall back to the last judged tap when the column has no nearby future note.
        // MS replaces the object type, but preserves its OJM sample identity.
        if (selectedObject is O2JamNote || selectedObject is Note && selectedObject.Samples.Any(sample => sample is O2JamHitSampleInfo))
            return selectedEntry.Judged;

        if (selectedObject is not HoldNote hold
            || hold is not O2JamHoldNote && !hold.Samples.Any(sample => sample is O2JamHitSampleInfo))
            return false;

        // Mania's freeform fallback can select the LN parent after its head has sounded.
        // The parent still carries the head sample for conversion/preview, so suppress only
        // this repeated fallback; its original head judgement remains on the native path.
        foreach (var nested in selectedEntry.NestedEntries)
        {
            if (ReferenceEquals(nested.HitObject, hold.Head))
                return nested.Judged;
        }

        return selectedEntry.Judged;
    }
}
