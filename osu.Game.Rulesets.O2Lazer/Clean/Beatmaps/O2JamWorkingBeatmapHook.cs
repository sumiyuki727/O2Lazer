using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.IO;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

/// <summary>
/// The single process-wide integration point required because osu! does not expose a custom WorkingBeatmap factory per ruleset.
/// </summary>
public static class O2JamWorkingBeatmapHook
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ExternalWorkingBeatmap";

    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<WorkingBeatmapCache, O2JamWorkingBeatmapCache> wrapperCaches = new();

    public static bool IsInstalled { get; private set; }

    public static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                // BMSRuleset owns a postfix on the inner WorkingBeatmapCache method. Hooking the
                // manager boundary keeps both portable rulesets' embedded Harmony runtimes from
                // replacing each other's detour while still covering every normal osu! call site.
                var target = AccessTools.Method(typeof(BeatmapManager), nameof(BeatmapManager.GetWorkingBeatmap), [typeof(BeatmapInfo), typeof(bool)]);
                var postfix = AccessTools.Method(typeof(O2JamWorkingBeatmapHook), nameof(wrapExternalChart));
                if (target == null || postfix == null)
                    return false;

                new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfix));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not install its external OJN WorkingBeatmap adapter.");
                return false;
            }
        }
    }

    // Harmony's triple-underscore field injection is intentionally named after BeatmapManager's field.
    private static void wrapExternalChart(WorkingBeatmapCache ___workingBeatmapCache, ref WorkingBeatmap __result)
    {
        if (__result is O2JamWorkingBeatmap || !O2JamExternalChart.TryResolve(__result.BeatmapInfo, out var chartPath))
            return;

        var resources = (IStorageResourceProvider)___workingBeatmapCache;
        if (resources.AudioManager == null)
            return;

        var cache = wrapperCaches.GetValue(
            ___workingBeatmapCache,
            inner => new O2JamWorkingBeatmapCache(inner, resources.AudioManager));
        __result = cache.Wrap(__result, chartPath);
    }
}
