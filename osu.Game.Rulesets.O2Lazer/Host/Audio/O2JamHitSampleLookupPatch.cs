using System;
using HarmonyLib;
using osu.Framework.Audio.Sample;
using osu.Framework.Graphics.Audio;
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

            var harmony = new Harmony("osu.Game.Rulesets.O2Lazer.HitSampleLookup");
            try
            {
                var target = AccessTools.Method(typeof(BeatmapSkinProvidingContainer), "AllowSampleLookup", [typeof(ISampleInfo)]);
                var getChannel = AccessTools.Method(typeof(DrawableSample), nameof(DrawableSample.GetChannel));
                if (target == null || getChannel == null)
                    throw new MissingMethodException("The native beatmap sample lookup gate is unavailable.");

                harmony.Patch(target,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(O2JamHitSampleLookupPatch), nameof(allowKeySound))));
                harmony.Patch(getChannel,
                    postfix: new HarmonyMethod(AccessTools.Method(typeof(O2JamHitSampleLookupPatch), nameof(bindPlaybackChannel))));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony.Id);
                Logger.Error(exception, "O2Lazer could not install its keysound lookup adapter.");
                return false;
            }
        }
    }

    private static void bindPlaybackChannel(DrawableSample __instance, SampleChannel __result) =>
        O2JamHitSoundRateAdjustments.BindChannel(__instance, __result);

    private static void allowKeySound(ISampleInfo sampleInfo, ISkin ___skin, ref bool __result)
    {
        // OJM keysounds are musical voices, not optional beatmap hit effects. Keep the
        // native gate for every other sample and skin without changing the global setting.
        if (sampleInfo is O2JamHitSampleInfo && ___skin is O2JamBeatmapSkin skin)
            __result = skin.AllowJudgementKeySounds;
    }
}
