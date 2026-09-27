using System;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Audio;

internal static class O2JamHeldKeySoundPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.HeldKeySound";
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
                    AccessTools.Method(typeof(O2JamHeldKeySoundPatch), nameof(suppressResolvedHold))));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony.Id);
                Logger.Error(exception, "O2Lazer could not install its resolved hold keysound adapter.");
                return false;
            }
        }
    }

    private static void suppressResolvedHold(GameplaySampleTriggerSource __instance,
                                             HitObjectLifetimeEntry? ___mostValidObject, ref HitObject? __result)
    {
        if (__instance.Parent is not O2JamManiaColumn || !ShouldSuppress(__result, ___mostValidObject))
            return;

        __result = null;
    }

    internal static bool ShouldSuppress(HitObject? selectedObject, HitObjectLifetimeEntry? selectedEntry)
    {
        if (selectedObject is not O2JamHoldNote hold || !ReferenceEquals(selectedEntry?.HitObject, hold))
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
