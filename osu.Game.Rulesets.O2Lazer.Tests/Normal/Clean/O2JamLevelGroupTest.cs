using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Scoring;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamLevelGroupTest
{
    [Test]
    public void OptionExistsOnlyWhileO2LazerIsSelected()
    {
        var o2lazer = new O2LazerRuleset().RulesetInfo;
        var mania = new ManiaRuleset().RulesetInfo;
        Assert.That(O2JamLevelGroupPatch.IsInstalled, Is.True);

        var dropdownType = typeof(FilterControl).GetNestedType("GroupModeDropdown", BindingFlags.NonPublic)!;
        var itemType = typeof(FilterControl).GetNestedType("GroupModeDropdownItem", BindingFlags.NonPublic)!;
        var itemsProperty = dropdownType.GetProperty("Items")!;
        var valueProperty = itemType.GetProperty("Value")!;
        var textProperty = itemType.GetProperty("Text")!;
        var dropdown = Activator.CreateInstance(dropdownType, [O2LazerStrings.Level])!;

        try
        {
            var nativeModes = Enum.GetValues<GroupMode>();
            var nativeItems = Array.CreateInstance(itemType, nativeModes.Length);
            for (var i = 0; i < nativeModes.Length; i++)
                nativeItems.SetValue(Activator.CreateInstance(itemType, [nativeModes[i], (LocalisableString)nativeModes[i].ToString()]), i);
            itemsProperty.SetValue(dropdown, nativeItems);

            O2JamLevelGroupPatch.UpdateOptions(dropdown, o2lazer);
            var o2Options = ((IEnumerable)itemsProperty.GetValue(dropdown)!).Cast<object>().ToArray();
            var expectedModes = nativeModes.ToList();
            expectedModes.Insert(expectedModes.IndexOf(GroupMode.Difficulty) + 1, O2JamLevelGroupPatch.Level);
            Assert.Multiple(() =>
            {
                Assert.That(o2Options.Select(item => (GroupMode)valueProperty.GetValue(item)!),
                    Is.EqualTo(expectedModes));
                var levelOption = o2Options.Single(item => (GroupMode)valueProperty.GetValue(item)! == O2JamLevelGroupPatch.Level);
                Assert.That(((LocalisableString)textProperty.GetValue(levelOption)!).ToString(),
                    Is.EqualTo(O2LazerStrings.Level.ToString()));
            });

            O2JamLevelGroupPatch.UpdateOptions(dropdown, mania);
            var maniaOptions = ((IEnumerable)itemsProperty.GetValue(dropdown)!).Cast<object>().ToArray();
            Assert.That(maniaOptions.Select(item => (GroupMode)valueProperty.GetValue(item)!), Is.EqualTo(nativeModes));
        }
        finally
        {
            (dropdown as IDisposable)?.Dispose();
        }
    }

    [Test]
    public async Task GroupsIndividualDifficultiesIntoTenLevelRanges()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        var criteria = new FilterCriteria { Ruleset = ruleset, Group = O2JamLevelGroupPatch.Level, Sort = SortMode.Title };
        var grouping = new BeatmapCarouselFilterGrouping
        {
            GetCriteria = () => criteria,
            GetCollections = () => [],
            GetLocalUserTopRanks = _ => new Dictionary<Guid, ScoreRank>(),
            GetFavouriteBeatmapSets = () => [],
        };
        var beatmaps = new[]
        {
            createBeatmap(ruleset, 9, "Below first boundary"),
            createBeatmap(ruleset, 10, "First boundary"),
            createBeatmap(ruleset, 149, "Below maximum"),
            createBeatmap(ruleset, 150, "Maximum boundary"),
            createBeatmap(ruleset, 0, "Zero"),
            createBeatmap(ruleset, 19, "Below second boundary"),
            createBeatmap(ruleset, 20, "Second boundary"),
            createBeatmap(ruleset, 151, "Above maximum"),
        };

        var result = await grouping.Run(beatmaps.Select(beatmap => new CarouselItem(beatmap)).ToList(), CancellationToken.None);
        var groups = result.Select(item => item.Model).OfType<StarDifficultyGroupDefinition>().ToArray();
        var groupedBeatmaps = result.Select(item => item.Model).OfType<GroupedBeatmap>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(groups.Select(group => group.Order), Is.EqualTo(new[] { 0, 10, 20, 140, 150 }));
            Assert.That(groups.Select(group => group.Title.ToString()),
                Is.EqualTo(new[] { "Lv.0 - 10", "Lv.10 - 20", "Lv.20 - 30", "Lv.140 - 150", "Over Lv.150" }));
            Assert.That(groups.Select(group => group.Difficulty.Stars), Is.EqualTo(new[] { 0, 1, 2, 14, 15 }),
                "Each level range must reuse the colour of the corresponding native star group.");
            Assert.That(groupedBeatmaps, Has.Length.EqualTo(beatmaps.Length));
            Assert.That(grouping.BeatmapSetsGroupedTogether, Is.False);
            Assert.That(grouping.BeatmapItemsCount, Is.EqualTo(beatmaps.Length));
            Assert.That(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether(criteria), Is.False);
        });

        assertGroupContains(groups[0], "EX 0", "EX 9");
        assertGroupContains(groups[1], "EX 10", "EX 19");
        assertGroupContains(groups[2], "EX 20");
        assertGroupContains(groups[3], "EX 149");
        assertGroupContains(groups[4], "EX 150", "EX 151");

        void assertGroupContains(GroupDefinition group, params string[] difficultyNames)
        {
            Assert.That(grouping.GroupItems[group]
                                .Select(item => item.Model)
                                .OfType<GroupedBeatmap>()
                                .Select(grouped => grouped.Beatmap.DifficultyName),
                Is.EquivalentTo(difficultyNames));
        }
    }

    [Test]
    public async Task NativeDifficultyGroupingIsNotReplaced()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        var criteria = new FilterCriteria { Ruleset = ruleset, Group = GroupMode.Difficulty, Sort = SortMode.Title };
        var grouping = new BeatmapCarouselFilterGrouping
        {
            GetCriteria = () => criteria,
            GetCollections = () => [],
            GetLocalUserTopRanks = _ => new Dictionary<Guid, ScoreRank>(),
            GetFavouriteBeatmapSets = () => [],
        };
        var lowerLevelHigherStars = createBeatmap(ruleset, 2, "Higher stars", 8.2);
        var higherLevelLowerStars = createBeatmap(ruleset, 100, "Lower stars", 1.4);

        var result = await grouping.Run(
            new List<CarouselItem> { new CarouselItem(lowerLevelHigherStars), new CarouselItem(higherLevelLowerStars) }, CancellationToken.None);

        Assert.That(result.Select(item => item.Model).OfType<StarDifficultyGroupDefinition>().Select(group => group.Order),
            Is.EqualTo(new[] { 1, 8 }), "Native difficulty grouping keeps using stored mania stars.");
    }

    private static BeatmapInfo createBeatmap(RulesetInfo ruleset, ushort level, string title, double stars = 0)
    {
        var beatmap = new BeatmapInfo(ruleset)
        {
            DifficultyName = $"EX {level}",
            StarRating = stars,
            Metadata = new BeatmapMetadata { Title = title },
        };
        beatmap.BeatmapSet = new BeatmapSetInfo([beatmap]);
        return beatmap;
    }
}
