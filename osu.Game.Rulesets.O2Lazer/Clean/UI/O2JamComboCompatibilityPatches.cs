using System;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Screens.Play.HUD;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.UI;

/// <summary>
/// Adapts osu!'s zero-based combo presentation to O2Jam's internal -1 break sentinel.
/// </summary>
internal static class O2JamComboCompatibilityPatches
{
    private const string effects_harmony_id = "osu.Game.Rulesets.O2Lazer.ComboEffects";
    private const string counters_harmony_id = "osu.Game.Rulesets.O2Lazer.ComboCounters";
    private const string maximum_combo_harmony_id = "osu.Game.Rulesets.O2Lazer.MaximumCombo";
    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<object, O2JamDisplayedComboAdapter> counterAdapters = new();

    internal static bool IsInstalled { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                var effectsTarget = AccessTools.Method(typeof(ComboEffects), "onComboChange");
                var effectsPrefix = AccessTools.Method(typeof(O2JamComboCompatibilityPatches), nameof(adaptComboBreak));
                var counterPostfix = AccessTools.Method(typeof(O2JamComboCompatibilityPatches), nameof(attachDisplayedCombo));
                var maximumComboTarget = AccessTools.Method(typeof(ScoreInfoExtensions), nameof(ScoreInfoExtensions.GetMaximumAchievableCombo));
                var maximumComboPostfix = AccessTools.Method(typeof(O2JamComboCompatibilityPatches), nameof(adaptMaximumAchievableCombo));
                var counterTargets = new[]
                {
                    AccessTools.Method(typeof(DefaultComboCounter), "load"),
                    AccessTools.Method(typeof(ArgonComboCounter), "load"),
                    AccessTools.Method(typeof(LegacyDefaultComboCounter), "load"),
                    AccessTools.Method(typeof(LegacyManiaComboCounter), "load"),
                };

                if (effectsTarget == null || effectsPrefix == null || counterPostfix == null
                    || maximumComboTarget == null || maximumComboPostfix == null || Array.Exists(counterTargets, target => target == null))
                    return false;

                new Harmony(effects_harmony_id).Patch(effectsTarget, prefix: new HarmonyMethod(effectsPrefix));

                var counterHarmony = new Harmony(counters_harmony_id);
                foreach (var target in counterTargets)
                    counterHarmony.Patch(target!, postfix: new HarmonyMethod(counterPostfix));

                new Harmony(maximum_combo_harmony_id).Patch(maximumComboTarget, postfix: new HarmonyMethod(maximumComboPostfix));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not install its combo presentation adapters.");
                return false;
            }
        }
    }

    private static void adaptMaximumAchievableCombo(ScoreInfo score, ref int __result)
    {
        if (!string.Equals(score.Ruleset?.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal))
            return;

        // Results and leaderboard FC checks derive this from endpoint counts, not difficulty
        // attributes. Keep the stored statistics and earned MaxCombo intact; only the first
        // successful O2Jam endpoint contributes zero to the achievable combo.
        __result = Math.Max(0, __result - 1);
    }

    // Harmony field injection uses the patched class's private field name.
    private static bool adaptComboBreak(ref ValueChangedEvent<int> combo, ScoreProcessor ___processor)
    {
        if (___processor is not O2JamScoreProcessor o2JamProcessor)
            return true;

        if (o2JamProcessor.IsResettingComboSentinel)
            return false;

        // Zero is the first successful O2Jam judgement, not a break.
        if (combo.NewValue == 0)
            return false;

        if (combo.NewValue != -1)
            return true;

        // The native effect remains responsible for user preference, seeking and playback state.
        combo = new ValueChangedEvent<int>(combo.OldValue, 0);
        return true;
    }

    private static void attachDisplayedCombo(object __instance, ScoreProcessor scoreProcessor)
    {
        if (scoreProcessor is not O2JamScoreProcessor)
            return;

        Bindable<int>? current = __instance switch
        {
            ComboCounter counter => counter.Current,
            LegacyDefaultComboCounter counter => counter.Current,
            LegacyManiaComboCounter counter => counter.Current,
            _ => null,
        };

        if (current == null)
            return;

        // Native counters must see 0 both before and after O2Jam's first successful judgement.
        // They then receive the normal 0→1 increment and real N→0 break transitions, preserving
        // mania's own increment, rolling and miss animations without ever rendering -1.
        current.UnbindFrom(scoreProcessor.Combo);
        var adapter = counterAdapters.GetValue(__instance, _ => new O2JamDisplayedComboAdapter(((O2JamScoreProcessor)scoreProcessor).GameplayState));
        current.BindTo(adapter.Current);
    }
}

internal sealed class O2JamDisplayedComboAdapter
{
    internal BindableInt Current { get; } = new() { MinValue = 0 };

    internal O2JamDisplayedComboAdapter(IO2JamGameplayStateSource source)
    {
        Current.Value = Math.Max(0, source.Current.Combo);
        source.StateChanged += state => Current.Value = Math.Max(0, state.Combo);
    }
}
