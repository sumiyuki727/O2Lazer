using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Screens.Play;
using osu.Game.Screens.Select;
using osu.Game.Utils;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamStarRatingPresentationPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.StarRatingPresentation";
    private const float level_text_horizontal_offset = -1.5f;
    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<StarRatingDisplay, PresentationState> states = new();
    private static readonly ConditionalWeakTable<PanelBeatmapStandalone.SpreadDisplay, SpreadPresentationState> standaloneSpreadStates = new();
    private static readonly ConditionalWeakTable<PanelBeatmapSet.SpreadDisplay, SpreadPresentationState> setSpreadStates = new();
    private static readonly OsuColour colours = new();

    private static FieldInfo backgroundField = null!;
    private static FieldInfo starIconField = null!;
    private static FieldInfo starsTextField = null!;
    private static MethodInfo titleBeatmapGetter = null!;
    private static MethodInfo titleModsGetter = null!;
    private static FieldInfo titleStarRatingDisplayField = null!;
    private static FieldInfo titleDifficultyTextField = null!;
    private static FieldInfo titleMappedByTextField = null!;
    private static FieldInfo titleCountStatisticsDisplayField = null!;
    private static FieldInfo titleDifficultyStatisticsDisplayField = null!;
    private static MethodInfo panelBeatmapGetter = null!;
    private static MethodInfo panelModsGetter = null!;
    private static FieldInfo panelStarRatingDisplayField = null!;
    private static MethodInfo standaloneBeatmapGetter = null!;
    private static MethodInfo standaloneModsGetter = null!;
    private static FieldInfo standaloneStarRatingDisplayField = null!;
    private static FieldInfo standaloneSpreadDisplayField = null!;
    private static MethodInfo standaloneSpreadUpdateMethod = null!;
    private static MethodInfo setSpreadUpdateMethod = null!;
    private static FieldInfo gameplayBeatmapField = null!;
    private static FieldInfo gameplayModsField = null!;
    private static FieldInfo gameplayStarRatingDisplayField = null!;
    private static FieldInfo modSelectStarRatingDisplayField = null!;
    private static FieldInfo tooltipStarRatingField = null!;
    private static FieldInfo tooltipBeatmapField = null!;
    private static FieldInfo tooltipModsField = null!;

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
                var starChanged = AccessTools.GetDeclaredMethods(typeof(StarRatingDisplay))
                                             .SingleOrDefault(method => method.Name.StartsWith("<LoadComplete>b__", StringComparison.Ordinal)
                                                                        && method.GetParameters().Length == 1
                                                                        && method.GetParameters()[0].ParameterType == typeof(ValueChangedEvent<double>));
                var titleWedgeLoad = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "LoadComplete");
                var titleWedgeUpdate = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "Update");
                var panelUpdate = AccessTools.Method(typeof(PanelBeatmap), "updateKeyCount");
                var standaloneUpdate = AccessTools.Method(typeof(PanelBeatmapStandalone), "updateKeyCount");
                var spreadUpdate = AccessTools.Method(typeof(PanelBeatmapStandalone.SpreadDisplay), "updateBeatmap");
                var setSpreadLoadComplete = AccessTools.Method(typeof(PanelBeatmapSet.SpreadDisplay), "LoadComplete");
                var setSpreadUpdate = AccessTools.Method(typeof(PanelBeatmapSet.SpreadDisplay), "updateBeatmapSet");
                var gameplayLoad = AccessTools.Method(typeof(BeatmapMetadataDisplay), "load");
                var modSelectLoad = AccessTools.Method(typeof(BeatmapAttributesDisplay), "LoadComplete");
                var tooltipType = typeof(DifficultyIcon).Assembly.GetType("osu.Game.Beatmaps.Drawables.DifficultyIconTooltip");
                var tooltipContentType = typeof(DifficultyIcon).Assembly.GetType("osu.Game.Beatmaps.Drawables.DifficultyIconTooltipContent");
                var tooltipSetContent = AccessTools.Method(tooltipType, "SetContent");

                backgroundField = AccessTools.Field(typeof(StarRatingDisplay), "background");
                starIconField = AccessTools.Field(typeof(StarRatingDisplay), "starIcon");
                starsTextField = AccessTools.Field(typeof(StarRatingDisplay), "starsText");
                titleBeatmapGetter = AccessTools.PropertyGetter(typeof(BeatmapTitleWedge.DifficultyDisplay), "beatmap");
                titleModsGetter = AccessTools.PropertyGetter(typeof(BeatmapTitleWedge.DifficultyDisplay), "mods");
                titleStarRatingDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "starRatingDisplay");
                titleDifficultyTextField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "difficultyText");
                titleMappedByTextField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "mappedByText");
                titleCountStatisticsDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "countStatisticsDisplay");
                titleDifficultyStatisticsDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "difficultyStatisticsDisplay");
                panelBeatmapGetter = AccessTools.PropertyGetter(typeof(PanelBeatmap), "beatmap");
                panelModsGetter = AccessTools.PropertyGetter(typeof(PanelBeatmap), "mods");
                panelStarRatingDisplayField = AccessTools.Field(typeof(PanelBeatmap), "starRatingDisplay");
                standaloneBeatmapGetter = AccessTools.PropertyGetter(typeof(PanelBeatmapStandalone), "beatmap");
                standaloneModsGetter = AccessTools.PropertyGetter(typeof(PanelBeatmapStandalone), "mods");
                standaloneStarRatingDisplayField = AccessTools.Field(typeof(PanelBeatmapStandalone), "starRatingDisplay");
                standaloneSpreadDisplayField = AccessTools.Field(typeof(PanelBeatmapStandalone), "spreadDisplay");
                standaloneSpreadUpdateMethod = spreadUpdate;
                setSpreadUpdateMethod = setSpreadUpdate;
                gameplayBeatmapField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "beatmap");
                gameplayModsField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "mods");
                gameplayStarRatingDisplayField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "starRatingDisplay");
                modSelectStarRatingDisplayField = AccessTools.Field(typeof(BeatmapAttributesDisplay), "starRatingDisplay");
                tooltipStarRatingField = AccessTools.Field(tooltipType, "starRating");
                tooltipBeatmapField = AccessTools.Field(tooltipContentType, "BeatmapInfo");
                tooltipModsField = AccessTools.Field(tooltipContentType, "Mods");

                if (starChanged == null || titleWedgeLoad == null || titleWedgeUpdate == null
                    || panelUpdate == null || standaloneUpdate == null || spreadUpdate == null
                    || setSpreadLoadComplete == null || setSpreadUpdate == null
                    || gameplayLoad == null || modSelectLoad == null || tooltipSetContent == null
                    || backgroundField == null || starIconField == null || starsTextField == null
                    || titleBeatmapGetter == null || titleModsGetter == null || titleStarRatingDisplayField == null
                    || titleDifficultyTextField == null || titleMappedByTextField == null
                    || titleCountStatisticsDisplayField == null || titleDifficultyStatisticsDisplayField == null
                    || panelBeatmapGetter == null || panelModsGetter == null || panelStarRatingDisplayField == null
                    || standaloneBeatmapGetter == null || standaloneModsGetter == null || standaloneStarRatingDisplayField == null
                    || standaloneSpreadDisplayField == null
                    || gameplayBeatmapField == null || gameplayModsField == null || gameplayStarRatingDisplayField == null
                    || modSelectStarRatingDisplayField == null
                    || tooltipStarRatingField == null || tooltipBeatmapField == null || tooltipModsField == null)
                    throw new MissingMemberException("The native star-rating presentation API has changed.");

                harmony.Patch(starChanged, postfix: new HarmonyMethod(method(nameof(refreshPresentation))));
                harmony.Patch(titleWedgeLoad, postfix: new HarmonyMethod(method(nameof(configureTitleWedge))));
                harmony.Patch(titleWedgeUpdate, postfix: new HarmonyMethod(method(nameof(correctTitleWedgeAccent))));
                harmony.Patch(panelUpdate, postfix: new HarmonyMethod(method(nameof(configurePanelBeatmap))));
                harmony.Patch(standaloneUpdate, postfix: new HarmonyMethod(method(nameof(configureStandaloneBeatmap))));
                harmony.Patch(spreadUpdate, transpiler: new HarmonyMethod(method(nameof(adaptStandaloneSpread))));
                harmony.Patch(setSpreadLoadComplete, postfix: new HarmonyMethod(method(nameof(configureSetSpread))));
                harmony.Patch(setSpreadUpdate, transpiler: new HarmonyMethod(method(nameof(adaptSetSpread))));
                harmony.Patch(gameplayLoad, postfix: new HarmonyMethod(method(nameof(configureGameplay))));
                harmony.Patch(modSelectLoad, postfix: new HarmonyMethod(method(nameof(configureModSelect))));
                harmony.Patch(tooltipSetContent, postfix: new HarmonyMethod(method(nameof(configureTooltip))));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its level star-rating presentation.");
                return false;
            }
        }
    }

    private static MethodInfo method(string name) => AccessTools.Method(typeof(O2JamStarRatingPresentationPatch), name);

    internal static void Configure(StarRatingDisplay display, IBeatmapInfo? beatmap, IEnumerable<Mod>? mods)
    {
        var state = states.GetOrCreateValue(display);
        state.UseLevel = O2JamGameplayProfile.UsesLevelPresentation(beatmap, mods);
        state.Level = beatmap == null ? (ushort)0 : O2JamStarRatingMetadata.ResolveLevel(beatmap);
        apply(display, display.IsLoaded ? display.DisplayedStars.Value : display.Current.Value.Stars);
    }

    private static void refreshPresentation(StarRatingDisplay __instance, ValueChangedEvent<double> __0) =>
        apply(__instance, __0.NewValue);

    private static void apply(StarRatingDisplay display, double stars)
    {
        if (!states.TryGetValue(display, out var state))
            return;

        var background = (Box)backgroundField.GetValue(display)!;
        var starIcon = (SpriteIcon)starIconField.GetValue(display)!;
        var starsText = (OsuSpriteText)starsTextField.GetValue(display)!;
        var nativeFont = state.NativeFont ??= starsText.Font;
        var nativeSpacing = state.NativeSpacing ??= starsText.Spacing;
        LocalisableString formattedStars = stars < 0 ? "-" : stars.FormatStarRating();
        var colourStars = state.UseLevel ? state.Level / 10d : stars;

        background.Colour = colours.ForStarDifficulty(colourStars);
        starIcon.Colour = colours.ForStarDifficultyText(colourStars);
        starsText.Colour = colours.ForStarDifficultyText(colourStars);

        if (state.UseLevel)
        {
            starIcon.Size = new Vector2(0, 8);
            starIcon.Hide();
            // The native grid keeps its 3 px icon-to-text spacer after the icon is hidden.
            // Moving by half that width keeps the level text visually centred in the pill.
            starsText.X = level_text_horizontal_offset;
            starsText.Font = nativeFont.With(fixedWidth: false);
            starsText.Spacing = new Vector2(0, nativeSpacing.Y);
            starsText.Text = O2LazerStrings.LevelBadge(state.Level);
        }
        else
        {
            starIcon.Size = new Vector2(8);
            starIcon.Show();
            starsText.X = 0;
            starsText.Font = nativeFont;
            starsText.Spacing = nativeSpacing;
            starsText.Text = formattedStars;
        }
    }

    private static void configureTitleWedge(BeatmapTitleWedge.DifficultyDisplay __instance)
    {
        var beatmap = (IBindable<WorkingBeatmap>)titleBeatmapGetter.Invoke(__instance, null)!;
        var mods = (IBindable<IReadOnlyList<Mod>>)titleModsGetter.Invoke(__instance, null)!;
        var display = (StarRatingDisplay)titleStarRatingDisplayField.GetValue(__instance)!;
        void update() => Configure(display, beatmap.Value.BeatmapInfo, mods.Value);

        beatmap.BindValueChanged(_ => update());
        mods.BindValueChanged(_ => update(), true);
    }

    private static void correctTitleWedgeAccent(BeatmapTitleWedge.DifficultyDisplay __instance)
    {
        var display = (StarRatingDisplay)titleStarRatingDisplayField.GetValue(__instance)!;
        if (!states.TryGetValue(display, out var state) || !state.UseLevel)
            return;

        // The native branch chooses between the badge's background and text colours using SR.
        // Once the badge is level-coloured, that threshold must use the same level scale.
        var colourStars = state.Level / 10d;
        var colour = colourStars >= OsuColour.STAR_DIFFICULTY_DEFINED_COLOUR_CUTOFF
            ? display.DisplayedDifficultyTextColour
            : display.DisplayedDifficultyColour;

        ((OsuSpriteText)titleDifficultyTextField.GetValue(__instance)!).Colour = colour;
        ((OsuSpriteText)titleMappedByTextField.GetValue(__instance)!).Colour = colour;
        ((BeatmapTitleWedge.DifficultyStatisticsDisplay)titleCountStatisticsDisplayField.GetValue(__instance)!).AccentColour = colour;
        ((BeatmapTitleWedge.DifficultyStatisticsDisplay)titleDifficultyStatisticsDisplayField.GetValue(__instance)!).AccentColour = colour;
    }

    private static void configurePanelBeatmap(PanelBeatmap __instance)
    {
        var mods = (IBindable<IReadOnlyList<Mod>>)panelModsGetter.Invoke(__instance, null)!;
        var display = (StarRatingDisplay)panelStarRatingDisplayField.GetValue(__instance)!;
        Configure(display, __instance.Item == null ? null : (IBeatmapInfo?)panelBeatmapGetter.Invoke(__instance, null), mods.Value);
    }

    private static void configureStandaloneBeatmap(PanelBeatmapStandalone __instance)
    {
        var mods = (IBindable<IReadOnlyList<Mod>>)standaloneModsGetter.Invoke(__instance, null)!;
        var display = (StarRatingDisplay)standaloneStarRatingDisplayField.GetValue(__instance)!;
        var spread = (PanelBeatmapStandalone.SpreadDisplay)standaloneSpreadDisplayField.GetValue(__instance)!;
        var beatmap = __instance.Item == null ? null : (IBeatmapInfo?)standaloneBeatmapGetter.Invoke(__instance, null);
        Configure(display, beatmap, mods.Value);
        configureStandaloneSpread(spread, beatmap, mods.Value);
    }

    private static void configureStandaloneSpread(PanelBeatmapStandalone.SpreadDisplay display, IBeatmapInfo? beatmap, IEnumerable<Mod> mods)
    {
        var useLevel = O2JamGameplayProfile.UsesLevelPresentation(beatmap, mods);
        standaloneSpreadStates.GetOrCreateValue(display).UseLevel = useLevel;
        if (beatmap?.Ruleset.ShortName == O2LazerIdentity.ShortName)
        {
            var stars = useLevel
                ? encodeChartOrder(beatmap)
                : O2JamDisplayedDifficulty.GetStars(beatmap);
            display.StarDifficulty.Value = new StarDifficulty(stars, display.StarDifficulty.Value.MaxCombo);
        }

        standaloneSpreadUpdateMethod.Invoke(display, null);
    }

    private static IEnumerable<CodeInstruction> adaptStandaloneSpread(IEnumerable<CodeInstruction> instructions)
    {
        var linqAdapted = adaptSpreadLinq(instructions, typeof(PanelBeatmapStandalone.SpreadDisplay),
            nameof(orderStandaloneBeatmaps), nameof(selectStandaloneRatings), null);
        return adaptStandaloneSpreadColour(linqAdapted);
    }

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
            ? source.Select(encodeChartOrder)
            : source.Select(nativeSelector);

    private static IEnumerable<CodeInstruction> adaptStandaloneSpreadColour(IEnumerable<CodeInstruction> instructions)
    {
        var result = new List<CodeInstruction>();
        var nativeColour = AccessTools.Method(typeof(OsuColour), nameof(OsuColour.ForStarDifficulty), [typeof(double)]);
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeColour))
            {
                var loadDisplay = new CodeInstruction(OpCodes.Ldarg_0);
                loadDisplay.labels.AddRange(instruction.labels);
                loadDisplay.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                result.Add(loadDisplay);
                instruction.opcode = OpCodes.Call;
                instruction.operand = method(nameof(getStandaloneSpreadColour));
                calls++;
            }

            result.Add(instruction);
        }

        if (calls != 1)
            throw new MissingMemberException("The native standalone difficulty spread colour has changed.");

        return result;
    }

    private static Colour4 getStandaloneSpreadColour(OsuColour colourProvider, double rating,
                                                       PanelBeatmapStandalone.SpreadDisplay display) =>
        colourProvider.ForStarDifficulty(
            standaloneSpreadStates.TryGetValue(display, out var state) && state.UseLevel
                ? decodeChartLevel(rating) / 10d
                : rating);

    private static void configureSetSpread(PanelBeatmapSet.SpreadDisplay __instance)
    {
        var mods = (IBindable<IReadOnlyList<Mod>>)__instance.Dependencies.Get(typeof(IBindable<IReadOnlyList<Mod>>));
        var state = setSpreadStates.GetOrCreateValue(__instance);
        mods.BindValueChanged(change =>
        {
            state.UseLevel = !O2JamGameplayProfile.UsesManiaScore(change.NewValue);
            if (__instance.BeatmapSet.Value?.Beatmaps.Any(isO2LazerBeatmap) == true)
                setSpreadUpdateMethod.Invoke(__instance, null);
        }, true);
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
            ? source.OrderBy(O2JamStarRatingMetadata.ResolveChartOrder)
                    .ThenBy(O2JamStarRatingMetadata.ResolveLevel)
            : source.OrderBy(beatmap => beatmap.StarRating);

    internal static double encodeChartOrder(IBeatmapInfo beatmap) =>
        O2JamStarRatingMetadata.ResolveChartOrder(beatmap) * chart_order_stride
        + O2JamStarRatingMetadata.ResolveLevel(beatmap);

    internal static ushort decodeChartLevel(double encoded) =>
        (ushort)Math.Clamp((int)Math.Round(encoded) % chart_order_stride, 0, ushort.MaxValue);

    private const int chart_order_stride = ushort.MaxValue + 1;

    private static bool isO2LazerBeatmap(IBeatmapInfo beatmap) =>
        beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName;

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
        useLevel && beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName
            ? O2JamStarRatingMetadata.ResolveLevel(beatmap) / 10d
            : beatmap.StarRating;

    private static void configureGameplay(BeatmapMetadataDisplay __instance)
    {
        var beatmap = (IWorkingBeatmap)gameplayBeatmapField.GetValue(__instance)!;
        var mods = (Bindable<IReadOnlyList<Mod>>)gameplayModsField.GetValue(__instance)!;
        var display = (StarRatingDisplay)gameplayStarRatingDisplayField.GetValue(__instance)!;
        mods.BindValueChanged(_ => Configure(display, beatmap.BeatmapInfo, mods.Value), true);
    }

    private static void configureModSelect(BeatmapAttributesDisplay __instance)
    {
        var display = (StarRatingDisplay)modSelectStarRatingDisplayField.GetValue(__instance)!;
        void update() => Configure(display, __instance.BeatmapInfo.Value, __instance.Mods.Value);

        __instance.BeatmapInfo.BindValueChanged(_ => update());
        __instance.Mods.BindValueChanged(_ => update(), true);
    }

    private static void configureTooltip(object __instance, object __0)
    {
        var display = (StarRatingDisplay)tooltipStarRatingField.GetValue(__instance)!;
        var beatmap = (IBeatmapInfo)tooltipBeatmapField.GetValue(__0)!;
        var mods = (IEnumerable<Mod>?)tooltipModsField.GetValue(__0);
        Configure(display, beatmap, mods);
    }

    private sealed class PresentationState
    {
        public bool UseLevel;
        public ushort Level;
        public FontUsage? NativeFont;
        public Vector2? NativeSpacing;
    }

    private sealed class SpreadPresentationState
    {
        public bool UseLevel;
    }
}
