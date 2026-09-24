using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

/// <summary>
/// Adapts native difficulty spreads to level ordering and colours while level mode is selected.
/// </summary>
internal static class O2JamLevelSpreadAdapter
{
    private static readonly ConditionalWeakTable<PanelBeatmapStandalone.SpreadDisplay, SpreadPresentationState> standaloneSpreadStates = new();
    private static readonly ConditionalWeakTable<PanelBeatmapSet.SpreadDisplay, SpreadPresentationState> setSpreadStates = new();
    private static FieldInfo standaloneSpreadDisplayField = null!;
    private static MethodInfo standaloneSpreadUpdateMethod = null!;
    private static MethodInfo setSpreadUpdateMethod = null!;
    private static MethodInfo drawableSchedulerGetter = null!;

    internal static void Install(Harmony harmony)
    {
        var spreadUpdate = AccessTools.Method(typeof(PanelBeatmapStandalone.SpreadDisplay), "updateBeatmap");
        var setSpreadLoad = AccessTools.Method(typeof(PanelBeatmapSet.SpreadDisplay), "load");
        var setSpreadUpdate = AccessTools.Method(typeof(PanelBeatmapSet.SpreadDisplay), "updateBeatmapSet");
        standaloneSpreadDisplayField = AccessTools.Field(typeof(PanelBeatmapStandalone), "spreadDisplay");
        standaloneSpreadUpdateMethod = spreadUpdate;
        setSpreadUpdateMethod = setSpreadUpdate;
        drawableSchedulerGetter = AccessTools.PropertyGetter(typeof(Drawable), "Scheduler");
        if (spreadUpdate == null || setSpreadLoad == null || setSpreadUpdate == null
            || standaloneSpreadDisplayField == null || drawableSchedulerGetter == null)
            throw new MissingMemberException("The native difficulty spread API has changed.");

        harmony.Patch(spreadUpdate, transpiler: new HarmonyMethod(method(nameof(adaptStandaloneSpread))));
        harmony.Patch(setSpreadLoad, postfix: new HarmonyMethod(method(nameof(configureSetSpread))));
        harmony.Patch(setSpreadUpdate, transpiler: new HarmonyMethod(method(nameof(adaptSetSpread))));
    }

    private static MethodInfo method(string name) => AccessTools.Method(typeof(O2JamLevelSpreadAdapter), name);

    internal static void SetStandaloneMode(PanelBeatmapStandalone panel, bool useLevel, bool refresh)
    {
        var spread = (PanelBeatmapStandalone.SpreadDisplay)standaloneSpreadDisplayField.GetValue(panel)!;
        var state = standaloneSpreadStates.GetOrCreateValue(spread);
        if (state.Initialised && state.UseLevel == useLevel)
            return;

        state.Initialised = true;
        state.UseLevel = useLevel;
        if (refresh)
            standaloneSpreadUpdateMethod.Invoke(spread, null);
    }

    private static IEnumerable<CodeInstruction> adaptStandaloneSpread(IEnumerable<CodeInstruction> instructions) =>
        adaptSpreadLinq(instructions, typeof(PanelBeatmapStandalone.SpreadDisplay),
            nameof(orderStandaloneBeatmaps), nameof(selectStandaloneRatings), null);

    private static IOrderedEnumerable<BeatmapInfo> orderStandaloneBeatmaps(IEnumerable<BeatmapInfo> source,
                                                                           Func<BeatmapInfo, double> nativeSelector,
                                                                           PanelBeatmapStandalone.SpreadDisplay display) =>
        standaloneSpreadStates.TryGetValue(display, out var state) && state.UseLevel
            ? OrderSpreadBeatmaps(source, true)
            : source.OrderBy(nativeSelector);

    private static IEnumerable<double> selectStandaloneRatings(IEnumerable<BeatmapInfo> source,
                                                                Func<BeatmapInfo, double> nativeSelector,
                                                                PanelBeatmapStandalone.SpreadDisplay display) =>
        standaloneSpreadStates.TryGetValue(display, out var state) && state.UseLevel
            ? source.Select(beatmap => GetColourStars(beatmap, true))
            : source.Select(nativeSelector);

    private static void configureSetSpread(PanelBeatmapSet.SpreadDisplay __instance)
    {
        var mods = (IBindable<IReadOnlyList<Mod>>)__instance.Dependencies.Get(typeof(IBindable<IReadOnlyList<Mod>>));
        var ruleset = (IBindable<RulesetInfo>)__instance.Dependencies.Get(typeof(IBindable<RulesetInfo>));
        var state = setSpreadStates.GetOrCreateValue(__instance);
        state.Refresh ??= () => updateSetSpreadMode(__instance, ruleset.Value.ShortName == O2LazerIdentity.ShortName
                                                                && !O2JamGameplayProfile.UsesManiaScore(mods.Value));
        // Initialise the first colour before the panel is shown. Later ruleset and forced-MS
        // changes can arrive in one frame, so rebuild the native spread only for the final profile.
        void update()
        {
            if (!state.Initialised)
                state.Refresh();
            else
                ((Scheduler)drawableSchedulerGetter.Invoke(__instance, null)!).AddOnce(state.Refresh);
        }

        mods.BindValueChanged(_ => update(), true);
        ruleset.BindValueChanged(_ => update(), true);
    }

    private static void updateSetSpreadMode(PanelBeatmapSet.SpreadDisplay display, bool useLevel)
    {
        var state = setSpreadStates.GetOrCreateValue(display);
        if (state.Initialised && state.UseLevel == useLevel)
            return;

        state.Initialised = true;
        state.UseLevel = useLevel;
        if (display.BeatmapSet.Value?.Beatmaps.Any(isO2LazerBeatmap) == true)
            setSpreadUpdateMethod.Invoke(display, null);
    }

    private static IEnumerable<CodeInstruction> adaptSetSpread(IEnumerable<CodeInstruction> instructions) =>
        adaptSpreadLinq(instructions, typeof(PanelBeatmapSet.SpreadDisplay), nameof(orderSetBeatmaps), null, nameof(getSetColourStars));

    private static IOrderedEnumerable<BeatmapInfo> orderSetBeatmaps(IEnumerable<BeatmapInfo> source,
                                                                    Func<BeatmapInfo, double> nativeSelector,
                                                                    PanelBeatmapSet.SpreadDisplay display) =>
        setSpreadStates.TryGetValue(display, out var state) && state.UseLevel && source.Any(isO2LazerBeatmap)
            ? OrderSpreadBeatmaps(source, true)
            : source.OrderBy(nativeSelector);

    private static double getSetColourStars(BeatmapInfo beatmap, PanelBeatmapSet.SpreadDisplay display) =>
        GetColourStars(beatmap, setSpreadStates.TryGetValue(display, out var state) && state.UseLevel);

    internal static IOrderedEnumerable<BeatmapInfo> OrderSpreadBeatmaps(IEnumerable<BeatmapInfo> source, bool useLevel) =>
        useLevel
            ? source.OrderBy(beatmap => GetColourStars(beatmap, true))
                    .ThenBy(beatmap => isO2LazerBeatmap(beatmap) ? O2JamStarRatingMetadata.ResolveChartOrder(beatmap) : 0)
            : source.OrderBy(beatmap => beatmap.StarRating);

    internal static double GetLevelColourStars(IBeatmapInfo beatmap) => O2JamStarRatingMetadata.ResolveLevel(beatmap) / 10d;

    private static bool isO2LazerBeatmap(IBeatmapInfo beatmap) => beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName;

    private static IEnumerable<CodeInstruction> adaptSpreadLinq(IEnumerable<CodeInstruction> instructions, Type displayType,
                                                                 string orderReplacement, string? selectReplacement,
                                                                 string? colourReplacement)
    {
        var result = new List<CodeInstruction>();
        var orderCalls = 0;
        var selectCalls = 0;
        var colourCalls = 0;
        var starGetter = AccessTools.PropertyGetter(typeof(BeatmapInfo), nameof(BeatmapInfo.StarRating));

        foreach (var instruction in instructions)
        {
            if (instruction.operand is MethodInfo called && called.IsGenericMethod
                && called.DeclaringType == typeof(Enumerable)
                && called.GetGenericArguments().FirstOrDefault() == typeof(BeatmapInfo)
                && (called.Name == nameof(Enumerable.OrderBy) || called.Name == nameof(Enumerable.Select)))
            {
                var replacementName = called.Name == nameof(Enumerable.OrderBy) ? orderReplacement : selectReplacement;
                if (replacementName != null)
                {
                    var loadDisplay = new CodeInstruction(OpCodes.Ldarg_0);
                    loadDisplay.labels.AddRange(instruction.labels);
                    loadDisplay.blocks.AddRange(instruction.blocks);
                    instruction.labels.Clear();
                    instruction.blocks.Clear();
                    result.Add(loadDisplay);
                    instruction.opcode = OpCodes.Call;
                    instruction.operand = method(replacementName);
                    if (called.Name == nameof(Enumerable.OrderBy))
                        orderCalls++;
                    else
                        selectCalls++;
                }
            }
            else if (colourReplacement != null && instruction.Calls(starGetter))
            {
                var loadDisplay = new CodeInstruction(OpCodes.Ldarg_0);
                loadDisplay.labels.AddRange(instruction.labels);
                loadDisplay.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                result.Add(loadDisplay);
                instruction.opcode = OpCodes.Call;
                instruction.operand = method(colourReplacement);
                colourCalls++;
            }

            result.Add(instruction);
        }

        if (orderCalls != 1 || selectReplacement != null && selectCalls != 1 || colourReplacement != null && colourCalls != 1)
            throw new MissingMemberException($"The native {displayType.Name} difficulty spread has changed.");

        return result;
    }

    internal static double GetColourStars(IBeatmapInfo beatmap, bool useLevel) =>
        useLevel && isO2LazerBeatmap(beatmap) ? GetLevelColourStars(beatmap) : beatmap.StarRating;

    private sealed class SpreadPresentationState
    {
        public bool Initialised;
        public bool UseLevel;
        public Action? Refresh;
    }
}
