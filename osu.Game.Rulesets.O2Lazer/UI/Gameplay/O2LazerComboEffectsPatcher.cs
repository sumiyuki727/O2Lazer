using System;
using System.Linq;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Rulesets.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

/// <summary>
/// Prevents osu!'s generic combo-break sound from observing O2Jam's successful-hit combo
/// display correction from one to zero.
/// </summary>
internal static class O2LazerComboEffectsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ComboEffects";

    private static readonly object install_lock = new();

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(ComboEffects), "onComboChange");
            var prefixMethod = AccessTools.Method(typeof(O2LazerComboEffectsPatcher), nameof(prefix));
            var missingMembers = new[]
            {
                (name: "ComboEffects.onComboChange", member: target),
                (name: "O2LazerComboEffectsPatcher.prefix", member: prefixMethod),
            }.Where(member => member.member == null).Select(member => member.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log(
                    "O2Jam ComboEffectsPatcher: Cannot install Harmony patch. Missing: "
                    + $"{string.Join(", ", missingMembers)}. The first successful hit may play osu!'s combo-break sample.",
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
                    "O2Jam ComboEffectsPatcher: Failed to install Harmony patch. The first successful hit may play osu!'s combo-break sample.");
            }
        }
    }

    internal static bool ShouldProcessComboChange(ScoreProcessor processor)
        => processor is not O2LazerScoreProcessor { IsCorrectingO2JamCombo: true };

    // ReSharper disable once InconsistentNaming
    private static bool prefix(ScoreProcessor ___processor) => ShouldProcessComboChange(___processor);
}
