using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

public static class O2LazerDifficultyStatisticsPatcher
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.DifficultyStatistics";

    private static readonly object install_lock = new();

    public static bool IsInstalled { get; private set; }

    private static PropertyInfo? beatmap_property;
    private static PropertyInfo? ruleset_property;

    public static void InstallOnce()
    {
        lock (install_lock)
        {
            if (IsInstalled)
                return;

            var target = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "updateCountStatistics");
            var prefixMethod = AccessTools.Method(typeof(O2LazerDifficultyStatisticsPatcher), nameof(prefix));
            var resolvedBeatmapProperty = typeof(BeatmapTitleWedge.DifficultyDisplay).GetProperty(
                "beatmap", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            var resolvedRulesetProperty = typeof(BeatmapTitleWedge.DifficultyDisplay).GetProperty(
                "ruleset", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);

            var missingMembers = new (string name, MemberInfo? member)[]
            {
                ("BeatmapTitleWedge.DifficultyDisplay.updateCountStatistics", target),
                ("O2LazerDifficultyStatisticsPatcher.prefix", prefixMethod),
                ("BeatmapTitleWedge.DifficultyDisplay.beatmap", resolvedBeatmapProperty),
                ("BeatmapTitleWedge.DifficultyDisplay.ruleset", resolvedRulesetProperty),
            }.Where(m => m.member == null).Select(m => m.name).ToArray();

            if (missingMembers.Length > 0)
            {
                O2LazerLogger.Log("O2Jam DifficultyStatisticsPatcher: Cannot install Harmony patch. Missing: " + string.Join(", ", missingMembers), level: LogLevel.Error);
                return;
            }

            try
            {
                beatmap_property = resolvedBeatmapProperty;
                ruleset_property = resolvedRulesetProperty;
                new Harmony(harmony_id).Patch(target, prefix: new HarmonyMethod(prefixMethod));
                IsInstalled = true;
            }
            catch (Exception ex)
            {
                O2LazerLogger.Error(ex, "O2Jam DifficultyStatisticsPatcher: Failed to install Harmony patch. Song-select statistics may log unobserved conversion errors.");
            }
        }
    }

    // ReSharper disable InconsistentNaming
    private static bool prefix(BeatmapTitleWedge.DifficultyDisplay __instance)
    {
        var beatmap = (IBindable<WorkingBeatmap>)beatmap_property!.GetValue(__instance)!;
        var ruleset = (IBindable<RulesetInfo>)ruleset_property!.GetValue(__instance)!;

        if (ruleset.Value == null || beatmap.IsDefault)
            return true;

        if (ruleset.Value.ShortName == Constant.SHORT_NAME)
        {
            // osu! computes count statistics inside an unobserved Task, so an un-convertible
            // selection surfaces as a reported unobserved error instead of a graceful no-op.
            // Only O2Jam-imported charts are wrapped as O2LazerWorkingBeatmap; skip the rest cheaply.
            return beatmap.Value is O2LazerWorkingBeatmap;
        }

        // Other rulesets share the same unobserved-task failure mode. Guard them too, but only
        // when the beatmap is already decoded so the converter check stays cheap on the update thread.
        if (!beatmap.Value.BeatmapLoaded)
            return true;

        try
        {
            var rulesetInstance = ruleset.Value.CreateInstance();
            return rulesetInstance.CreateBeatmapConverter(beatmap.Value.Beatmap).CanConvert();
        }
        catch
        {
            // If convertibility cannot be determined, let osu!'s original path decide.
            return true;
        }
    }
    // ReSharper restore InconsistentNaming
}
