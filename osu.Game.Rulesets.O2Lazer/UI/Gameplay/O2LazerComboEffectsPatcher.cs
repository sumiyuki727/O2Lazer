using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Audio;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

/// <summary>
/// Adapts osu!'s generic combo-break sound to O2Jam's -1 combo sentinel.
/// A reset to 0 is only ever the first COOL/GOOD after a break, while -1 is the actual break value.
/// </summary>
internal static class O2LazerComboEffectsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ComboEffects";

    private static readonly object install_lock = new();

    private static readonly FieldInfo? first_break_time_field = AccessTools.Field(typeof(ComboEffects), "firstBreakTime");
    private static readonly FieldInfo? always_play_first_field = AccessTools.Field(typeof(ComboEffects), "alwaysPlayFirst");
    private static readonly FieldInfo? combo_break_sample_field = AccessTools.Field(typeof(ComboEffects), "comboBreakSample");
    private static readonly PropertyInfo? gameplay_clock_property = AccessTools.Property(typeof(ComboEffects), "gameplayClock");
    private static readonly PropertyInfo? sample_playback_disabler_property = AccessTools.Property(typeof(ComboEffects), "samplePlaybackDisabler");

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(ComboEffects), "onComboChange");
            var prefixMethod = AccessTools.Method(typeof(O2LazerComboEffectsPatcher), nameof(prefix));
            var missingMembers = new (string name, MemberInfo? member)[]
            {
                (name: "ComboEffects.onComboChange", member: target),
                (name: "O2LazerComboEffectsPatcher.prefix", member: prefixMethod),
                (name: "ComboEffects.firstBreakTime", member: first_break_time_field),
                (name: "ComboEffects.alwaysPlayFirst", member: always_play_first_field),
                (name: "ComboEffects.comboBreakSample", member: combo_break_sample_field),
                (name: "ComboEffects.gameplayClock", member: gameplay_clock_property),
                (name: "ComboEffects.samplePlaybackDisabler", member: sample_playback_disabler_property),
            }.Where(member => member.member == null).Select(member => member.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log(
                    "O2Jam ComboEffectsPatcher: Cannot install Harmony patch. Missing: "
                    + $"{string.Join(", ", missingMembers)}. Combo-break sound may behave incorrectly with the -1 sentinel.",
                    level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                O2LazerLogger.Error(exception,
                    "O2Jam ComboEffectsPatcher: Failed to install Harmony patch. Combo-break sound may behave incorrectly with the -1 sentinel.");
            }
        }
    }

    // ReSharper disable once InconsistentNaming
    private static bool prefix(object __instance, ValueChangedEvent<int> combo, ScoreProcessor ___processor)
    {
        if (___processor is not O2LazerScoreProcessor { IsO2Jam: true } o2lazerProcessor)
            return true;

        // The reset from 0 to -1 happens before gameplay starts; it must not sound like a break.
        if (o2lazerProcessor.IsResettingComboSentinel)
            return false;

        // O2Jam never breaks at 0. The framework may pass through 0 while O2JamScoreState later
        // applies the real -1 break, and a successful first hit also lands on 0.
        if (combo.NewValue == 0)
            return false;

        if (combo.NewValue != -1)
            return true;

        var firstBreakTime = first_break_time_field!.GetValue(__instance) as double?;
        var gameplayClock = gameplay_clock_property!.GetValue(__instance) as IGameplayClock;

        if (gameplayClock != null && firstBreakTime != null && gameplayClock.CurrentTime < firstBreakTime)
        {
            first_break_time_field!.SetValue(__instance, null);
            firstBreakTime = null;
        }

        if (gameplayClock?.IsRewinding == true)
            return false;

        var alwaysPlayFirst = always_play_first_field!.GetValue(__instance) as Bindable<bool>;
        bool shouldPlayBreak = combo.OldValue > 20
                               || (alwaysPlayFirst?.Value == true && firstBreakTime == null);

        if (shouldPlayBreak)
        {
            first_break_time_field!.SetValue(__instance, gameplayClock?.CurrentTime);

            var samplePlaybackDisabler = sample_playback_disabler_property!.GetValue(__instance) as ISamplePlaybackDisabler;
            if (samplePlaybackDisabler?.SamplePlaybackDisabled.Value != true)
                (combo_break_sample_field!.GetValue(__instance) as SkinnableSound)?.Play();
        }

        return false;
    }
}
