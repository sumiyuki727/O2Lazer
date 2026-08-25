using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Graphics.UserInterface;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.UI.Gameplay;

/// <summary>
/// Keeps framework combo counters from rendering the -1 sentinel used internally by
/// O2Jam. This covers osu!'s DefaultComboCounter and LegacyDefaultComboCounter, which
/// bind directly to ScoreProcessor.Combo without any ruleset-specific display clamp.
/// </summary>
internal static class O2LazerComboCounterDisplayPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.ComboCounterDisplay";

    private static readonly object install_lock = new();

    internal static bool IsInstalled { get; private set; }

    internal static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var rollingCounterTarget = AccessTools.PropertySetter(typeof(RollingCounter<int>), nameof(RollingCounter<int>.DisplayedCount));
            var legacyCounterTarget = AccessTools.PropertySetter(typeof(LegacyDefaultComboCounter), nameof(LegacyDefaultComboCounter.DisplayedCount));
            var prefixMethod = AccessTools.Method(typeof(O2LazerComboCounterDisplayPatcher), nameof(displayedCountPrefix));
            var missingMembers = new (string name, MemberInfo? member)[]
            {
                (name: "RollingCounter<int>.DisplayedCount.set", member: rollingCounterTarget),
                (name: "LegacyDefaultComboCounter.DisplayedCount.set", member: legacyCounterTarget),
                (name: "O2LazerComboCounterDisplayPatcher.displayedCountPrefix", member: prefixMethod),
            }.Where(member => member.member == null).Select(member => member.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log(
                    "O2Jam ComboCounterDisplayPatcher: Cannot install Harmony patch. Missing: "
                    + $"{string.Join(", ", missingMembers)}. Framework combo counters may display -1.",
                    level: LogLevel.Error);
                return;
            }

            try
            {
                var harmony = new Harmony(harmony_id);
                harmony.Patch(rollingCounterTarget, prefix: new HarmonyMethod(prefixMethod));
                harmony.Patch(legacyCounterTarget, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception exception)
            {
                O2LazerLogger.Error(exception,
                    "O2Jam ComboCounterDisplayPatcher: Failed to install Harmony patch. Framework combo counters may display -1.");
            }
        }
    }

    private static bool displayedCountPrefix(ref int value)
    {
        if (value < 0)
            value = 0;

        return true;
    }
}
