using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public static class O2LazerBeatmapCompatibilityPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.BeatmapCompatibility";

    private static readonly object install_lock = new();

    public static bool IsInstalled { get; private set; }

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(BeatmapInfoExtensions), nameof(BeatmapInfoExtensions.AllowGameplayWithRuleset));
            var prefixMethod = AccessTools.Method(typeof(O2LazerBeatmapCompatibilityPatcher), nameof(prefix));

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                ("BeatmapInfoExtensions.AllowGameplayWithRuleset", target),
                ("O2LazerBeatmapCompatibilityPatcher.prefix", prefixMethod),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log("O2Jam BeatmapCompatibilityPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missingMembers), level: LogLevel.Error);
                return;
            }

            try
            {
                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                O2LazerLogger.Error(ex, "O2Jam BeatmapCompatibilityPatcher: Failed to install Harmony patch. Song select may keep non-O2Jam selections when switching rulesets.");
            }
        }
    }

    // ReSharper disable InconsistentNaming
    private static bool prefix(IBeatmapInfo beatmap, RulesetInfo ruleset, ref bool __result)
    {
        if (ruleset?.ShortName != Constant.SHORT_NAME)
            return true;

        // osu! treats every osu!standard map as convertible to a custom ruleset, but O2Jam only
        // accepts its own imported charts. Keep song-select validity checks consistent so switching
        // to O2Jam re-selects an actual O2Jam beatmap instead of keeping an unplayable selection.
        __result = beatmap.Ruleset.ShortName == Constant.SHORT_NAME;
        return false;
    }
    // ReSharper restore InconsistentNaming
}
