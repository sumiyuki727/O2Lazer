using System;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Screens.Select;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.UI.Icons;

/// <summary>
/// Supplies the ruleset icon where osu!'s difficulty display only resolves non-negative online IDs.
/// </summary>
internal static class O2JamDifficultyIconPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.DifficultyIcon";
    private const string song_select_harmony_id = "osu.Game.Rulesets.O2Lazer.SongSelectRulesetIcon";
    private const string bms_assembly_name = "osu.Game.Rulesets.BmsRuleset";
    private static readonly object installLock = new();
    private static MethodInfo? targetMethod;
    private static MethodInfo? prefixMethod;
    private static MethodInfo? songSelectTargetMethod;
    private static MethodInfo? songSelectPostfixMethod;
    private static bool subscribedToAssemblyLoad;

    internal static bool IsInstalled { get; private set; }
    internal static bool UsesBmsHarmony { get; private set; }

    internal static bool InstallOnce()
    {
        lock (installLock)
        {
            if (IsInstalled)
                return true;

            try
            {
                targetMethod = AccessTools.Method(typeof(DifficultyIcon), "getRulesetIcon");
                prefixMethod = AccessTools.Method(typeof(O2JamDifficultyIconPatch), nameof(provideO2JamIcon));
                songSelectTargetMethod = AccessTools.Method(typeof(PanelBeatmapSet.SpreadDisplay), "updateBeatmapSet");
                songSelectPostfixMethod = AccessTools.Method(typeof(O2JamDifficultyIconPatch), nameof(correctSongSelectRulesetIcon));
                if (targetMethod == null || prefixMethod == null || songSelectTargetMethod == null || songSelectPostfixMethod == null)
                    return false;

                UsesBmsHarmony = O2JamBeatmapBoundaryPatches.TryPatchWithBmsHarmony(
                    targetMethod,
                    prefixMethod,
                    harmony_id,
                    Priority.First);
                if (!UsesBmsHarmony)
                {
                    new Harmony(harmony_id).Patch(targetMethod, prefix: new HarmonyMethod(prefixMethod)
                    {
                        priority = Priority.First,
                    });
                }


                new Harmony(song_select_harmony_id).Patch(
                    songSelectTargetMethod,
                    postfix: new HarmonyMethod(songSelectPostfixMethod));

                if (!subscribedToAssemblyLoad)
                {
                    AppDomain.CurrentDomain.AssemblyLoad += onAssemblyLoad;
                    subscribedToAssemblyLoad = true;
                }

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not install its results-screen icon adapter.");
                return false;
            }
        }
    }

    private static void onAssemblyLoad(object? sender, AssemblyLoadEventArgs args)
    {
        if (!string.Equals(args.LoadedAssembly.GetName().Name, bms_assembly_name, StringComparison.Ordinal))
            return;

        lock (installLock)
        {
            if (targetMethod == null || prefixMethod == null)
                return;

            UsesBmsHarmony = O2JamBeatmapBoundaryPatches.TryPatchWithBmsHarmony(
                targetMethod,
                prefixMethod,
                harmony_id,
                Priority.First);
        }
    }

    // Harmony field injection uses the patched class's private field name.
    private static bool provideO2JamIcon(IRulesetInfo ___ruleset, IBeatmapInfo? ___beatmap, ref Drawable __result)
    {
        if (!ShouldUseO2JamIcon(___ruleset, ___beatmap))
            return true;

        __result = new O2JamRulesetIcon();
        return false;
    }

    internal static bool ShouldUseO2JamIcon(IRulesetInfo ruleset, IBeatmapInfo? beatmap) =>
        string.Equals(ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal)
        || string.Equals(beatmap?.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal);

    // osu! groups the spread display by OnlineID. Every community ruleset uses -1, so resolving
    // that ID can return BMS even though every beatmap in this set belongs to O2Lazer.
    private static void correctSongSelectRulesetIcon(
        PanelBeatmapSet.SpreadDisplay __instance,
        FillFlowContainer ___flow)
    {
        var beatmaps = __instance.BeatmapSet.Value?.Beatmaps;
        if (beatmaps == null || beatmaps.Count == 0
                             || beatmaps.Any(beatmap => !string.Equals(
                                 beatmap.Ruleset.ShortName,
                                 O2LazerIdentity.ShortName,
                                 StringComparison.Ordinal))
                             || ___flow.Count == 0)
            return;

        var oldIcon = ___flow[0];
        var icon = new O2JamRulesetIcon
        {
            Size = new Vector2(14),
            Anchor = Anchor.CentreLeft,
            Origin = Anchor.CentreLeft,
            Margin = oldIcon.Margin,
        };

        ___flow.Remove(oldIcon, true);
        ___flow.Insert(0, icon);
    }
}
