using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Graphics.UserInterface;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamLevelSortPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.LevelSort";
    internal const SortMode Level = (SortMode)int.MaxValue;

    private static readonly object installLock = new();
    private static FieldInfo? sortDropdownField;
    private static MethodInfo? rulesetGetter;
    private static MethodInfo? beatmapItemsCountSetter;
    private static FieldInfo? dropdownItemMapField;
    private static FieldInfo? sortingCriteriaField;

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
                var sortingRun = AccessTools.Method(typeof(BeatmapCarouselFilterSorting), nameof(BeatmapCarouselFilterSorting.Run));
                var grouping = AccessTools.Method(typeof(BeatmapCarouselFilterGrouping), nameof(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether));
                var requiresSorting = AccessTools.Method(typeof(FilterCriteria), nameof(FilterCriteria.RequiresSorting));
                sortDropdownField = AccessTools.Field(typeof(FilterControl), "sortDropdown");
                rulesetGetter = AccessTools.PropertyGetter(typeof(FilterControl), "ruleset");
                beatmapItemsCountSetter = AccessTools.PropertySetter(typeof(BeatmapCarouselFilterSorting), nameof(BeatmapCarouselFilterSorting.BeatmapItemsCount));
                dropdownItemMapField = AccessTools.Field(typeof(Dropdown<SortMode>), "itemMap");
                sortingCriteriaField = AccessTools.Field(typeof(BeatmapCarouselFilterSorting), "getCriteria");

                if (filterLoad == null || sortingRun == null || grouping == null || requiresSorting == null
                    || sortDropdownField == null || rulesetGetter == null || beatmapItemsCountSetter == null
                    || dropdownItemMapField == null || sortingCriteriaField == null)
                    throw new MissingMemberException("The native song-select sorting API has changed.");

                harmony.Patch(filterLoad, postfix: new HarmonyMethod(method(nameof(attachFilterControl))));
                harmony.Patch(sortingRun, prefix: new HarmonyMethod(method(nameof(runLevelSort))));
                harmony.Patch(grouping, postfix: new HarmonyMethod(method(nameof(splitLevelDifficulties))));
                harmony.Patch(requiresSorting, prefix: new HarmonyMethod(method(nameof(requireLevelSorting))));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its level sorting adapter.");
                return false;
            }
        }
    }

    private static MethodInfo method(string name) => AccessTools.Method(typeof(O2JamLevelSortPatch), name);

    private static void attachFilterControl(FilterControl __instance)
    {
        var dropdown = (ShearedDropdown<SortMode>)sortDropdownField!.GetValue(__instance)!;
        var ruleset = (IBindable<RulesetInfo>)rulesetGetter!.Invoke(__instance, null)!;
        Attach(dropdown, ruleset);
    }

    internal static void Attach(ShearedDropdown<SortMode> dropdown, IBindable<RulesetInfo> ruleset)
    {
        void updateOptions()
        {
            if (ruleset.Value?.ShortName == O2LazerIdentity.ShortName)
            {
                if (!dropdown.Items.Contains(Level))
                {
                    var items = dropdown.Items.Where(item => item != Level).ToList();
                    var difficultyIndex = items.IndexOf(SortMode.Difficulty);
                    items.Insert(difficultyIndex >= 0 ? difficultyIndex + 1 : items.Count, Level);
                    dropdown.Items = items;
                }
                applyLevelLabel(dropdown);
            }
            else if (dropdown.Items.Contains(Level))
            {
                if (dropdown.Current.Value == Level)
                    dropdown.Current.Value = SortMode.Difficulty;
                dropdown.RemoveDropdownItem(Level);
            }
        }

        ruleset.BindValueChanged(_ => updateOptions(), true);
        dropdown.Current.BindValueChanged(change =>
        {
            // A persisted custom value may be restored by config after the item list was
            // prepared. Never let another ruleset publish an unsupported filter criterion.
            if (change.NewValue == Level && ruleset.Value?.ShortName != O2LazerIdentity.ShortName)
                dropdown.Current.Value = SortMode.Difficulty;
        });
        dropdown.OnLoadComplete += _ => applyLevelLabel(dropdown);
    }

    private static void applyLevelLabel(ShearedDropdown<SortMode> dropdown)
    {
        var items = (Dictionary<SortMode, DropdownMenuItem<SortMode>>)dropdownItemMapField!.GetValue(dropdown)!;
        if (items.TryGetValue(Level, out var item))
            item.Text.Value = O2LazerStrings.Level;
    }

    private static bool runLevelSort(BeatmapCarouselFilterSorting __instance, IEnumerable<CarouselItem> items,
                                     CancellationToken cancellationToken, ref Task<List<CarouselItem>> __result)
    {
        var getCriteria = (Func<FilterCriteria>)sortingCriteriaField!.GetValue(__instance)!;
        var criteria = getCriteria();
        if (criteria.Sort != Level)
            return true;

        __result = Task.Run(() =>
        {
            cancellationToken.ThrowIfCancellationRequested();
            var materialised = items.ToList();
            beatmapItemsCountSetter!.Invoke(__instance, [materialised.Count]);
            var compareByLevel = criteria.Ruleset?.ShortName == O2LazerIdentity.ShortName;
            var sorted = materialised.Order(Comparer<CarouselItem>.Create((left, right) =>
                compare((BeatmapInfo)left.Model, (BeatmapInfo)right.Model, compareByLevel))).ToList();
            cancellationToken.ThrowIfCancellationRequested();
            return sorted;
        }, cancellationToken);
        return false;
    }

    private static int compare(BeatmapInfo left, BeatmapInfo right, bool compareByLevel)
    {
        var comparison = compareByLevel
            ? O2JamStarRatingMetadata.ResolveLevel(left).CompareTo(O2JamStarRatingMetadata.ResolveLevel(right))
            : left.StarRating.CompareTo(right.StarRating);
        if (comparison == 0)
            comparison = OrdinalSortByCaseStringComparer.DEFAULT.Compare(left.BeatmapSet!.Metadata.Title, right.BeatmapSet!.Metadata.Title);
        if (comparison == 0)
            comparison = right.BeatmapSet!.DateAdded.CompareTo(left.BeatmapSet!.DateAdded);
        if (comparison == 0)
            comparison = right.BeatmapSet!.ID.CompareTo(left.BeatmapSet!.ID);
        return comparison;
    }

    private static void splitLevelDifficulties(FilterCriteria criteria, ref bool __result)
    {
        if (criteria.Sort == Level)
            __result = false;
    }

    private static bool requireLevelSorting(FilterCriteria __instance, FilterCriteria newCriteria, ref bool __result)
    {
        if (__instance.Sort != Level && newCriteria.Sort != Level)
            return true;
        __result = true;
        return false;
    }
}
