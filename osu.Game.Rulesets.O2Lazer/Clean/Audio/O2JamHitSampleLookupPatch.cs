using System;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Audio;

internal static class O2JamHitSampleLookupPatch
{
    private static readonly object installLock = new();

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                var target = AccessTools.Method(typeof(BeatmapSkinProvidingContainer), "AllowSampleLookup", [typeof(ISampleInfo)]);
                if (target == null)
                    throw new MissingMethodException("The native beatmap sample lookup gate is unavailable.");

                new Harmony("osu.Game.Rulesets.O2Lazer.HitSampleLookup").Patch(target,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(O2JamHitSampleLookupPatch), nameof(allowKeySound))));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not install its keysound lookup adapter.");
                return false;
            }
        }
    }

    private static void allowKeySound(ISampleInfo sampleInfo, ISkin ___skin, ref bool __result)
    {
        // OJM keysounds are musical voices, not optional beatmap hit effects. Keep the
        // native gate for every other sample and skin without changing the global setting.
        if (sampleInfo is O2JamHitSampleInfo && ___skin is O2JamBeatmapSkin)
            __result = true;
    }
}
