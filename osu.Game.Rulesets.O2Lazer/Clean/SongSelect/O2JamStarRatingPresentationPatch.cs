using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
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

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamStarRatingPresentationPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.StarRatingPresentation";
    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<StarRatingDisplay, PresentationState> states = new();
    private static readonly ConditionalWeakTable<PanelBeatmapStandalone.SpreadDisplay, SpreadPresentationState> spreadStates = new();
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
    private static FieldInfo spreadPrecedingField = null!;
    private static FieldInfo spreadSucceedingField = null!;
    private static FieldInfo spreadRulesetField = null!;
    private static FieldInfo spreadShowConvertedBeatmapsField = null!;
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
                spreadPrecedingField = AccessTools.Field(typeof(PanelBeatmapStandalone.SpreadDisplay), "preceding");
                spreadSucceedingField = AccessTools.Field(typeof(PanelBeatmapStandalone.SpreadDisplay), "succeeding");
                spreadRulesetField = AccessTools.Field(typeof(PanelBeatmapStandalone.SpreadDisplay), "<ruleset>k__BackingField");
                spreadShowConvertedBeatmapsField = AccessTools.Field(typeof(PanelBeatmapStandalone.SpreadDisplay), "showConvertedBeatmaps");
                gameplayBeatmapField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "beatmap");
                gameplayModsField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "mods");
                gameplayStarRatingDisplayField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "starRatingDisplay");
                modSelectStarRatingDisplayField = AccessTools.Field(typeof(BeatmapAttributesDisplay), "starRatingDisplay");
                tooltipStarRatingField = AccessTools.Field(tooltipType, "starRating");
                tooltipBeatmapField = AccessTools.Field(tooltipContentType, "BeatmapInfo");
                tooltipModsField = AccessTools.Field(tooltipContentType, "Mods");

                if (starChanged == null || titleWedgeLoad == null || titleWedgeUpdate == null
                    || panelUpdate == null || standaloneUpdate == null || spreadUpdate == null
                    || gameplayLoad == null || modSelectLoad == null || tooltipSetContent == null
                    || backgroundField == null || starIconField == null || starsTextField == null
                    || titleBeatmapGetter == null || titleModsGetter == null || titleStarRatingDisplayField == null
                    || titleDifficultyTextField == null || titleMappedByTextField == null
                    || titleCountStatisticsDisplayField == null || titleDifficultyStatisticsDisplayField == null
                    || panelBeatmapGetter == null || panelModsGetter == null || panelStarRatingDisplayField == null
                    || standaloneBeatmapGetter == null || standaloneModsGetter == null || standaloneStarRatingDisplayField == null
                    || standaloneSpreadDisplayField == null || spreadPrecedingField == null || spreadSucceedingField == null
                    || spreadRulesetField == null || spreadShowConvertedBeatmapsField == null
                    || gameplayBeatmapField == null || gameplayModsField == null || gameplayStarRatingDisplayField == null
                    || modSelectStarRatingDisplayField == null
                    || tooltipStarRatingField == null || tooltipBeatmapField == null || tooltipModsField == null)
                    throw new MissingMemberException("The native star-rating presentation API has changed.");

                harmony.Patch(starChanged, postfix: new HarmonyMethod(method(nameof(refreshPresentation))));
                harmony.Patch(titleWedgeLoad, postfix: new HarmonyMethod(method(nameof(configureTitleWedge))));
                harmony.Patch(titleWedgeUpdate, postfix: new HarmonyMethod(method(nameof(correctTitleWedgeAccent))));
                harmony.Patch(panelUpdate, postfix: new HarmonyMethod(method(nameof(configurePanelBeatmap))));
                harmony.Patch(standaloneUpdate, postfix: new HarmonyMethod(method(nameof(configureStandaloneBeatmap))));
                harmony.Patch(spreadUpdate, postfix: new HarmonyMethod(method(nameof(refreshStandaloneSpread))));
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
        state.UseLevel = usesLevelPresentation(beatmap, mods);
        state.Level = beatmap == null ? (ushort)0 : O2JamStarRatingMetadata.ResolveLevel(beatmap);
        apply(display, display.IsLoaded ? display.DisplayedStars.Value : display.Current.Value.Stars);
    }

    private static bool containsManiaScore(IEnumerable<Mod>? mods) =>
        mods != null && ModUtils.FlattenMods(mods).Any(mod => mod is O2JamModManiaScore);

    private static bool usesLevelPresentation(IBeatmapInfo? beatmap, IEnumerable<Mod>? mods) =>
        beatmap?.Ruleset.ShortName == O2LazerIdentity.ShortName && !containsManiaScore(mods);

    private static void refreshPresentation(StarRatingDisplay __instance, ValueChangedEvent<double> __0) =>
        apply(__instance, __0.NewValue);

    private static void apply(StarRatingDisplay display, double stars)
    {
        if (!states.TryGetValue(display, out var state))
            return;

        var background = (Box)backgroundField.GetValue(display)!;
        var starIcon = (SpriteIcon)starIconField.GetValue(display)!;
        var starsText = (OsuSpriteText)starsTextField.GetValue(display)!;
        LocalisableString formattedStars = stars < 0 ? "-" : stars.FormatStarRating();
        var colourStars = state.UseLevel ? state.Level / 10d : stars;

        background.Colour = colours.ForStarDifficulty(colourStars);
        starIcon.Colour = colours.ForStarDifficultyText(colourStars);
        starsText.Colour = colours.ForStarDifficultyText(colourStars);

        if (state.UseLevel)
        {
            starIcon.Size = new Vector2(0, 8);
            starIcon.Hide();
            starsText.Text = O2LazerStrings.LevelBadge(state.Level);
        }
        else
        {
            starIcon.Size = new Vector2(8);
            starIcon.Show();
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
        configureSpread(spread, usesLevelPresentation(beatmap, mods.Value));
    }

    private static void configureSpread(PanelBeatmapStandalone.SpreadDisplay display, bool useLevel)
    {
        spreadStates.GetOrCreateValue(display).UseLevel = useLevel;
        applySpread(display);
    }

    private static void refreshStandaloneSpread(PanelBeatmapStandalone.SpreadDisplay __instance) => applySpread(__instance);

    private static void applySpread(PanelBeatmapStandalone.SpreadDisplay display)
    {
        if (!spreadStates.TryGetValue(display, out var state) || display.Beatmap.Value?.BeatmapSet == null)
            return;

        var ruleset = (IBindable<RulesetInfo>)spreadRulesetField.GetValue(display)!;
        var showConvertedBeatmaps = (Bindable<bool>)spreadShowConvertedBeatmapsField.GetValue(display)!;
        var preceding = (FillFlowContainer)spreadPrecedingField.GetValue(display)!;
        var succeeding = (FillFlowContainer)spreadSucceedingField.GetValue(display)!;

        // Native spread dots retain only the numeric star value. Rebuild the same visible window
        // so each dot can use its own chart level without changing native ordering or placement.
        var otherBeatmaps = display.Beatmap.Value.BeatmapSet.Beatmaps
                                   .Except([display.Beatmap.Value])
                                   .Where(beatmap => beatmap.AllowGameplayWithRuleset(ruleset.Value, showConvertedBeatmaps.Value))
                                   .OrderBy(beatmap => beatmap.StarRating)
                                   .ToList();

        if (otherBeatmaps.Count == 0)
            return;

        const int max_difficulties_total = 11;
        var startIndex = 0;
        var endIndex = otherBeatmaps.Count - 1;

        if (otherBeatmaps.Count > max_difficulties_total)
        {
            var otherStarDifficulties = otherBeatmaps.Select(beatmap => beatmap.StarRating).ToList();
            startIndex = otherStarDifficulties.BinarySearch(display.StarDifficulty.Value.Stars);
            if (startIndex < 0)
                startIndex = ~startIndex - 1;

            startIndex = Math.Clamp(startIndex - max_difficulties_total / 2, 0, otherBeatmaps.Count - 1);
            endIndex = Math.Clamp(startIndex + max_difficulties_total, 0, otherBeatmaps.Count - 1);
        }

        var precedingIndex = 0;
        var succeedingIndex = 0;

        for (var i = startIndex; i <= endIndex; i++)
        {
            var beatmap = otherBeatmaps[i];
            var target = beatmap.StarRating < display.StarDifficulty.Value.Stars ? preceding : succeeding;
            var childIndex = target == preceding ? precedingIndex++ : succeedingIndex++;
            if (childIndex < target.Count && target[childIndex] is Circle circle)
                circle.Colour = colours.ForStarDifficulty(GetColourStars(beatmap, state.UseLevel));
        }
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
    }

    private sealed class SpreadPresentationState
    {
        public bool UseLevel;
    }
}
