using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public static class O2LazerWorkingBeatmapPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.WorkingBeatmap";

    private static readonly object install_lock = new();
    private static readonly ConditionalWeakTable<WorkingBeatmapCache, O2LazerWorkingBeatmapCache> wrapper_caches = new();

    /// <summary>
    ///     Install the O2LAZER working-beatmap Harmony patch. Safe to call multiple times (idempotent).
    /// </summary>
    public static bool InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return true;

            var target = AccessTools.Method(typeof(WorkingBeatmapCache), nameof(WorkingBeatmapCache.GetWorkingBeatmap), [typeof(BeatmapInfo)]);
            var playableTarget = AccessTools.Method(
                typeof(WorkingBeatmap),
                nameof(WorkingBeatmap.GetPlayableBeatmap),
                [typeof(IRulesetInfo), typeof(IReadOnlyList<Mod>), typeof(CancellationToken)]);
            var postfixMethod = AccessTools.Method(typeof(O2LazerWorkingBeatmapPatcher), nameof(postfix));
            var playablePrefixMethod = AccessTools.Method(typeof(O2LazerWorkingBeatmapPatcher), nameof(playablePrefix));

            var missingMembers = new[]
            {
                (name: "WorkingBeatmapCache.GetWorkingBeatmap", member: target),
                (name: "WorkingBeatmap.GetPlayableBeatmap", member: playableTarget),
                (name: "O2LazerWorkingBeatmapPatcher.playablePrefix", member: playablePrefixMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log(
                    "O2LAZER WorkingBeatmapPatcher: Cannot install Harmony patch. Missing: "
                    + $"{string.Join(", ", missingMembers)}. O2LAZER preview audio will not be available.",
                    level: LogLevel.Error);
                return false;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, postfix: new HarmonyMethod(postfixMethod));
                new Harmony(harmony_id).Patch(playableTarget, prefix: new HarmonyMethod(playablePrefixMethod));
                IsInstalled = true;
                return true;
            }
            catch (Exception ex)
            {
                O2LazerLogger.Error(ex, "O2LAZER WorkingBeatmapPatcher: Failed to install Harmony patch. O2LAZER preview audio will not be available.");
                return false;
            }
        }
    }

    public static bool IsInstalled { get; private set; }

    // ReSharper disable InconsistentNaming
    private static void postfix(WorkingBeatmapCache __instance, BeatmapInfo? beatmapInfo, ref WorkingBeatmap __result)
    {
        _ = beatmapInfo;

        if (__result is O2LazerWorkingBeatmap)
            return;

        if (__result.BeatmapInfo.Ruleset.ShortName != Constant.SHORT_NAME)
            return;

        try
        {
            __result = wrapper_caches.GetValue(__instance, static cache => new O2LazerWorkingBeatmapCache(cache)).Wrap(__result);
        }
        catch (Exception e)
        {
            O2LazerLogger.Error(e, "O2LAZER WorkingBeatmapPatcher: Failed to wrap a O2LAZER working beatmap. Falling back to osu!'s default working beatmap.");
        }
    }
    // ReSharper disable once InconsistentNaming
    private static bool playablePrefix(WorkingBeatmap __instance, IRulesetInfo ruleset)
    {
        if (ruleset?.ShortName == Constant.SHORT_NAME && !O2LazerWorkingBeatmap.IsExternalChartAvailable(__instance.BeatmapInfo))
        {
            throw new BeatmapInvalidForRulesetException("O2Jam source file is missing.");
        }

        return true;
    }
    // ReSharper restore InconsistentNaming
}


