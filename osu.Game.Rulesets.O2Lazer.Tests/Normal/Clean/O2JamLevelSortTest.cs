using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Game.Beatmaps;
using osu.Game.Graphics.Carousel;
using osu.Game.Graphics.UserInterfaceV2;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamLevelSortTest
{
    [Test]
    public void OptionExistsOnlyWhileO2LazerIsSelected()
    {
        var o2lazer = new O2LazerRuleset().RulesetInfo;
        var mania = new ManiaRuleset().RulesetInfo;
        Assert.That(O2JamLevelSortPatch.IsInstalled, Is.True);
        using var dropdown = new ShearedDropdown<SortMode>(string.Empty)
        {
            Items = Enum.GetValues<SortMode>(),
        };
        var ruleset = new Bindable<RulesetInfo>(o2lazer);
        O2JamLevelSortPatch.Attach(dropdown, ruleset);
        var expectedItems = Enum.GetValues<SortMode>().ToList();
        expectedItems.Insert(expectedItems.IndexOf(SortMode.Difficulty) + 1, O2JamLevelSortPatch.Level);

        Assert.Multiple(() =>
        {
            Assert.That(dropdown.Items, Is.EqualTo(expectedItems));
            Assert.That(getItemText(dropdown, O2JamLevelSortPatch.Level),
                Is.EqualTo(O2LazerStrings.Level.ToString()));
        });

        dropdown.Current.Value = O2JamLevelSortPatch.Level;
        ruleset.Value = mania;
        Assert.Multiple(() =>
        {
            Assert.That(dropdown.Items, Is.EqualTo(Enum.GetValues<SortMode>()));
            Assert.That(dropdown.Current.Value, Is.EqualTo(SortMode.Difficulty));
            Assert.That(getItemText(dropdown, O2JamLevelSortPatch.Level), Is.Null);
        });

        dropdown.Current.Value = O2JamLevelSortPatch.Level;
        Assert.That(dropdown.Current.Value, Is.EqualTo(SortMode.Difficulty), "A persisted custom value cannot leak into another ruleset.");
        ruleset.Value = o2lazer;
        Assert.That(dropdown.Items, Is.EqualTo(expectedItems));
    }

    [Test]
    public async Task SortsIndividualDifficultiesByNativeO2JamLevelThenTitle()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        var beatmaps = new[]
        {
            createBeatmap(ruleset, 150, "Very hard", 1),
            createBeatmap(ruleset, 2, "Zulu", 100),
            createBeatmap(ruleset, 50, "Middle", 3),
            createBeatmap(ruleset, 2, "Alpha", 99),
        };
        var criteria = new FilterCriteria { Ruleset = ruleset, Sort = O2JamLevelSortPatch.Level };
        var sorter = new BeatmapCarouselFilterSorting(() => criteria);

        var result = await sorter.Run(beatmaps.Select(beatmap => new CarouselItem(beatmap)), CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(result.Select(item => ((BeatmapInfo)item.Model).DifficultyName),
                Is.EqualTo(new[] { "EX 2", "EX 2", "EX 50", "EX 150" }));
            Assert.That(result.Take(2).Select(item => ((BeatmapInfo)item.Model).Metadata.Title), Is.EqualTo(new[] { "Alpha", "Zulu" }));
            Assert.That(sorter.BeatmapItemsCount, Is.EqualTo(beatmaps.Length));
            Assert.That(BeatmapCarouselFilterGrouping.ShouldGroupBeatmapsTogether(criteria), Is.False,
                "Level ordering must expose each O2Jam difficulty instead of aggregating its set.");
            Assert.That(criteria.RequiresSorting(new FilterCriteria { Ruleset = ruleset, Sort = O2JamLevelSortPatch.Level }), Is.True);
        });
    }

    [Test]
    public async Task NativeDifficultySortIsNotReplaced()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        var lowerLevelHigherStars = createBeatmap(ruleset, 2, "Lower level", 8);
        var higherLevelLowerStars = createBeatmap(ruleset, 100, "Higher level", 1);
        var criteria = new FilterCriteria { Ruleset = ruleset, Sort = SortMode.Difficulty };
        var sorter = new BeatmapCarouselFilterSorting(() => criteria);

        var result = await sorter.Run([new CarouselItem(lowerLevelHigherStars), new CarouselItem(higherLevelLowerStars)], CancellationToken.None);
        Assert.That(result.Select(item => ((BeatmapInfo)item.Model).Metadata.Title),
            Is.EqualTo(new[] { "Higher level", "Lower level" }), "Native difficulty keeps sorting by stored mania stars.");
    }

    private static BeatmapInfo createBeatmap(RulesetInfo ruleset, ushort level, string title, double stars)
    {
        var beatmap = new BeatmapInfo(ruleset)
        {
            DifficultyName = $"EX {level}",
            StarRating = stars,
            Metadata = new BeatmapMetadata { Title = title },
        };
        var set = new BeatmapSetInfo([beatmap])
        {
            DateAdded = DateTimeOffset.FromUnixTimeSeconds(level),
        };
        beatmap.BeatmapSet = set;
        return beatmap;
    }

    private static string? getItemText(ShearedDropdown<SortMode> dropdown, SortMode mode)
    {
        var field = typeof(osu.Framework.Graphics.UserInterface.Dropdown<SortMode>)
            .GetField("itemMap", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var items = (Dictionary<SortMode, osu.Framework.Graphics.UserInterface.DropdownMenuItem<SortMode>>)field.GetValue(dropdown)!;
        return items.TryGetValue(mode, out var item) ? item.Text.Value.ToString() : null;
    }
}
