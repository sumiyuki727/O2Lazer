using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Runtime.CompilerServices;
using System.Threading;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Overlays.Mods;
using osu.Game.Rulesets;
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
    private const float level_text_horizontal_offset = -1.5f;
    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<StarRatingDisplay, PresentationState> states = new();
    private static readonly ConditionalWeakTable<object, OwnerPresentationState> ownerStates = new();
    private static readonly ConditionalWeakTable<PanelBeatmapStandalone.SpreadDisplay, SpreadPresentationState> standaloneSpreadStates = new();
    private static readonly ConditionalWeakTable<PanelBeatmapSet.SpreadDisplay, SpreadPresentationState> setSpreadStates = new();

    private static FieldInfo starIconField = null!;
    private static FieldInfo starsTextField = null!;
    private static MethodInfo titleModsGetter = null!;
    private static MethodInfo titleRulesetGetter = null!;
    private static FieldInfo titleStarRatingDisplayField = null!;
    private static FieldInfo titleCountStatisticsDisplayField = null!;
    private static MethodInfo titleUpdateDifficultyStatisticsMethod = null!;
    private static MethodInfo panelModsGetter = null!;
    private static MethodInfo panelRulesetGetter = null!;
    private static FieldInfo panelStarRatingDisplayField = null!;
    private static FieldInfo panelStarCounterField = null!;
    private static MethodInfo standaloneModsGetter = null!;
    private static MethodInfo standaloneRulesetGetter = null!;
    private static FieldInfo standaloneStarRatingDisplayField = null!;
    private static FieldInfo standaloneSpreadDisplayField = null!;
    private static MethodInfo standaloneSpreadUpdateMethod = null!;
    private static MethodInfo setSpreadUpdateMethod = null!;
    private static FieldInfo gameplayModsField = null!;
    private static FieldInfo gameplayStarRatingDisplayField = null!;
    private static FieldInfo modSelectGameRulesetField = null!;
    private static FieldInfo modSelectStarRatingDisplayField = null!;
    private static FieldInfo tooltipStarRatingField = null!;
    private static FieldInfo tooltipBeatmapField = null!;
    private static FieldInfo tooltipDifficultyField = null!;
    private static FieldInfo tooltipRulesetField = null!;
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
                var titleUpdateDisplay = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "updateDisplay");
                var panelUpdate = AccessTools.Method(typeof(PanelBeatmap), "updateKeyCount");
                var panelCompute = AccessTools.Method(typeof(PanelBeatmap), "computeStarRating");
                var panelDifficultyChanged = AccessTools.GetDeclaredMethods(typeof(PanelBeatmap))
                                                        .SingleOrDefault(method => method.Name.StartsWith("<computeStarRating>b__", StringComparison.Ordinal)
                                                                                   && method.GetParameters().Length == 1
                                                                                   && method.GetParameters()[0].ParameterType == typeof(ValueChangedEvent<StarDifficulty>));
                var standaloneUpdate = AccessTools.Method(typeof(PanelBeatmapStandalone), "updateKeyCount");
                var standaloneCompute = AccessTools.Method(typeof(PanelBeatmapStandalone), "computeStarRating");
                var spreadUpdate = AccessTools.Method(typeof(PanelBeatmapStandalone.SpreadDisplay), "updateBeatmap");
                var setSpreadLoad = AccessTools.Method(typeof(PanelBeatmapSet.SpreadDisplay), "load");
                var setSpreadUpdate = AccessTools.Method(typeof(PanelBeatmapSet.SpreadDisplay), "updateBeatmapSet");
                var gameplayLoad = AccessTools.Method(typeof(BeatmapMetadataDisplay), "load");
                var modSelectLoad = AccessTools.Method(typeof(BeatmapAttributesDisplay), "LoadComplete");
                var modSelectUpdateDifficulty = AccessTools.Method(typeof(BeatmapAttributesDisplay), "updateStarDifficultyBindable");
                var tooltipType = typeof(DifficultyIcon).Assembly.GetType("osu.Game.Beatmaps.Drawables.DifficultyIconTooltip");
                var tooltipContentType = typeof(DifficultyIcon).Assembly.GetType("osu.Game.Beatmaps.Drawables.DifficultyIconTooltipContent");
                var tooltipSetContent = AccessTools.Method(tooltipType, "SetContent");

                starIconField = AccessTools.Field(typeof(StarRatingDisplay), "starIcon");
                starsTextField = AccessTools.Field(typeof(StarRatingDisplay), "starsText");
                titleModsGetter = AccessTools.PropertyGetter(typeof(BeatmapTitleWedge.DifficultyDisplay), "mods");
                titleRulesetGetter = AccessTools.PropertyGetter(typeof(BeatmapTitleWedge.DifficultyDisplay), "ruleset");
                titleStarRatingDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "starRatingDisplay");
                titleCountStatisticsDisplayField = AccessTools.Field(typeof(BeatmapTitleWedge.DifficultyDisplay), "countStatisticsDisplay");
                titleUpdateDifficultyStatisticsMethod = AccessTools.Method(typeof(BeatmapTitleWedge.DifficultyDisplay), "updateDifficultyStatistics");
                panelModsGetter = AccessTools.PropertyGetter(typeof(PanelBeatmap), "mods");
                panelRulesetGetter = AccessTools.PropertyGetter(typeof(PanelBeatmap), "ruleset");
                panelStarRatingDisplayField = AccessTools.Field(typeof(PanelBeatmap), "starRatingDisplay");
                panelStarCounterField = AccessTools.Field(typeof(PanelBeatmap), "starCounter");
                standaloneModsGetter = AccessTools.PropertyGetter(typeof(PanelBeatmapStandalone), "mods");
                standaloneRulesetGetter = AccessTools.PropertyGetter(typeof(PanelBeatmapStandalone), "ruleset");
                standaloneStarRatingDisplayField = AccessTools.Field(typeof(PanelBeatmapStandalone), "starRatingDisplay");
                standaloneSpreadDisplayField = AccessTools.Field(typeof(PanelBeatmapStandalone), "spreadDisplay");
                standaloneSpreadUpdateMethod = spreadUpdate;
                setSpreadUpdateMethod = setSpreadUpdate;
                gameplayModsField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "mods");
                gameplayStarRatingDisplayField = AccessTools.Field(typeof(BeatmapMetadataDisplay), "starRatingDisplay");
                modSelectGameRulesetField = AccessTools.Field(typeof(BeatmapAttributesDisplay), "GameRuleset");
                modSelectStarRatingDisplayField = AccessTools.Field(typeof(BeatmapAttributesDisplay), "starRatingDisplay");
                tooltipStarRatingField = AccessTools.Field(tooltipType, "starRating");
                tooltipBeatmapField = AccessTools.Field(tooltipContentType, "BeatmapInfo");
                tooltipDifficultyField = AccessTools.Field(tooltipContentType, "Difficulty");
                tooltipRulesetField = AccessTools.Field(tooltipContentType, "Ruleset");
                tooltipModsField = AccessTools.Field(tooltipContentType, "Mods");

                if (starChanged == null || titleWedgeLoad == null || titleUpdateDisplay == null
                    || panelUpdate == null || panelCompute == null || panelDifficultyChanged == null
                    || standaloneUpdate == null || standaloneCompute == null
                    || spreadUpdate == null || setSpreadLoad == null || setSpreadUpdate == null
                    || gameplayLoad == null || modSelectLoad == null || modSelectUpdateDifficulty == null || tooltipSetContent == null
                    || starIconField == null || starsTextField == null
                    || titleModsGetter == null || titleRulesetGetter == null || titleStarRatingDisplayField == null
                    || titleCountStatisticsDisplayField == null || titleUpdateDifficultyStatisticsMethod == null
                    || panelModsGetter == null || panelRulesetGetter == null || panelStarRatingDisplayField == null || panelStarCounterField == null
                    || standaloneModsGetter == null || standaloneRulesetGetter == null || standaloneStarRatingDisplayField == null
                    || standaloneSpreadDisplayField == null
                    || gameplayModsField == null || gameplayStarRatingDisplayField == null
                    || modSelectGameRulesetField == null || modSelectStarRatingDisplayField == null
                    || tooltipStarRatingField == null || tooltipBeatmapField == null || tooltipDifficultyField == null
                    || tooltipRulesetField == null || tooltipModsField == null)
                    throw new MissingMemberException("The native star-rating presentation API has changed.");

                var bindingTranspiler = new HarmonyMethod(method(nameof(adaptDifficultyBinding)));
                harmony.Patch(starChanged, postfix: new HarmonyMethod(method(nameof(refreshPresentation))));
                harmony.Patch(titleWedgeLoad, postfix: new HarmonyMethod(method(nameof(configureTitleWedge))));
                harmony.Patch(titleUpdateDisplay, transpiler: bindingTranspiler);
                harmony.Patch(panelUpdate, postfix: new HarmonyMethod(method(nameof(refreshOwnerPresentation))));
                harmony.Patch(panelCompute, transpiler: bindingTranspiler);
                harmony.Patch(panelDifficultyChanged, transpiler: new HarmonyMethod(method(nameof(useNativePanelStarCounter))));
                harmony.Patch(standaloneUpdate, postfix: new HarmonyMethod(method(nameof(refreshOwnerPresentation))));
                harmony.Patch(standaloneCompute, transpiler: bindingTranspiler);
                harmony.Patch(spreadUpdate, transpiler: new HarmonyMethod(method(nameof(adaptStandaloneSpread))));
                harmony.Patch(setSpreadLoad, postfix: new HarmonyMethod(method(nameof(configureSetSpread))));
                harmony.Patch(setSpreadUpdate, transpiler: new HarmonyMethod(method(nameof(adaptSetSpread))));
                harmony.Patch(gameplayLoad,
                    transpiler: bindingTranspiler,
                    postfix: new HarmonyMethod(method(nameof(configureGameplay))));
                harmony.Patch(modSelectLoad, postfix: new HarmonyMethod(method(nameof(configureModSelect))));
                harmony.Patch(modSelectUpdateDifficulty, transpiler: bindingTranspiler);
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

    private static IEnumerable<CodeInstruction> adaptDifficultyBinding(IEnumerable<CodeInstruction> instructions)
    {
        var nativeLookup = AccessTools.Method(typeof(BeatmapDifficultyCache), nameof(BeatmapDifficultyCache.GetBindableDifficulty),
            [typeof(IBeatmapInfo), typeof(CancellationToken), typeof(int)]);
        var replacement = method(nameof(getColourDifficulty));
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(nativeLookup))
            {
                var loadOwner = new CodeInstruction(OpCodes.Ldarg_0);
                loadOwner.labels.AddRange(instruction.labels);
                loadOwner.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return loadOwner;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                calls++;
            }

            yield return instruction;
        }

        if (calls != 1)
            throw new MissingMemberException("A native difficulty display binding has changed.");
    }

    private static IBindable<StarDifficulty> getColourDifficulty(BeatmapDifficultyCache cache, IBeatmapInfo beatmap,
                                                                 CancellationToken cancellationToken, int computationDelay,
                                                                 object owner)
    {
        var native = cache.GetBindableDifficulty(beatmap, cancellationToken, computationDelay);
        var (ruleset, mods) = getProfile(owner, beatmap);
        var display = getDisplay(owner);
        if (!isO2LazerBeatmap(beatmap))
        {
            Configure(display, beatmap, mods, ruleset);
            ownerStates.Remove(owner);
            states.Remove(display);
            return native;
        }

        var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset, mods);
        ownerStates.GetOrCreateValue(owner).Binding = binding;
        var displayState = states.GetOrCreateValue(display);
        displayState.NativeBeatmap = beatmap;
        displayState.NativeDifficulty = native;
        displayState.NativeDifficultyReady = O2JamStarRatingMetadata.ReadMania(beatmap) != null;
        Configure(display, beatmap, mods, ruleset);

        if (owner is PanelBeatmapStandalone standalone)
            setStandaloneSpreadMode(standalone, binding.UsesLevelColour, false);

        if (owner is PanelBeatmap panel)
            binding.NativeDifficultyUpdated += _ => updatePanelStarCounter(panel, binding);

        if (owner is BeatmapTitleWedge.DifficultyDisplay title)
        {
            binding.NativeDifficultyUpdated += starsChanged =>
            {
                if (ownerStates.TryGetValue(title, out var current) && ReferenceEquals(current.Binding, binding))
                {
                    var becameReady = !displayState.NativeDifficultyReady;
                    displayState.NativeDifficultyReady = true;
                    if (becameReady || starsChanged)
                        titleUpdateDifficultyStatisticsMethod.Invoke(title, null);
                }
            };
        }

        return binding.ColourDifficulty;
    }

    private static IEnumerable<CodeInstruction> useNativePanelStarCounter(IEnumerable<CodeInstruction> instructions)
    {
        var setter = AccessTools.PropertySetter(typeof(StarCounter), nameof(StarCounter.Current));
        var replacement = method(nameof(setPanelStarCounter));
        var calls = 0;

        foreach (var instruction in instructions)
        {
            if (instruction.Calls(setter))
            {
                var loadPanel = new CodeInstruction(OpCodes.Ldarg_0);
                loadPanel.labels.AddRange(instruction.labels);
                loadPanel.blocks.AddRange(instruction.blocks);
                instruction.labels.Clear();
                instruction.blocks.Clear();
                yield return loadPanel;
                instruction.opcode = OpCodes.Call;
                instruction.operand = replacement;
                calls++;
            }

            yield return instruction;
        }

        if (calls != 1)
            throw new MissingMemberException("The native carousel star counter has changed.");
    }

    private static void setPanelStarCounter(StarCounter counter, float fallbackStars, PanelBeatmap panel)
    {
        if (ownerStates.TryGetValue(panel, out var state) && state.Binding != null)
            counter.Current = (float)state.Binding.Native.Value.Stars;
        else
            counter.Current = fallbackStars;
    }

    private static void updatePanelStarCounter(PanelBeatmap panel, O2JamDifficultyColourBinding binding)
    {
        if (!ownerStates.TryGetValue(panel, out var state) || !ReferenceEquals(state.Binding, binding))
            return;

        ((StarCounter)panelStarCounterField.GetValue(panel)!).Current = (float)binding.Native.Value.Stars;
    }

    internal static void Configure(StarRatingDisplay display, IBeatmapInfo? beatmap, IEnumerable<Mod>? mods,
                                   IRulesetInfo? ruleset = null)
    {
        ruleset ??= beatmap?.Ruleset;
        var state = states.GetOrCreateValue(display);
        state.UseLevel = O2JamDifficultyColourBinding.UsesLevel(beatmap, ruleset, mods);
        state.Level = beatmap == null ? (ushort)0 : O2JamStarRatingMetadata.ResolveLevel(beatmap);

        double displayedValue = display.IsLoaded ? display.DisplayedStars.Value : display.Current.Value.Stars;
        applyLabel(display, displayedValue);
    }

    internal static void ConfigureStatic(StarRatingDisplay display, IBeatmapInfo beatmap, IEnumerable<Mod>? mods,
                                         IRulesetInfo? ruleset, StarDifficulty nativeDifficulty)
    {
        ruleset ??= beatmap.Ruleset;
        if (!O2JamDifficultyColourBinding.UsesLevel(beatmap, ruleset, mods))
            return;

        Configure(display, beatmap, mods, ruleset);

        // StarRatingDisplay derives its colour and transition from Current. The patched label path
        // deliberately ignores this colour-only value while level presentation is active.
        display.Current.Value = O2JamDifficultyColourBinding.GetColourDifficulty(beatmap, ruleset, mods, nativeDifficulty);
    }

    internal static double GetNativeStars(StarRatingDisplay display, IBeatmapInfo beatmap)
    {
        if (states.TryGetValue(display, out var state)
            && state.NativeBeatmap?.Equals(beatmap) == true
            && state.NativeDifficulty != null
            && state.NativeDifficultyReady)
            return state.NativeDifficulty.Value.Stars;

        return O2JamDisplayedDifficulty.GetStars(beatmap);
    }

    private static void refreshPresentation(StarRatingDisplay __instance, ValueChangedEvent<double> __0)
    {
        if (states.TryGetValue(__instance, out _))
            applyLabel(__instance, __0.NewValue);
    }

    private static void applyLabel(StarRatingDisplay display, double displayedValue)
    {
        if (!states.TryGetValue(display, out var state))
            return;

        var starIcon = (SpriteIcon)starIconField.GetValue(display)!;
        var starsText = (OsuSpriteText)starsTextField.GetValue(display)!;
        var nativeFont = state.NativeFont ??= starsText.Font;
        var nativeSpacing = state.NativeSpacing ??= starsText.Spacing;

        if (state.UseLevel)
        {
            starIcon.Size = new Vector2(0, 8);
            starIcon.Hide();
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
            starsText.Text = displayedValue < 0 ? "-" : displayedValue.FormatStarRating();
        }
    }

    private static void configureTitleWedge(BeatmapTitleWedge.DifficultyDisplay __instance)
    {
        var mods = (IBindable<IReadOnlyList<Mod>>)titleModsGetter.Invoke(__instance, null)!;
        var ruleset = (IBindable<RulesetInfo>)titleRulesetGetter.Invoke(__instance, null)!;
        mods.BindValueChanged(_ => refreshOwnerPresentation(__instance), true);
        ruleset.BindValueChanged(change =>
        {
            refreshOwnerPresentation(__instance);
            if (ShouldAnimateCounts(change.OldValue?.ShortName, change.NewValue?.ShortName))
            {
                var counts = (BeatmapTitleWedge.DifficultyStatisticsDisplay)titleCountStatisticsDisplayField.GetValue(__instance)!;
                counts.FadeOut(0).FadeIn(200, Easing.InQuint);
            }
        });
    }

    internal static bool ShouldAnimateCounts(string? previous, string? current) =>
        previous == "mania" && current == O2LazerIdentity.ShortName
        || previous == O2LazerIdentity.ShortName && current == "mania";

    private static void refreshOwnerPresentation(object __instance)
    {
        if (!ownerStates.TryGetValue(__instance, out var state) || state.Binding == null)
            return;

        var beatmap = state.Binding.Beatmap;
        var (ruleset, mods) = getProfile(__instance, beatmap);
        Configure(getDisplay(__instance), beatmap, mods, ruleset);
        state.Binding.UpdateProfile(ruleset, mods);

        if (__instance is PanelBeatmapStandalone standalone)
            setStandaloneSpreadMode(standalone, state.Binding.UsesLevelColour, true);
    }

    private static (IRulesetInfo? Ruleset, IEnumerable<Mod>? Mods) getProfile(object owner, IBeatmapInfo beatmap) => owner switch
    {
        BeatmapTitleWedge.DifficultyDisplay title =>
            (((IBindable<RulesetInfo>)titleRulesetGetter.Invoke(title, null)!).Value,
             ((IBindable<IReadOnlyList<Mod>>)titleModsGetter.Invoke(title, null)!).Value),
        PanelBeatmap panel =>
            (((IBindable<RulesetInfo>)panelRulesetGetter.Invoke(panel, null)!).Value,
             ((IBindable<IReadOnlyList<Mod>>)panelModsGetter.Invoke(panel, null)!).Value),
        PanelBeatmapStandalone standalone =>
            (((IBindable<RulesetInfo>)standaloneRulesetGetter.Invoke(standalone, null)!).Value,
             ((IBindable<IReadOnlyList<Mod>>)standaloneModsGetter.Invoke(standalone, null)!).Value),
        BeatmapMetadataDisplay gameplay => (beatmap.Ruleset, gameplay.Mods.Value),
        BeatmapAttributesDisplay modSelect =>
            (((IBindable<RulesetInfo>)modSelectGameRulesetField.GetValue(modSelect)!).Value, modSelect.Mods.Value),
        _ => (beatmap.Ruleset, null),
    };

    private static StarRatingDisplay getDisplay(object owner) => owner switch
    {
        BeatmapTitleWedge.DifficultyDisplay title => (StarRatingDisplay)titleStarRatingDisplayField.GetValue(title)!,
        PanelBeatmap panel => (StarRatingDisplay)panelStarRatingDisplayField.GetValue(panel)!,
        PanelBeatmapStandalone standalone => (StarRatingDisplay)standaloneStarRatingDisplayField.GetValue(standalone)!,
        BeatmapMetadataDisplay gameplay => (StarRatingDisplay)gameplayStarRatingDisplayField.GetValue(gameplay)!,
        BeatmapAttributesDisplay modSelect => (StarRatingDisplay)modSelectStarRatingDisplayField.GetValue(modSelect)!,
        _ => throw new InvalidOperationException($"Unsupported difficulty presentation owner: {owner.GetType().FullName}"),
    };

    private static void setStandaloneSpreadMode(PanelBeatmapStandalone panel, bool useLevel, bool refresh)
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
            ? source.Select(GetLevelColourStars)
            : source.Select(nativeSelector);

    private static void configureSetSpread(PanelBeatmapSet.SpreadDisplay __instance)
    {
        var mods = (IBindable<IReadOnlyList<Mod>>)__instance.Dependencies.Get(typeof(IBindable<IReadOnlyList<Mod>>));
        var ruleset = (IBindable<RulesetInfo>)__instance.Dependencies.Get(typeof(IBindable<RulesetInfo>));
        void update() => updateSetSpreadMode(__instance, ruleset.Value.ShortName == O2LazerIdentity.ShortName
                                                         && !O2JamGameplayProfile.UsesManiaScore(mods.Value));

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
            ? source.OrderBy(O2JamStarRatingMetadata.ResolveLevel)
                    .ThenBy(O2JamStarRatingMetadata.ResolveChartOrder)
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

    private static void configureGameplay(BeatmapMetadataDisplay __instance)
    {
        var mods = (Bindable<IReadOnlyList<Mod>>)gameplayModsField.GetValue(__instance)!;
        mods.BindValueChanged(_ => refreshOwnerPresentation(__instance), true);
    }

    private static void configureModSelect(BeatmapAttributesDisplay __instance)
    {
        __instance.Mods.BindValueChanged(_ => refreshOwnerPresentation(__instance), true);
        var ruleset = (IBindable<RulesetInfo>)modSelectGameRulesetField.GetValue(__instance)!;
        ruleset.BindValueChanged(_ => refreshOwnerPresentation(__instance), true);
    }

    private static void configureTooltip(object __instance, object __0)
    {
        var display = (StarRatingDisplay)tooltipStarRatingField.GetValue(__instance)!;
        var beatmap = (IBeatmapInfo)tooltipBeatmapField.GetValue(__0)!;
        var native = (IBindable<StarDifficulty>)tooltipDifficultyField.GetValue(__0)!;
        var ruleset = (IRulesetInfo)tooltipRulesetField.GetValue(__0)!;
        var mods = (IEnumerable<Mod>?)tooltipModsField.GetValue(__0);
        if (!O2JamDifficultyColourBinding.UsesLevel(beatmap, ruleset, mods))
        {
            Configure(display, beatmap, mods, ruleset);
            states.Remove(display);
            return;
        }

        Configure(display, beatmap, mods, ruleset);
        var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset, mods);
        display.Current = binding.ColourDifficulty;
    }

    private sealed class PresentationState
    {
        public bool UseLevel;
        public ushort Level;
        public FontUsage? NativeFont;
        public Vector2? NativeSpacing;
        public IBeatmapInfo? NativeBeatmap;
        public IBindable<StarDifficulty>? NativeDifficulty;
        public bool NativeDifficultyReady;
    }

    private sealed class OwnerPresentationState
    {
        public O2JamDifficultyColourBinding? Binding;
    }

    private sealed class SpreadPresentationState
    {
        public bool Initialised;
        public bool UseLevel;
    }
}
