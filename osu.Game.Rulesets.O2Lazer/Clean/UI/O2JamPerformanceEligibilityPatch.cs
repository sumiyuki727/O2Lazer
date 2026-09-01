using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Transforms;
using osu.Framework.Logging;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded.Statistics;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.UI;

internal static class O2JamPerformanceEligibilityPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.PerformanceEligibility";
    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<FooterButtonMods, O2JamNoModBadgeAnimation> badgeAnimations = new();
    private static readonly ConditionalWeakTable<RankingInformationDisplay, FlashSuppression> suppressedFlashes = new();
    private static FieldInfo? unrankedBadgeField;
    private static FieldInfo? modDisplayBarField;
    private static MethodInfo? buttonUpdateDisplay;
    private static MethodInfo? setPerformanceValue;

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
                var scorePostfix = method(nameof(adaptScoreEligibility));
                (MethodInfo? Target, MethodInfo? Postfix)[] patches =
                [
                    (AccessTools.Method(typeof(ModSelectFooterContent), "updateInformation"), method(nameof(adaptModSelection))),
                    (AccessTools.Method(typeof(PerformanceStatistic), "hasUnrankedMods"), scorePostfix),
                    (AccessTools.Method(typeof(BeatmapLeaderboardScore.LeaderboardScoreTooltip.PerformanceStatisticRow), "hasUnrankedMods"), scorePostfix),
                    (AccessTools.Method(typeof(PerformanceStatistic), "load"), method(nameof(initialiseUnrankedPerformance))),
                ];

                setPerformanceValue = AccessTools.Method(typeof(PerformanceStatistic), "setPerformanceValue", [typeof(ScoreInfo), typeof(double?)]);
                buttonUpdateDisplay = AccessTools.Method(typeof(FooterButtonMods), "updateDisplay");
                var buttonUpdate = AccessTools.Method(typeof(FooterButtonMods), "Update");
                unrankedBadgeField = AccessTools.Field(typeof(FooterButtonMods), "unrankedBadge");
                modDisplayBarField = AccessTools.Field(typeof(FooterButtonMods), "modDisplayBar");

                if (patches.Any(patch => patch.Target == null || patch.Postfix == null)
                    || buttonUpdateDisplay == null || buttonUpdate == null || unrankedBadgeField == null || modDisplayBarField == null || setPerformanceValue == null
                    || AccessTools.Field(typeof(ModSelectFooterContent), "rankingInformationDisplay") == null)
                    throw new MissingMemberException("The native mod ranking display API is incompatible with O2Lazer.");

                foreach (var patch in patches)
                    harmony.Patch(patch.Target!, postfix: new HarmonyMethod(patch.Postfix));
                harmony.Patch(patches[0].Target!,
                    prefix: new HarmonyMethod(method(nameof(prepareModSelection))),
                    transpiler: new HarmonyMethod(method(nameof(adaptModSelectionEligibility))),
                    finalizer: new HarmonyMethod(method(nameof(finishModSelection))));
                harmony.Patch(AccessTools.Method(typeof(RankingInformationDisplay), "flash"),
                    prefix: new HarmonyMethod(method(nameof(allowRankingFlash))));
                harmony.Patch(buttonUpdateDisplay,
                    prefix: new HarmonyMethod(method(nameof(prepareSongSelectButton))),
                    transpiler: new HarmonyMethod(method(nameof(adaptNativeBadgeCalls))));
                harmony.Patch(buttonUpdate, prefix: new HarmonyMethod(method(nameof(flushSongSelectButton))));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its performance eligibility display adapters.");
                return false;
            }
        }
    }

    private static MethodInfo method(string name) => AccessTools.Method(typeof(O2JamPerformanceEligibilityPatch), name);

    private static void prepareModSelection(ModSelectFooterContent __instance, RankingInformationDisplay? ___rankingInformationDisplay,
                                            out RankingInformationDisplay? __state)
    {
        __state = null;
        if (___rankingInformationDisplay?.Ranked.Value != false || !isO2Lazer(__instance.Ruleset.Value)
            || O2JamPerformanceEligibility.IsEligible(__instance.ActiveMods.Value))
            return;

        // Native multiplier changes flash the entire panel too. Keep the counter's rolling,
        // movement and colour, but do not re-highlight an unchanged unranked status.
        __state = ___rankingInformationDisplay;
        suppressedFlashes.GetValue(__state, _ => new FlashSuppression()).Depth++;
    }

    private static void finishModSelection(RankingInformationDisplay? __state)
    {
        if (__state != null && suppressedFlashes.TryGetValue(__state, out var suppression))
            suppression.Depth--;
    }

    private static bool allowRankingFlash(RankingInformationDisplay __instance) =>
        !suppressedFlashes.TryGetValue(__instance, out var suppression) || suppression.Depth == 0;

    private sealed class FlashSuppression
    {
        public int Depth;
    }

    private static void adaptModSelection(ModSelectFooterContent __instance, RankingInformationDisplay? ___rankingInformationDisplay)
    {
        if (__instance.Beatmap.Value == null && isO2Lazer(__instance.Ruleset.Value) && ___rankingInformationDisplay != null)
            ___rankingInformationDisplay.Ranked.Value = O2JamPerformanceEligibility.IsEligible(__instance.ActiveMods.Value);
    }

    private static bool modSelectionRanked(bool nativeRanked, ModSelectFooterContent footer) =>
        isO2Lazer(footer.Ruleset.Value) ? O2JamPerformanceEligibility.IsEligible(footer.ActiveMods.Value) : nativeRanked;

    private static IEnumerable<CodeInstruction> adaptModSelectionEligibility(IEnumerable<CodeInstruction> instructions)
    {
        var result = new List<CodeInstruction>();
        var calls = 0;
        foreach (var instruction in instructions)
        {
            result.Add(instruction);
            if (instruction.opcode != OpCodes.Call || instruction.operand is not MethodInfo called
                || called.DeclaringType != typeof(Enumerable) || called.Name != nameof(Enumerable.All)
                || !called.IsGenericMethod || !called.GetGenericArguments().SequenceEqual([typeof(Mod)])
                || called.GetParameters().Length != 2)
                continue;

            // Ranked drives the native flash animation. Resolve eligibility before its one
            // bindable write, so an unchanged unranked selection never briefly becomes ranked.
            result.Add(new CodeInstruction(OpCodes.Ldarg_0));
            result.Add(new CodeInstruction(OpCodes.Call, method(nameof(modSelectionRanked))));
            calls++;
        }

        if (calls != 1)
            throw new MissingMethodException($"Incompatible native mod footer: found {calls} eligibility calls.");
        return result;
    }

    private static void adaptScoreEligibility(ScoreInfo scoreInfo, ref bool __result)
    {
        if (isO2Lazer(scoreInfo.Ruleset))
            __result = !O2JamPerformanceEligibility.IsEligible(scoreInfo.Mods);
    }

    private static void initialiseUnrankedPerformance(PerformanceStatistic __instance, ScoreInfo ___score)
    {
        if (!isO2Lazer(___score.Ruleset) || ___score.PP.HasValue || ___score.BeatmapInfo == null
            || O2JamPerformanceEligibility.IsEligible(___score.Mods))
            return;

        // Without a PP calculator, the native load returns without styling its default zero.
        // Reuse the native dimming/tooltip policy without assigning PP to the stored score;
        // any future asynchronous calculation can still replace this display value normally.
        setPerformanceValue!.Invoke(__instance, [___score, (double?)0]);
    }

    private static bool isO2Lazer(RulesetInfo? ruleset) =>
        string.Equals(ruleset?.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal);

    private static bool adaptNativeEligibility(bool nativeUnranked, FooterButtonMods button) =>
        isO2Lazer(button.Ruleset.Value) ? !O2JamPerformanceEligibility.IsEligible(button.Mods.Value) : nativeUnranked;

    private static O2JamNoModBadgeAnimation.Destination destination(FooterButtonMods button)
    {
        if (button.Mods.Value.Count == 0)
            return isO2Lazer(button.Ruleset.Value)
                ? O2JamNoModBadgeAnimation.Destination.LeftUpper
                : O2JamNoModBadgeAnimation.Destination.NativeNoMods;

        return adaptNativeEligibility(button.Mods.Value.Any(mod => !mod.Ranked), button)
            ? O2JamNoModBadgeAnimation.Destination.NativeUnranked
            : O2JamNoModBadgeAnimation.Destination.NativeRanked;
    }

    private static void prepareSongSelectButton(FooterButtonMods __instance)
    {
        if (unrankedBadgeField!.GetValue(__instance) is not Drawable badge)
            return;

        if (badgeAnimations.TryGetValue(__instance, out var animation))
            animation.Request(destination(__instance));
        else
            badgeAnimations.Add(__instance, new O2JamNoModBadgeAnimation(badge, destination(__instance)));
    }

    private static void flushSongSelectButton(FooterButtonMods __instance) => FlushBadgeUpdate(__instance);

    internal static void FlushBadgeUpdate(FooterButtonMods button)
    {
        // Ruleset conversion and mod selection can notify several times before the next frame.
        // Only the final request may start a custom route or perform a hidden relocation.
        if (!badgeAnimations.TryGetValue(button, out var animation))
            return;
        if (animation.Flush())
            buttonUpdateDisplay!.Invoke(button, null);
        if (!animation.UpdateButtonWidth || modDisplayBarField!.GetValue(button) is not Drawable bar
            || unrankedBadgeField!.GetValue(button) is not Drawable badge)
            return;

        var width = bar.Width + (animation.AppliedDestination == O2JamNoModBadgeAnimation.Destination.NativeUnranked ? 5 + badge.DrawWidth : 0);
        // LeftUpper has no disappearing badge to wait for when narrowing the button. Its
        // width must share the horizontal slide's start time, in both directions.
        if (animation.AppliedDestination == O2JamNoModBadgeAnimation.Destination.NativeNoMods)
            button.Delay(O2JamNoModBadgeAnimation.Duration).ResizeWidthTo(width, O2JamNoModBadgeAnimation.Duration, O2JamNoModBadgeAnimation.Easing);
        else
            button.ResizeWidthTo(width, O2JamNoModBadgeAnimation.Duration, O2JamNoModBadgeAnimation.Easing);
    }

    private static bool useNativeBadge(FooterButtonMods button) =>
        !badgeAnimations.TryGetValue(button, out var animation) || !animation.OwnsBadge;

    private static TransformSequence<Drawable> moveBadgeX(Drawable badge, float value, double duration, Easing easing, FooterButtonMods button) =>
        useNativeBadge(button) ? badge.MoveToX(value, duration, easing) : new TransformSequence<Drawable>(badge);

    private static TransformSequence<Drawable> moveBadgeY(Drawable badge, float value, double duration, Easing easing, FooterButtonMods button) =>
        useNativeBadge(button) ? badge.MoveToY(value, duration, easing) : new TransformSequence<Drawable>(badge);

    private static TransformSequence<Drawable> showBadge(Drawable badge, double duration, Easing easing, FooterButtonMods button) =>
        useNativeBadge(button) ? badge.FadeIn(duration, easing) : new TransformSequence<Drawable>(badge);

    private static TransformSequence<Drawable> hideBadge(Drawable badge, double duration, Easing easing, FooterButtonMods button) =>
        useNativeBadge(button) ? badge.FadeOut(duration, easing) : new TransformSequence<Drawable>(badge);

    private static TransformSequence<FooterButtonMods> resizeButton(FooterButtonMods drawable, float value, double duration, Easing easing, FooterButtonMods button) =>
        useNativeBadge(button) ? drawable.ResizeWidthTo(value, duration, easing) : new TransformSequence<FooterButtonMods>(drawable);

    private static TransformSequence<FooterButtonMods> resizeButtonLater(TransformSequence<FooterButtonMods> sequence, float value, double duration, Easing easing, FooterButtonMods button) =>
        useNativeBadge(button) ? sequence.ResizeWidthTo(value, duration, easing) : sequence;

    private static IEnumerable<CodeInstruction> adaptNativeBadgeCalls(IEnumerable<CodeInstruction> instructions)
    {
        var result = new List<CodeInstruction>();
        var eligibilityCalls = 0;
        var badgeCalls = 0;
        var widthCalls = 0;
        foreach (var instruction in instructions)
        {
            if (instruction.opcode == OpCodes.Call && instruction.operand is MethodInfo called && called.IsGenericMethod)
            {
                if (called.DeclaringType == typeof(Enumerable) && called.Name == nameof(Enumerable.Any)
                    && called.GetGenericArguments().SequenceEqual([typeof(Mod)]) && called.GetParameters().Length == 2)
                {
                    result.Add(instruction);
                    result.Add(new CodeInstruction(OpCodes.Ldarg_0));
                    result.Add(new CodeInstruction(OpCodes.Call, method(nameof(adaptNativeEligibility))));
                    eligibilityCalls++;
                    continue;
                }

                MethodInfo? replacement = null;
                // Only the badge is statically typed as Drawable in this method. Preserve the
                // original instructions for the mod bar, overflow count and multiplier.
                if (called.DeclaringType == typeof(TransformableExtensions)
                    && called.GetGenericArguments().SequenceEqual([typeof(Drawable)]))
                {
                    replacement = called.Name switch
                    {
                        nameof(TransformableExtensions.MoveToX) => method(nameof(moveBadgeX)),
                        nameof(TransformableExtensions.MoveToY) => method(nameof(moveBadgeY)),
                        nameof(TransformableExtensions.FadeIn) => method(nameof(showBadge)),
                        nameof(TransformableExtensions.FadeOut) => method(nameof(hideBadge)),
                        _ => throw new MissingMethodException($"Unexpected native badge transform: {called.Name}."),
                    };
                    badgeCalls++;
                }
                else if (called.GetGenericArguments().SequenceEqual([typeof(FooterButtonMods)])
                         && called.Name == nameof(TransformableExtensions.ResizeWidthTo))
                {
                    if (called.DeclaringType == typeof(TransformableExtensions))
                        replacement = method(nameof(resizeButton));
                    else if (called.DeclaringType == typeof(TransformSequenceExtensions))
                        replacement = method(nameof(resizeButtonLater));
                    else
                        throw new MissingMethodException($"Unexpected native button resize: {called}.");
                    widthCalls++;
                }

                if (replacement != null)
                {
                    if (!called.GetParameters().Select(parameter => parameter.ParameterType)
                               .SequenceEqual(replacement.GetParameters().SkipLast(1).Select(parameter => parameter.ParameterType))
                        || called.ReturnType != replacement.ReturnType)
                        throw new MissingMethodException($"Incompatible native badge transform: {called.Name}.");

                    var loadButton = new CodeInstruction(OpCodes.Ldarg_0);
                    loadButton.labels.AddRange(instruction.labels);
                    loadButton.blocks.AddRange(instruction.blocks);
                    result.Add(loadButton);
                    result.Add(new CodeInstruction(OpCodes.Call, replacement));
                    continue;
                }
            }
            result.Add(instruction);
        }

        if (eligibilityCalls != 1 || badgeCalls != 7 || widthCalls != 3)
            throw new MissingMethodException($"Incompatible native mod display: found {eligibilityCalls} eligibility, {badgeCalls} badge and {widthCalls} width calls.");
        return result;
    }
}
