using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using HarmonyLib;
using osu.Framework.Bindables;
using osu.Framework.Localisation;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.SongSelect;

internal static class O2JamLevelGroupPatch
{
    private const string harmony_id = "osu.Game.Rulesets.O2Lazer.LevelGroup";
    private const int level_group_size = 10;
    private const int maximum_level_group = 150;
    internal const GroupMode Level = (GroupMode)int.MaxValue;

    private static readonly object installLock = new();
    private static Type? groupDropdownType;
    private static Type? groupDropdownItemType;
    private static MethodInfo? groupDropdownItemsGetter;
    private static MethodInfo? groupDropdownItemsSetter;
    private static MethodInfo? groupDropdownRulesetGetter;
    private static PropertyInfo? groupDropdownItemValueProperty;
    private static ConstructorInfo? groupDropdownItemConstructor;
    private static MethodInfo? getGroupsByMethod;

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
                groupDropdownType = AccessTools.Inner(typeof(FilterControl), "GroupModeDropdown");
                groupDropdownItemType = AccessTools.Inner(typeof(FilterControl), "GroupModeDropdownItem");
                var updateAvailableItems = AccessTools.Method(groupDropdownType, "updateAvailableItems");
                var getGroups = AccessTools.Method(typeof(BeatmapCarouselFilterGrouping), "getGroups");
                var shouldGroupTogether = AccessTools.Method(typeof(BeatmapCarouselFilterGrouping), nameof(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether));
                groupDropdownItemsGetter = AccessTools.PropertyGetter(groupDropdownType, "Items");
                groupDropdownItemsSetter = AccessTools.PropertySetter(groupDropdownType, "Items");
                groupDropdownRulesetGetter = AccessTools.PropertyGetter(groupDropdownType, "ruleset");
                groupDropdownItemValueProperty = AccessTools.Property(groupDropdownItemType, "Value");
                groupDropdownItemConstructor = AccessTools.Constructor(groupDropdownItemType, [typeof(GroupMode), typeof(LocalisableString)]);
                getGroupsByMethod = AccessTools.Method(typeof(BeatmapCarouselFilterGrouping), "getGroupsBy");

                if (groupDropdownType == null || groupDropdownItemType == null || updateAvailableItems == null
                    || getGroups == null || shouldGroupTogether == null || groupDropdownItemsGetter == null
                    || groupDropdownItemsSetter == null || groupDropdownRulesetGetter == null
                    || groupDropdownItemValueProperty == null || groupDropdownItemConstructor == null || getGroupsByMethod == null)
                    throw new MissingMemberException("The native song-select grouping API has changed.");

                harmony.Patch(updateAvailableItems, postfix: new HarmonyMethod(method(nameof(updateGroupOptions))));
                harmony.Patch(getGroups, prefix: new HarmonyMethod(method(nameof(getLevelGroups))));
                harmony.Patch(shouldGroupTogether, postfix: new HarmonyMethod(method(nameof(splitLevelDifficulties))));

                IsInstalled = true;
                return true;
            }
            catch (Exception exception)
            {
                harmony.UnpatchAll(harmony_id);
                Logger.Error(exception, "O2Lazer could not install its level grouping adapter.");
                return false;
            }
        }
    }

    private static MethodInfo method(string name) => AccessTools.Method(typeof(O2JamLevelGroupPatch), name);

    private static void updateGroupOptions(object __instance)
    {
        var ruleset = (IBindable<RulesetInfo>)groupDropdownRulesetGetter!.Invoke(__instance, null)!;
        UpdateOptions(__instance, ruleset.Value);
    }

    internal static void UpdateOptions(object dropdown, RulesetInfo? ruleset)
    {
        var currentItems = ((IEnumerable)groupDropdownItemsGetter!.Invoke(dropdown, null)!).Cast<object>().ToList();
        var levelItems = currentItems.Count(item => getGroupMode(item) == Level);
        var shouldIncludeLevel = ruleset?.ShortName == O2LazerIdentity.ShortName;

        // Leaving an already-native list untouched prevents this adapter from causing a second
        // dropdown refresh whenever another ruleset becomes active.
        if ((shouldIncludeLevel && levelItems == 1) || (!shouldIncludeLevel && levelItems == 0))
            return;

        var items = currentItems.Where(item => getGroupMode(item) != Level).ToList();

        if (shouldIncludeLevel)
        {
            var difficultyIndex = items.FindIndex(item => getGroupMode(item) == GroupMode.Difficulty);
            items.Insert(difficultyIndex >= 0 ? difficultyIndex + 1 : items.Count,
                groupDropdownItemConstructor!.Invoke([Level, O2LazerStrings.Level]));
        }

        var itemArray = Array.CreateInstance(groupDropdownItemType!, items.Count);
        for (var i = 0; i < items.Count; i++)
            itemArray.SetValue(items[i], i);

        groupDropdownItemsSetter!.Invoke(dropdown, [itemArray]);
    }

    private static GroupMode getGroupMode(object item) => (GroupMode)groupDropdownItemValueProperty!.GetValue(item)!;

    private static bool getLevelGroups(BeatmapCarouselFilterGrouping __instance, List<CarouselItem> items,
                                       FilterCriteria criteria, ref object __result)
    {
        if (criteria.Group != Level)
            return true;

        IEnumerable<GroupDefinition> defineGroup(BeatmapInfo beatmap)
        {
            var level = O2JamStarRatingMetadata.ResolveLevel(beatmap);

            if (level >= maximum_level_group)
            {
                return
                [
                    new StarDifficultyGroupDefinition(maximum_level_group, O2LazerStrings.O2JamLevelGroupOver(maximum_level_group),
                        new StarDifficulty(maximum_level_group / level_group_size, 0)),
                ];
            }

            var lowerBound = level / level_group_size * level_group_size;
            return
            [
                new StarDifficultyGroupDefinition(lowerBound, O2LazerStrings.O2JamLevelGroupRange(lowerBound, lowerBound + level_group_size),
                    new StarDifficulty(lowerBound / level_group_size, 0)),
            ];
        }

        __result = getGroupsByMethod!.Invoke(__instance,
            [new Func<BeatmapInfo, IEnumerable<GroupDefinition>>(defineGroup), items])!;
        return false;
    }

    private static void splitLevelDifficulties(FilterCriteria criteria, ref bool __result)
    {
        if (criteria.Group == Level)
            __result = false;
    }
}
