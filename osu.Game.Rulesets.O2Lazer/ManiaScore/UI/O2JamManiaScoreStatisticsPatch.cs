using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play.HUD.JudgementCounter;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamManiaScoreStatisticsPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ManiaScoreStatistics";
    private static readonly object installLock = new();
    private static readonly ManiaRuleset maniaPresentation = new();
    private static PropertyInfo scoreProcessorProperty = null!;

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
                var scoreTarget = AccessTools.Method(typeof(ScoreInfo), nameof(ScoreInfo.GetStatisticsForDisplay));
                var scorePostfix = AccessTools.Method(typeof(O2JamManiaScoreStatisticsPatch), nameof(adaptScoreStatistics));
                var gameplayTarget = AccessTools.Method(typeof(JudgementCountController), "load");
                var gameplayTranspiler = AccessTools.Method(typeof(O2JamManiaScoreStatisticsPatch), nameof(adaptGameplayStatistics));
                scoreProcessorProperty = AccessTools.Property(typeof(JudgementCountController), "scoreProcessor");
                if (scoreTarget == null || scorePostfix == null || gameplayTarget == null || gameplayTranspiler == null
                    || scoreProcessorProperty == null)
                    throw new MissingMemberException("The native judgement statistics API has changed.");

                harmony.Patch(scoreTarget, postfix: new HarmonyMethod(scorePostfix));
                harmony.Patch(gameplayTarget, transpiler: new HarmonyMethod(gameplayTranspiler));
                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not install Mania Score judgement statistics.");
                return false;
            }
        }
    }

    private static void adaptScoreStatistics(ScoreInfo __instance, ref IEnumerable<HitResultDisplayStatistic> __result)
    {
        if (!isO2Lazer(__instance.Ruleset))
            return;

        if (O2JamGameplayProfile.UsesManiaScore(__instance.Mods))
        {
            __result = maniaPresentation.GetHitResultsForDisplay()
                                        .Select(result => new HitResultDisplayStatistic(result.result,
                                            __instance.Statistics.GetValueOrDefault(result.result), null, result.displayName));
            return;
        }

        __result = __result.Where(statistic => isO2JamResult(statistic.Result));
    }

    private static IEnumerable<CodeInstruction> adaptGameplayStatistics(IEnumerable<CodeInstruction> instructions)
    {
        var nativeLookup = AccessTools.Method(typeof(Ruleset), nameof(Ruleset.GetHitResultsForDisplay));
        var replacement = AccessTools.Method(typeof(O2JamManiaScoreStatisticsPatch), nameof(getGameplayStatistics));
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeLookup))
            {
                var loadController = new CodeInstruction(OpCodes.Ldarg_0);
                loadController.labels.AddRange(instruction.labels);
                loadController.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return loadController;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                calls++;
            }

            yield return instruction;
        }

        if (calls != 1)
            throw new MissingMemberException("The native gameplay judgement lookup has changed.");
    }

    private static IEnumerable<(HitResult result, LocalisableString displayName)> getGameplayStatistics(
        Ruleset ruleset, JudgementCountController controller)
    {
        var nativeResults = ruleset.GetHitResultsForDisplay();
        if (ruleset.ShortName != O2LazerIdentity.ShortName
            || scoreProcessorProperty.GetValue(controller) is not O2JamScoreProcessor processor)
            return nativeResults;

        return processor.UsesManiaScoring
            ? maniaPresentation.GetHitResultsForDisplay()
            : nativeResults.Where(result => isO2JamResult(result.result));
    }

    private static bool isO2Lazer(RulesetInfo? ruleset) =>
        string.Equals(ruleset?.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal);

    private static bool isO2JamResult(HitResult result) =>
        result is HitResult.Perfect or HitResult.Good or HitResult.Meh or HitResult.Miss;
}
