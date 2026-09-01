using System;
using System.Collections.Generic;
using System.Linq;
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
    private const string bms_assembly_name = "osu.Game.Rulesets.BmsRuleset";

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
                UsesBmsHarmonyForStatistics = TryPatchWithBmsHarmony(statisticsTarget, statisticsPrefix, statistics_harmony_id);
                if (!UsesBmsHarmonyForStatistics)
                    new Harmony(statistics_harmony_id).Patch(statisticsTarget, prefix: new HarmonyMethod(statisticsPrefix));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
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

    internal static bool TryPatchWithBmsHarmony(MethodInfo target, MethodInfo prefix, string harmonyId, int? priority = null)
        => TryPatchWithBmsHarmony(target, prefix, null, harmonyId, priority);

    internal static bool TryPatchWithBmsHarmony(
        MethodInfo target,
        MethodInfo? prefix,
        MethodInfo? postfix,
        string harmonyId,
        int? priority = null)
    {
        try
        {
            var assembly = AppDomain.CurrentDomain.GetAssemblies()
                                    .FirstOrDefault(candidate => candidate.GetName().Name == bms_assembly_name);
            var harmonyType = assembly?.GetType("HarmonyLib.Harmony");
            var harmonyMethodType = assembly?.GetType("HarmonyLib.HarmonyMethod");
            if (harmonyType == null || harmonyMethodType == null)
                return false;

            var harmony = Activator.CreateInstance(harmonyType, harmonyId);
            var harmonyPrefix = prefix == null ? null : Activator.CreateInstance(harmonyMethodType, prefix);
            var harmonyPostfix = postfix == null ? null : Activator.CreateInstance(harmonyMethodType, postfix);
            if (priority != null)
            {
                var priorityField = harmonyMethodType.GetField("priority", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
                if (harmonyPrefix != null)
                    priorityField?.SetValue(harmonyPrefix, priority.Value);
                if (harmonyPostfix != null)
                    priorityField?.SetValue(harmonyPostfix, priority.Value);
            }

            var patch = harmonyType.GetMethods(BindingFlags.Instance | BindingFlags.Public)
                                   .SingleOrDefault(method => method.Name == "Patch"
                                                              && method.GetParameters() is { Length: 5 } parameters
                                                              && parameters[0].ParameterType == typeof(MethodBase));
            if (harmony == null || prefix != null && harmonyPrefix == null || postfix != null && harmonyPostfix == null || patch == null)
                return false;

            patch.Invoke(harmony, [target, harmonyPrefix, harmonyPostfix, null, null]);
            return true;
        }
        catch (Exception exception)
        {
            Logger.Log($"Could not share BMSRuleset's Harmony runtime; using O2Lazer's runtime instead: {exception.Message}",
                level: LogLevel.Verbose);
            return false;
        }
    }
}
