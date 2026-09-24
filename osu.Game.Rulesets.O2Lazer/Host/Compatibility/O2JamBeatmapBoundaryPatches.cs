using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.UI;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

internal static class O2JamBeatmapBoundaryPatches
{
    private const string gameplay_harmony_id = "osu.Game.Rulesets.O2Lazer.BeatmapBoundary";
    private const string statistics_harmony_id = "osu.Game.Rulesets.O2Lazer.DifficultyStatistics";

    private static readonly object installLock = new();
    private static PropertyInfo? beatmapProperty;
    private static PropertyInfo? rulesetProperty;

    internal static bool IsInstalled { get; private set; }

    internal static bool UsesBmsHarmonyForStatistics { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                var gameplayTarget = AccessTools.Method(typeof(WorkingBeatmap), nameof(WorkingBeatmap.GetPlayableBeatmap),
                    [typeof(IRulesetInfo), typeof(IReadOnlyList<Mod>), typeof(CancellationToken)]);
                var gameplayPrefix = AccessTools.Method(typeof(O2JamBeatmapBoundaryPatches), nameof(rejectCrossRulesetGameplay));
                var statisticsTarget = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "updateCountStatistics");
                var statisticsPrefix = AccessTools.Method(typeof(O2JamBeatmapBoundaryPatches), nameof(skipCrossRulesetStatistics));
                beatmapProperty = typeof(BeatmapTitleWedge.DifficultyDisplay).GetProperty(
                    "beatmap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                rulesetProperty = typeof(BeatmapTitleWedge.DifficultyDisplay).GetProperty(
                    "ruleset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

                if (gameplayTarget == null || gameplayPrefix == null || statisticsTarget == null || statisticsPrefix == null
                    || beatmapProperty == null || rulesetProperty == null)
                    return false;

                new Harmony(gameplay_harmony_id).Patch(gameplayTarget, prefix: new HarmonyMethod(gameplayPrefix));

                // BMSRuleset also guards this private lazer method. Register through its already-loaded
                // Harmony runtime when present so two portable Harmony copies do not replace each other's detour.
                UsesBmsHarmonyForStatistics = O2JamBmsHarmonyCompatibility.TryPatch(statisticsTarget, statisticsPrefix, statistics_harmony_id);
                if (!UsesBmsHarmonyForStatistics)
                    new Harmony(statistics_harmony_id).Patch(statisticsTarget, prefix: new HarmonyMethod(statisticsPrefix));

                IsInstalled = true;
                if (!UsesBmsHarmonyForStatistics)
                {
                    O2JamBmsHarmonyCompatibility.RegisterForLateLoad(
                        statisticsTarget, statisticsPrefix, null, statistics_harmony_id,
                        onPatched: () => UsesBmsHarmonyForStatistics = true);
                }
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(gameplay_harmony_id, statistics_harmony_id);
                UsesBmsHarmonyForStatistics = false;
                Logger.Error(exception, "O2Lazer could not install its beatmap conversion boundary.");
                return false;
            }
        }
    }

    private static void rejectCrossRulesetGameplay(WorkingBeatmap __instance, IRulesetInfo ruleset)
    {
        if (O2JamBeatmapBoundary.Crosses(__instance.BeatmapInfo, ruleset))
            throw new BeatmapInvalidForRulesetException(O2LazerStrings.CrossRulesetConversionUnsupported.ToString());
    }

    private static bool skipCrossRulesetStatistics(BeatmapTitleWedge.DifficultyDisplay __instance)
    {
        var beatmap = (IBindable<WorkingBeatmap>)beatmapProperty!.GetValue(__instance)!;
        var ruleset = (IBindable<RulesetInfo>)rulesetProperty!.GetValue(__instance)!;

        return beatmap.IsDefault || ruleset.Value == null
               || !O2JamBeatmapBoundary.Crosses(beatmap.Value.BeatmapInfo, ruleset.Value);
    }
}
