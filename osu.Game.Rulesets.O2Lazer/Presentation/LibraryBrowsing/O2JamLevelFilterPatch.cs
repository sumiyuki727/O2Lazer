using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Graphics.Sprites;
using osu.Game.Graphics.UserInterface;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Screens.Select;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamLevelFilterPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.LevelFilter";
    private const int maximum_selectable_level = 100;
    private const int unlimited_level = maximum_selectable_level + 1;

    private static readonly object installLock = new();
    private static readonly ConditionalWeakTable<FilterControl.DifficultyRangeSlider, FilterState> states = new();
    private static readonly ConditionalWeakTable<object, FilterState> boundSliderStates = new();

    private static FieldInfo difficultyRangeSliderField = null!;
    private static MethodInfo rulesetGetter = null!;
    private static MethodInfo modsGetter = null!;
    private static MethodInfo configGetter = null!;
    private static MethodInfo lowerBoundSliderGetter = null!;
    private static MethodInfo upperBoundSliderGetter = null!;
    private static MethodInfo sliderContainerGetter = null!;
    private static MethodInfo internalChildrenGetter = null!;
    private static MethodInfo nubTextGetter = null!;
    private static MethodInfo updateDisplayMethod = null!;

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
                var filterLoad = AccessTools.Method(typeof(FilterControl), "load");
                var filterLoadComplete = AccessTools.Method(typeof(FilterControl), "LoadComplete");
                var filterDispose = AccessTools.Method(typeof(FilterControl), "Dispose", [typeof(bool)]);
                var createCriteria = AccessTools.Method(typeof(FilterControl), nameof(FilterControl.CreateCriteria));
                var boundSliderType = AccessTools.Inner(typeof(ShearedRangeSlider), "BoundSliderBar");
                var difficultyBoundSliderType = AccessTools.Inner(typeof(FilterControl.DifficultyRangeSlider), "DifficultyBoundSliderBar");

                difficultyRangeSliderField = AccessTools.Field(typeof(FilterControl), "difficultyRangeSlider");
                rulesetGetter = AccessTools.PropertyGetter(typeof(FilterControl), "ruleset");
                modsGetter = AccessTools.PropertyGetter(typeof(FilterControl), "mods");
                configGetter = AccessTools.PropertyGetter(typeof(FilterControl), "config");
                lowerBoundSliderGetter = AccessTools.PropertyGetter(typeof(ShearedRangeSlider), "LowerBoundSlider");
                upperBoundSliderGetter = AccessTools.PropertyGetter(typeof(ShearedRangeSlider), "UpperBoundSlider");
                sliderContainerGetter = AccessTools.PropertyGetter(typeof(ShearedRangeSlider), "SliderContainer");
                internalChildrenGetter = AccessTools.PropertyGetter(typeof(CompositeDrawable), "InternalChildren");
                nubTextGetter = AccessTools.PropertyGetter(boundSliderType, "NubText");
                updateDisplayMethod = AccessTools.Method(difficultyBoundSliderType, "UpdateDisplay");
                var tooltipGetter = AccessTools.PropertyGetter(difficultyBoundSliderType, "TooltipText");

                if (filterLoad == null || filterLoadComplete == null || filterDispose == null || createCriteria == null
                    || boundSliderType == null || difficultyBoundSliderType == null || difficultyRangeSliderField == null
                    || rulesetGetter == null || modsGetter == null || configGetter == null || lowerBoundSliderGetter == null
                    || upperBoundSliderGetter == null || sliderContainerGetter == null || internalChildrenGetter == null || nubTextGetter == null
                    || updateDisplayMethod == null || tooltipGetter == null)
                    throw new MissingMemberException("The native song-select difficulty filter API has changed.");

                harmony.Patch(filterLoad, postfix: new HarmonyMethod(method(nameof(trackFilterControl))));
                harmony.Patch(filterLoadComplete, postfix: new HarmonyMethod(method(nameof(completeFilterControl))));
                harmony.Patch(filterDispose, prefix: new HarmonyMethod(method(nameof(detachFilterControl))));
                harmony.Patch(createCriteria, postfix: new HarmonyMethod(method(nameof(adaptFilterCriteria))));
                harmony.Patch(updateDisplayMethod,
                    prefix: new HarmonyMethod(method(nameof(adaptLevelColour))),
                    postfix: new HarmonyMethod(method(nameof(formatLevelNub))));
                harmony.Patch(tooltipGetter, postfix: new HarmonyMethod(method(nameof(formatLevelTooltip))));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                O2JamPatchRollback.Unpatch(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its level filter adapter.");
                return false;
            }
        }
    }

    private static MethodInfo method(string name) => AccessTools.Method(typeof(O2JamLevelFilterPatch), name);

    private static void trackFilterControl(FilterControl __instance)
    {
        var slider = getSlider(__instance);
        if (slider == null)
            return;

        var ruleset = (IBindable<RulesetInfo>)rulesetGetter.Invoke(__instance, null)!;
        var mods = (IBindable<IReadOnlyList<Mod>>)modsGetter.Invoke(__instance, null)!;
        var config = (OsuConfigManager)configGetter.Invoke(__instance, null)!;

        Attach(slider, ruleset, mods,
            config.GetBindable<double>(OsuSetting.DisplayStarsMinimum),
            config.GetBindable<double>(OsuSetting.DisplayStarsMaximum));
    }

    private static void completeFilterControl(FilterControl __instance)
    {
        var slider = getSlider(__instance);
        if (slider != null && states.TryGetValue(slider, out var state))
            state.CompleteUiAttachment();
    }

    private static void detachFilterControl(FilterControl __instance)
    {
        var slider = getSlider(__instance);
        if (slider == null || !states.TryGetValue(slider, out var state))
            return;

        state.Dispose();
        states.Remove(slider);
    }

    private static FilterControl.DifficultyRangeSlider? getSlider(FilterControl control) =>
        difficultyRangeSliderField.GetValue(control) as FilterControl.DifficultyRangeSlider;

    internal static IDisposable Attach(FilterControl.DifficultyRangeSlider slider, IBindable<RulesetInfo> ruleset,
                                       IBindable<IReadOnlyList<Mod>> mods, Bindable<double> starLower, Bindable<double> starUpper)
    {
        if (states.TryGetValue(slider, out var existing))
            return existing;

        var state = new FilterState(slider, ruleset, mods, starLower, starUpper);
        states.Add(slider, state);
        return state;
    }

    private static void adaptFilterCriteria(FilterControl __instance, FilterCriteria __result)
    {
        var slider = getSlider(__instance);
        if (slider != null && states.TryGetValue(slider, out var state))
            ApplyLevelRange(__result, state.LevelRange);
    }

    internal static void ApplyLevelRange(FilterCriteria criteria, FilterCriteria.OptionalRange<double> levelRange)
    {
        if (criteria.Ruleset?.ShortName != O2LazerIdentity.ShortName
            || O2JamGameplayProfile.UsesManiaScore(criteria.Mods)
            || criteria.RulesetCriteria is not O2JamFilterCriteria o2JamCriteria)
            return;

        // Native matching applies UserStarDifficulty before consulting a ruleset criterion.
        // Transfer the same UI range to the O2Jam criterion so level filtering never mutates
        // or depends on the stored mania star rating.
        criteria.UserStarDifficulty = new FilterCriteria.OptionalRange<double>
        {
            IsLowerInclusive = true,
            IsUpperInclusive = true,
        };
        o2JamCriteria.AddLevelFilter(levelRange);
    }

    private static void adaptLevelColour(object __instance, ref double value)
    {
        if (boundSliderStates.TryGetValue(__instance, out var state) && state.UseLevel)
            value /= 10;
    }

    private static void formatLevelNub(object __instance)
    {
        if (!boundSliderStates.TryGetValue(__instance, out var state) || !state.UseLevel)
            return;

        var slider = (SliderBar<double>)__instance;
        if (ReferenceEquals(__instance, state.UpperSlider) && slider.Current.IsDefault)
            return;

        var nubText = (OsuSpriteText)nubTextGetter.Invoke(__instance, null)!;
        nubText.Text = FormatLevelNub(slider.Current.Value);
    }

    private static void formatLevelTooltip(object __instance, ref LocalisableString __result)
    {
        if (!boundSliderStates.TryGetValue(__instance, out var state) || !state.UseLevel)
            return;

        var slider = (SliderBar<double>)__instance;
        __result = ReferenceEquals(__instance, state.UpperSlider) && slider.Current.IsDefault
            ? osu.Game.Localisation.UserInterfaceStrings.NoLimit
            : FormatLevelTooltip(slider.Current.Value);
    }

    internal static LocalisableString FormatLevelTooltip(double value) =>
        O2LazerStrings.LevelBadge((int)Math.Round(value, MidpointRounding.AwayFromZero));

    internal static LocalisableString FormatLevelNub(double value) =>
        O2LazerStrings.LevelFilterValue((int)Math.Round(value, MidpointRounding.AwayFromZero));

    private static IEnumerable<T> descendantsOfType<T>(Drawable drawable)
        where T : Drawable
    {
        if (drawable is T match)
            yield return match;

        if (drawable is not CompositeDrawable composite)
            yield break;

        var children = (IReadOnlyList<Drawable>)internalChildrenGetter.Invoke(composite, null)!;
        foreach (var child in children)
        {
            foreach (var descendant in descendantsOfType<T>(child))
                yield return descendant;
        }
    }

    private sealed class FilterState : IDisposable
    {
        private readonly FilterControl.DifficultyRangeSlider slider;
        private readonly IBindable<RulesetInfo> ruleset;
        private readonly IBindable<IReadOnlyList<Mod>> mods;
        private readonly Bindable<double> starLower;
        private readonly Bindable<double> starUpper;
        private readonly BindableDouble levelLower = new(0)
        {
            MinValue = 0,
            MaxValue = maximum_selectable_level,
            Precision = 1,
        };
        private readonly BindableDouble levelUpper = new(unlimited_level)
        {
            MinValue = 0,
            MaxValue = unlimited_level,
            Precision = 1,
        };

        private readonly Action<ValueChangedEvent<RulesetInfo>> rulesetChanged;
        private readonly Action<ValueChangedEvent<IReadOnlyList<Mod>>> modsChanged;
        private OsuSpriteText? label;
        private LocalisableString nativeLabel;
        private bool disposed;

        public bool UseLevel { get; private set; }
        public object? LowerSlider { get; private set; }
        public object? UpperSlider { get; private set; }

        public FilterCriteria.OptionalRange<double> LevelRange
        {
            get
            {
                var range = new FilterCriteria.OptionalRange<double>
                {
                    IsLowerInclusive = true,
                    IsUpperInclusive = true,
                };

                if (!levelLower.IsDefault)
                    range.Min = levelLower.Value;
                if (!levelUpper.IsDefault)
                    range.Max = levelUpper.Value;
                return range;
            }
        }

        public FilterState(FilterControl.DifficultyRangeSlider slider, IBindable<RulesetInfo> ruleset,
                           IBindable<IReadOnlyList<Mod>> mods, Bindable<double> starLower, Bindable<double> starUpper)
        {
            this.slider = slider;
            this.ruleset = ruleset;
            this.mods = mods;
            this.starLower = starLower;
            this.starUpper = starUpper;
            rulesetChanged = _ => updateMode();
            modsChanged = _ => updateMode();

            ruleset.BindValueChanged(rulesetChanged);
            mods.BindValueChanged(modsChanged);
            updateMode(force: true);
        }

        public void CompleteUiAttachment()
        {
            if (LowerSlider != null && UpperSlider != null)
            {
                updateMode(force: true);
                return;
            }

            LowerSlider = lowerBoundSliderGetter.Invoke(slider, null)!;
            UpperSlider = upperBoundSliderGetter.Invoke(slider, null)!;
            var sliderContainer = (Container)sliderContainerGetter.Invoke(slider, null)!;
            var nubTexts = descendantsOfType<OsuSpriteText>(sliderContainer).ToHashSet();
            label = descendantsOfType<OsuSpriteText>(slider).FirstOrDefault(text => !nubTexts.Contains(text));
            if (label != null)
                nativeLabel = label.Text;

            boundSliderStates.Remove(LowerSlider);
            boundSliderStates.Remove(UpperSlider);
            boundSliderStates.Add(LowerSlider, this);
            boundSliderStates.Add(UpperSlider, this);
            updateMode(force: true);
        }

        private void updateMode(bool force = false)
        {
            if (disposed)
                return;

            var useLevel = ruleset.Value?.ShortName == O2LazerIdentity.ShortName
                           && !O2JamGameplayProfile.UsesManiaScore(mods.Value);
            if (!force && UseLevel == useLevel)
                return;

            UseLevel = useLevel;
            slider.MinRange = useLevel ? 1 : 0.1f;
            rebindRanges(useLevel);

            if (LowerSlider == null || UpperSlider == null)
                return;

            ((SliderBar<double>)LowerSlider).KeyboardStep = useLevel ? 1 : 0.1f;
            ((SliderBar<double>)UpperSlider).KeyboardStep = useLevel ? 1 : 0.1f;
            if (label != null)
                label.Text = useLevel ? O2LazerStrings.Level : nativeLabel;

            updateDisplayMethod.Invoke(LowerSlider, [((SliderBar<double>)LowerSlider).Current.Value]);
            updateDisplayMethod.Invoke(UpperSlider, [((SliderBar<double>)UpperSlider).Current.Value]);
        }

        private void rebindRanges(bool useLevel)
        {
            var levelValues = (Lower: levelLower.Value, Upper: levelUpper.Value);
            var starValues = (Lower: starLower.Value, Upper: starUpper.Value);

            slider.LowerBound = useLevel ? levelLower : starLower;
            slider.UpperBound = useLevel ? levelUpper : starUpper;

            // The native sliders enforce their separation after each individual bind. Preserve both
            // ranges across the brief mixed-range state because changing the two bindings is not atomic.
            if (useLevel)
            {
                restoreRange(starLower, starUpper, starValues);
                restoreRange(levelLower, levelUpper, levelValues);
            }
            else
            {
                restoreRange(levelLower, levelUpper, levelValues);
                restoreRange(starLower, starUpper, starValues);
            }
        }

        private static void restoreRange(Bindable<double> lower, Bindable<double> upper, (double Lower, double Upper) values)
        {
            // Restoring the upper value first gives the lower value room to return without pushing it.
            upper.Value = values.Upper;
            lower.Value = values.Lower;
        }

        public void Dispose()
        {
            if (disposed)
                return;

            disposed = true;
            ruleset.ValueChanged -= rulesetChanged;
            mods.ValueChanged -= modsChanged;
            if (LowerSlider != null)
                boundSliderStates.Remove(LowerSlider);
            if (UpperSlider != null)
                boundSliderStates.Remove(UpperSlider);
            states.Remove(slider);
        }
    }
}
