using System.Reflection;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Screens.Select;
using osu.Game.Screens.Select.Filter;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamFilterCriteriaTest
{
    private static readonly MethodInfo applyQueries = typeof(FilterQueryParser).GetMethod(
        "ApplyQueries", BindingFlags.Static | BindingFlags.NonPublic)!;

    [TestCase("level>75", "NX 76", true)]
    [TestCase("level>75", "NX 75", false)]
    [TestCase("lv>=75", "NX 75", true)]
    [TestCase("lv<75", "EX Lv.74", true)]
    [TestCase("lv<75", "NX 75", false)]
    [TestCase("level<=75", "NX 75", true)]
    [TestCase("level=75", "NX 等级 75", true)]
    [TestCase("level=75", "NX 76", false)]
    [TestCase("lv!=75", "NX 75", false)]
    [TestCase("lv!=75", "NX 76", true)]
    [TestCase("LEVEL>150", "HX 151", true)]
    [TestCase("lV=200", "HX 200", true)]
    [TestCase("Level>74.5", "NX 75", true)]
    [TestCase("lv=75.0001", "NX 75", false)]
    [TestCase("lv:75", "NX 75", true)]
    [TestCase("level!:75", "NX 75", false)]
    [TestCase("level>:75", "NX 75", true)]
    [TestCase("lv<:75", "NX 75", true)]
    [TestCase("lv=0", "EX 0", true)]
    [TestCase("lv>-1", "EX 0", true)]
    [TestCase("level<70000", "HX 65535", true)]
    public void FiltersByNativeLevelWithOsuComparisonOperators(string query, string difficultyName, bool expected)
    {
        var beatmap = createBeatmap();
        beatmap.DifficultyName = difficultyName;
        beatmap.StarRating = 1;
        var criteria = parse(query);

        Assert.Multiple(() =>
        {
            Assert.That(criteria.SearchTerms, Is.Empty);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, criteria), Is.EqualTo(expected));
        });
    }

    [TestCase("level>=10 LV<20", true)]
    [TestCase("LV>=10 level<15", false)]
    [TestCase("level>20 lv>10", false)]
    [TestCase("lv>10 level>20", false)]
    [TestCase("level>=15 lv>15", false)]
    [TestCase("level!=15 lv>10", false)]
    [TestCase("level>10 lv!=15", false)]
    [TestCase("level!=10 lv!=20", true)]
    [TestCase("o2ma100 LEVEL=15 ln>50 note<50 piano artist=Artist", true)]
    [TestCase("o2ma100 level=16 ln>50", false)]
    [TestCase("o2ma1000 level=15", false)]
    [TestCase("o2ma100 lv=15 ln>70", false)]
    public void IntersectsLevelAliasesAndOtherSearchConditions(string query, bool expected) =>
        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(), parse(query)), Is.EqualTo(expected));

    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("1e999")]
    [TestCase("50%")]
    [TestCase("50,5")]
    [TestCase("invalid")]
    public void InvalidLevelsRemainOrdinarySearchText(string value)
    {
        var query = $"lv>{value}";
        Assert.That(parse(query).SearchText, Is.EqualTo(query));
    }

    [Test]
    public void LevelSearchUsesRatingFallbackWithoutRequiringObjectCounts()
    {
        var beatmap = createBeatmap(0, 0);
        beatmap.DifficultyName = "HX";
        beatmap.StarRating = 11.9;

        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("level=119")), Is.True);
    }

    [Test]
    public void ClearingSearchDoesNotRetainThePreviousLevelRange()
    {
        var beatmap = createBeatmap();

        Assert.Multiple(() =>
        {
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("level>100")), Is.False);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse(string.Empty)), Is.True);
        });
    }

    [TestCase("ln>50", 100, 60, true)]
    [TestCase("ln>50", 100, 50, false)]
    [TestCase("ln>50", 100, 49, false)]
    [TestCase("ln>=50", 100, 50, true)]
    [TestCase("ln<50", 100, 49, true)]
    [TestCase("ln<50", 100, 50, false)]
    [TestCase("ln<=50", 100, 50, true)]
    [TestCase("ln=50", 100, 50, true)]
    [TestCase("ln=50", 100, 51, false)]
    [TestCase("ln!=50", 100, 50, false)]
    [TestCase("ln!=50", 100, 51, true)]
    [TestCase("ln=0", 100, 0, true)]
    [TestCase("ln=100", 100, 100, true)]
    [TestCase("ln>50", 10000, 5001, true)]
    [TestCase("ln=50.5", 200, 101, true)]
    [TestCase("LN>=50%", 100, 50, true)]
    [TestCase("note>50", 100, 40, true)]
    [TestCase("note>50", 100, 60, false)]
    [TestCase("note=40", 100, 60, true)]
    public void FiltersEachDifficultyByObjectPercentage(string query, int total, int holds, bool expected)
    {
        var criteria = parse(query);

        Assert.Multiple(() =>
        {
            Assert.That(criteria.SearchTerms, Is.Empty, "The native parser must consume valid custom clauses.");
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(total, holds), criteria), Is.EqualTo(expected));
        });
    }

    [TestCase("ln>20 ln<70", 60, true)]
    [TestCase("ln>20 ln<70", 70, false)]
    [TestCase("ln!=50 ln>20", 50, false)]
    [TestCase("ln>20 ln!=50", 50, false)]
    [TestCase("ln>70 ln>20", 60, false)]
    [TestCase("ln>=50 ln>50", 50, false)]
    [TestCase("ln>50 note>30", 60, true)]
    [TestCase("ln>50 note>30", 80, false)]
    public void IntersectsAllPercentageClauses(string query, int holds, bool expected) =>
        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(100, holds), parse(query)), Is.EqualTo(expected));

    [TestCase(0, 0)]
    [TestCase(-1, -1)]
    [TestCase(100, -1)]
    [TestCase(100, 101)]
    public void UndefinedPercentagesDoNotMatch(int total, int holds) =>
        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(total, holds), parse("ln>=0")), Is.False);

    [TestCase("NaN")]
    [TestCase("Infinity")]
    [TestCase("-1")]
    [TestCase("101")]
    [TestCase("50%%")]
    [TestCase("50,5")]
    [TestCase("invalid")]
    public void InvalidPercentagesRemainOrdinarySearchText(string value)
    {
        var query = $"ln>{value}";
        Assert.That(parse(query).SearchText, Is.EqualTo(query));
        Assert.That(new O2JamFilterCriteria().TryParseCustomKeywordCriteria("other", Operator.Greater, "50"), Is.False);
    }

    [TestCase("o2ma100", "o2ma100", true)]
    [TestCase("O2MA100", "o2ma100", true)]
    [TestCase("o2ma100", "O2MA100", true)]
    [TestCase("o2ma100", "o2ma1000", false)]
    [TestCase("o2ma100", "o2ma1001", false)]
    [TestCase("o2ma100", "xo2ma100", false)]
    [TestCase("o2ma100", "o2ma100-extra", false)]
    [TestCase("o2ma100", "\to2ma100\n", true)]
    [TestCase("\"o2ma100\"", "o2ma100", true)]
    [TestCase("100", "o2ma100", false)]
    [TestCase("100", "o2ma1000", false)]
    [TestCase("100", "o2ma1001", false)]
    [TestCase("\"100\"", "o2ma100", false)]
    public void SongIdentifiersMustMatchTheWholeTag(string query, string tag, bool expected)
    {
        var beatmap = createBeatmap();
        beatmap.Metadata.Tags = $"o2jam {tag} o2lazer-clean:2 o2lazer-encoding:2 o2lazer-source-size:54100";

        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse(query)), Is.EqualTo(expected));
    }

    [Test]
    public void ExactIdentifierCannotBeSatisfiedByAnUnrelatedTitle()
    {
        var beatmap = createBeatmap();
        beatmap.Metadata.Tags = "o2jam o2ma1000";
        beatmap.Metadata.Title = "o2ma100 remix";

        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("o2ma100")), Is.False);
    }

    [Test]
    public void BareNumberDoesNotLeakFromFallbackTitlesPathsOrInternalTags()
    {
        var beatmap = createBeatmap();
        beatmap.Metadata.Title = "o2ma100";
        beatmap.Metadata.Source = @"E:\o2jam\o2ma1000";

        Assert.Multiple(() =>
        {
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("100")), Is.False);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("54100")), Is.False);
        });
    }

    [Test]
    public void BareNumberDoesNotMatchInternalVersionMarkers()
    {
        var beatmap = createBeatmap();
        // Keep the digit out of public metadata such as the literal "o2jam" tag and folder.
        beatmap.Metadata.Source = string.Empty;
        beatmap.Metadata.Tags = "o2ma100 o2lazer-clean:2 o2lazer-encoding:2";

        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("2")), Is.False);
    }

    [Test]
    public void ClearingSearchDoesNotRetainThePreviousPercentageRange()
    {
        var beatmap = createBeatmap();

        Assert.Multiple(() =>
        {
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("ln>70")), Is.False);
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse(string.Empty)), Is.True);
        });
    }

    [TestCase("title")]
    [TestCase("artist")]
    [TestCase("author")]
    [TestCase("difficulty")]
    [TestCase("tag")]
    public void BareNumbersStillMatchOrdinaryMetadata(string field)
    {
        var beatmap = createBeatmap();
        switch (field)
        {
            case "title":
                beatmap.Metadata.Title = "o2ma1000 - 100 percent";
                break;
            case "artist":
                beatmap.Metadata.Artist = "Artist 100";
                break;
            case "author":
                beatmap.Metadata.Author.Username = "Mapper 100";
                break;
            case "difficulty":
                beatmap.DifficultyName = "HX 100";
                break;
            case "tag":
                beatmap.Metadata.Tags += " 100bpm";
                break;
        }

        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, parse("100")), Is.True);
    }

    [TestCase("o2ma100 ln>50 piano", true)]
    [TestCase("o2ma100 ln>50 guitar", false)]
    [TestCase("o2ma100 ln>70 piano", false)]
    [TestCase("o2ma100 o2ma1000", false)]
    [TestCase("o2ma100 artist=Artist", true)]
    [TestCase("o2ma100 artist=Other", false)]
    public void CombinesIdentifiersWithTextAndNativeFilters(string query, bool expected) =>
        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(createBeatmap(), parse(query)), Is.EqualTo(expected));

    [Test]
    public void OtherRulesetsKeepTheirNativeSubstringSearch()
    {
        var beatmap = createBeatmap();
        beatmap.Ruleset = new RulesetInfo { ShortName = "mania", OnlineID = 3 };
        beatmap.Metadata.Tags = "o2ma1000";
        var criteria = new FilterCriteria { Ruleset = beatmap.Ruleset, SearchText = "100" };

        Assert.Multiple(() =>
        {
            Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, criteria), Is.True);
            Assert.That(new O2JamFilterCriteria().Matches(beatmap, criteria), Is.False);
        });
    }

    private static BeatmapInfo createBeatmap(int total = 100, int holds = 60) => new(
        new RulesetInfo { ShortName = O2LazerIdentity.ShortName, OnlineID = -1 },
        metadata: new BeatmapMetadata
        {
            Title = "Piano song",
            Artist = "Artist",
            Source = @"E:\o2jam\SongA",
            Tags = "o2jam o2ma100 o2lazer-clean:2 o2lazer-encoding:2 o2lazer-source-size:54100",
        })
    {
        TotalObjectCount = total,
        EndTimeObjectCount = holds,
        DifficultyName = "EX 15",
    };

    private static FilterCriteria parse(string query)
    {
        var ruleset = new O2LazerRuleset();
        var criteria = new FilterCriteria
        {
            Ruleset = ruleset.RulesetInfo,
            RulesetCriteria = ruleset.CreateRulesetFilterCriteria(),
        };

        // Exercise osu!'s actual query parser and carousel matcher, including their ordering.
        applyQueries.Invoke(null, [criteria, query]);
        return criteria;
    }
}
