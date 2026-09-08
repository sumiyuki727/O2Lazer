using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using HarmonyLib;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Shapes;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Graphics.Sprites;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded;
using osu.Game.Screens.Select;
using osu.Game.Utils;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamStarRatingDisplayTest
{
    [TestCase(true, false, false, 3.25)]
    [TestCase(true, false, true, 3.25)]
    [TestCase(true, true, false, 3.25)]
    [TestCase(true, true, true, 3.25)]
    [TestCase(false, false, false, 3.25)]
    [TestCase(false, false, true, 3.25)]
    public void NativeResultsUseManiaStarsRegardlessOfMS(bool o2lazer, bool ms, bool inLibrary, double expected)
    {
        var previousContext = SynchronizationContext.Current;
        try
        {
            // No UI notifications are needed here. Avoid handing Realm's native scheduler
            // to NUnit's asynchronous context after the temporary database has been closed.
            SynchronizationContext.SetSynchronizationContext(null);
            checkNativeResults(o2lazer, ms, inLibrary, expected);
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previousContext);
        }
    }

    private static void checkNativeResults(bool o2lazer, bool ms, bool inLibrary, double expected)
    {
        var o2Ruleset = new O2LazerRuleset().RulesetInfo;
        Assert.That(O2JamStarRatingDisplayPatch.IsInstalled, Is.True);
        var ruleset = o2lazer ? o2Ruleset : new ManiaRuleset().RulesetInfo;
        var beatmap = createBeatmap(ruleset, 75, 3.25);
        using var storage = new TemporaryNativeStorage($"{nameof(O2JamStarRatingDisplayTest)}-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        if (inLibrary)
            realm.Write(database => database.Add(new BeatmapInfo { ID = beatmap.ID }));

        using var cache = new TestDifficultyCache(ruleset);
        cache.ChangeMods(ms ? [] : [new O2JamModManiaScore()]);
        var score = new ScoreInfo(ruleset: ruleset) { BeatmapInfo = beatmap, Mods = ms ? [new O2JamModManiaScore()] : [] };
        using var panel = new ExpandedPanelMiddleContent(score);
        typeof(ExpandedPanelMiddleContent).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, [realm, cache]);

        var display = panel.ChildrenOfType<StarRatingDisplay>().Single();
        var icon = panel.ChildrenOfType<DifficultyIcon>().Single();
        var expectedIconStars = o2lazer && !ms ? 7.5 : expected;
        var starIcon = (SpriteIcon)typeof(StarRatingDisplay).GetField("starIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
        var starsText = (OsuSpriteText)typeof(StarRatingDisplay).GetField("starsText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
        var background = (Box)typeof(StarRatingDisplay).GetField("background", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingPresentationPatch.IsInstalled, Is.True);
            Assert.That(display.Current.Value.Stars, Is.EqualTo(expected));
            Assert.That(icon.Current.Value.Stars, Is.EqualTo(expectedIconStars), "The ruleset icon must use the score's display-mode colour.");
            Assert.That(getTooltipStars(icon), Is.EqualTo(expectedIconStars), "The ruleset icon tooltip must receive the same colour-driving value.");

            if (o2lazer && !ms)
            {
                Assert.That(starIcon.Width, Is.Zero);
                Assert.That(starsText.Text.ToString(), Is.EqualTo(O2LazerStrings.LevelBadge(75).ToString()));
                Assert.That(background.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForStarDifficulty(7.5)));
            }
            else
            {
                Assert.That(starIcon.Width, Is.EqualTo(8));
                Assert.That(starsText.Text.ToString(), Is.EqualTo(expected.FormatStarRating().ToString()));
                Assert.That(background.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForStarDifficulty(expected)));
            }
        });
        Assert.That(beatmap.StarRating, Is.EqualTo(3.25), "The display must never overwrite native mania stars.");
        Assert.That(cache.NativeLookups, Is.EqualTo(inLibrary ? 1 : 0));
    }

    [Test]
    public void NativeResultsUseRecordedRateModDifficulty()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        var beatmap = createBeatmap(ruleset, 75, 3.25);
        using var storage = new TemporaryNativeStorage($"{nameof(O2JamStarRatingDisplayTest)}-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        realm.Write(database => database.Add(new BeatmapInfo { ID = beatmap.ID }));
        using var cache = new TestDifficultyCache(ruleset);
        var score = new ScoreInfo(ruleset: ruleset) { BeatmapInfo = beatmap, Mods = [new O2JamModDoubleTime()] };
        using var panel = new ExpandedPanelMiddleContent(score);

        typeof(ExpandedPanelMiddleContent).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, [realm, cache]);

        var display = panel.ChildrenOfType<StarRatingDisplay>().Single();
        var icon = panel.ChildrenOfType<DifficultyIcon>().Single();
        Assert.Multiple(() =>
        {
            Assert.That(display.Current.Value.Stars, Is.EqualTo(4.875));
            Assert.That(icon.Current.Value.Stars, Is.EqualTo(7.5));
            Assert.That(getTooltipStars(icon), Is.EqualTo(7.5));
            Assert.That(cache.NativeLookups, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task NativeDisplayBindingKeepsManiaStarsAcrossModChanges()
    {
        var ruleset = new O2LazerRuleset();
        Assert.That(O2JamStarRatingDisplayPatch.IsInstalled, Is.True);
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        using var cache = new TestDifficultyCache(ruleset.RulesetInfo);
        var binding = cache.GetBindableDifficulty(beatmap);

        assertDisplayed(cache, binding, 3.25);
        cache.ChangeMods([new O2JamModManiaScore()]);
        assertDisplayed(cache, binding, 3.25);
        cache.ChangeMods([]);
        assertDisplayed(cache, binding, 3.25);
        Assert.That(cache.NativeLookups, Is.Zero);

        var native = await cache.GetDifficultyAsync(beatmap, ruleset.RulesetInfo, []);
        Assert.That(native?.Stars, Is.EqualTo(3.25));
        Assert.That(beatmap.StarRating, Is.EqualTo(3.25));

        var refreshed = createBeatmap(ruleset.RulesetInfo, 99, 4.5);
        refreshed.ID = beatmap.ID;
        cache.Invalidate(beatmap, refreshed);
        assertDisplayed(cache, binding, 4.5);
        cache.ChangeMods([new O2JamModManiaScore()]);
        assertDisplayed(cache, binding, 4.5);
        Assert.That(cache.NativeLookups, Is.EqualTo(1));
    }

    [Test]
    public void NativeDisplayBindingUsesTheModdedDifficultyLookupForRateMods()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        using var cache = new TestDifficultyCache(ruleset.RulesetInfo);
        var binding = cache.GetBindableDifficulty(beatmap);

        assertDisplayed(cache, binding, 3.25);
        cache.ChangeMods([new O2JamModDoubleTime()]);
        assertDisplayed(cache, binding, 4.875);
        cache.ChangeMods([new O2JamModHalfTime()]);
        assertDisplayed(cache, binding, 2.4375);
        cache.ChangeMods([]);
        assertDisplayed(cache, binding, 3.25);
        Assert.That(cache.NativeLookups, Is.EqualTo(2));
    }

    [Test]
    public void NativeDisplayBindingCalculatesAMissingBaseline()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        beatmap.Metadata.Tags = O2JamStarRatingMetadata.CreateO2JamTag(75);
        using var cache = new TestDifficultyCache(ruleset.RulesetInfo);
        var binding = cache.GetBindableDifficulty(beatmap);

        assertDisplayed(cache, binding, 3.25);
        Assert.That(cache.NativeLookups, Is.EqualTo(1));
    }

    [Test]
    public void SongSelectStarAttributeUsesTheModdedDifficulty()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        var attributes = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            ruleset, beatmap, [new O2JamModDoubleTime()], 4.875, beatmap).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(attributes.Select(attribute => attribute.OriginalValue), Is.EqualTo(new[] { float.Epsilon, 3.25f, 75 }));
            Assert.That(attributes.Select(attribute => attribute.AdjustedValue), Is.EqualTo(new[] { float.Epsilon, 4.875f, 75 }));
        });
    }

    [Test]
    public void SongSelectIgnoresPreviousRulesetStarsWhileRebindingTheBeatmap()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        var previousBeatmap = createBeatmap(new ManiaRuleset().RulesetInfo, 90, 8);
        var attributes = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            ruleset, beatmap, [new O2JamModDoubleTime()], 8, previousBeatmap).ToArray();
        var stars = attributes.Single(attribute => attribute.Label == O2LazerStrings.StarRating);

        Assert.Multiple(() =>
        {
            Assert.That(stars.OriginalValue, Is.EqualTo(3.25));
            Assert.That(stars.AdjustedValue, Is.EqualTo(3.25));
        });
    }

    [Test]
    public void SongSelectUsesAnUnadjustedBaselineForTheFirstUpdateAfterRebinding()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        var attributes = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            ruleset, beatmap, [new O2JamModDoubleTime()], 3.25, beatmap, true).ToArray();
        var stars = attributes.Single(attribute => attribute.Label == O2LazerStrings.StarRating);

        Assert.Multiple(() =>
        {
            Assert.That(stars.OriginalValue, Is.EqualTo(3.25));
            Assert.That(stars.AdjustedValue, Is.EqualTo(3.25));
        });
    }

    [Test]
    public void SongSelectDefersAnUncalculatedStarAttribute()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, -1);
        var unavailable = O2JamBeatmapAttributes.Create(beatmap)
                                                  .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                                  .ToArray();
        var ready = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            ruleset, beatmap, [], 3.25, beatmap, true)
                                                  .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                                  .ToArray();
        var modded = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            ruleset, beatmap, [new O2JamModDoubleTime()], 4.875, beatmap, knownBaselineStars: 3.25)
                                                   .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                                   .ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingDisplayPatch.ShouldApplySongSelectStatistics(unavailable), Is.False);
            Assert.That(O2JamStarRatingDisplayPatch.ShouldApplySongSelectStatistics(ready), Is.True);
            Assert.That(ready.Single(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating).Value, Is.EqualTo(3.25));
            Assert.That(modded.Single(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating).Value, Is.EqualTo(3.25));
            Assert.That(modded.Single(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating).AdjustedValue, Is.EqualTo(4.875));
        });
    }

    [Test]
    public void SpreadDifficultyColoursUseLevelOnlyForNonMSO2Lazer()
    {
        var o2Beatmap = createBeatmap(new O2LazerRuleset().RulesetInfo, 75, 3.25);
        var maniaBeatmap = createBeatmap(new ManiaRuleset().RulesetInfo, 75, 3.25);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingPresentationPatch.GetColourStars(o2Beatmap, true), Is.EqualTo(7.5));
            Assert.That(O2JamStarRatingPresentationPatch.GetColourStars(o2Beatmap, false), Is.EqualTo(3.25));
            Assert.That(O2JamStarRatingPresentationPatch.GetColourStars(maniaBeatmap, true), Is.EqualTo(3.25));
        });
    }

    [Test]
    public void LevelPresentationSurvivesAnimatedStarUpdatesAndRestoresForMS()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        using var display = new StarRatingDisplay(new StarDifficulty(3.25, 0));
        typeof(StarRatingDisplay).GetProperty("colours", BindingFlags.Instance | BindingFlags.NonPublic)!
                                 .SetValue(display, new OsuColour());
        var callback = AccessTools.GetDeclaredMethods(typeof(StarRatingDisplay))
                                  .Single(method => method.Name.StartsWith("<LoadComplete>b__", StringComparison.Ordinal)
                                                    && method.GetParameters().Single().ParameterType == typeof(ValueChangedEvent<double>));
        var starIcon = (SpriteIcon)typeof(StarRatingDisplay).GetField("starIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
        var starsText = (OsuSpriteText)typeof(StarRatingDisplay).GetField("starsText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
        var background = (Box)typeof(StarRatingDisplay).GetField("background", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;

        O2JamStarRatingPresentationPatch.Configure(display, beatmap, []);
        callback.Invoke(display, [new ValueChangedEvent<double>(3.25, 4.875)]);
        Assert.Multiple(() =>
        {
            Assert.That(starIcon.Width, Is.Zero);
            Assert.That(starsText.Text.ToString(), Is.EqualTo(O2LazerStrings.LevelBadge(75).ToString()));
            Assert.That(background.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForStarDifficulty(7.5)));
        });

        O2JamStarRatingPresentationPatch.Configure(display, beatmap, [new O2JamModManiaScore()]);
        callback.Invoke(display, [new ValueChangedEvent<double>(3.25, 4.875)]);
        Assert.Multiple(() =>
        {
            Assert.That(starIcon.Width, Is.EqualTo(8));
            Assert.That(starsText.Text.ToString(), Is.EqualTo(4.875.FormatStarRating().ToString()));
            Assert.That(background.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForStarDifficulty(4.875)));
        });
    }

    [Test]
    public void OtherRulesetsKeepTheirNativeLookup()
    {
        _ = new O2LazerRuleset();
        var ruleset = new ManiaRuleset().RulesetInfo;
        var beatmap = new BeatmapInfo(ruleset) { StarRating = 4.5 };
        using var cache = new TestDifficultyCache(ruleset);
        var binding = cache.GetBindableDifficulty(beatmap);
        Assert.That(cache.NativeLookups, Is.EqualTo(1));
        assertDisplayed(cache, binding, 4.5);
    }

    [Test]
    public void CancelledDisplayRequestsKeepNativeCancellationCleanup()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var cache = new TestDifficultyCache(ruleset);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        var binding = cache.GetBindableDifficulty(createBeatmap(ruleset, 75, 3.25), cancellation.Token);
        Assert.That(() => { cache.Pump(); return cache.PendingRequests; }, Is.Zero.After(3000, 10));
        Assert.That(binding.Value.Stars, Is.EqualTo(3.25));
        Assert.That(cache.NativeLookups, Is.Zero);
    }

    private static BeatmapInfo createBeatmap(RulesetInfo ruleset, ushort level, double stars) => new(ruleset)
    {
        DifficultyName = $"EX Lv.{level}",
        StarRating = stars,
        Metadata = new BeatmapMetadata { Tags = $"{O2JamStarRatingMetadata.CreateO2JamTag(level)} {O2JamStarRatingMetadata.ManiaVersionTag}" },
    };

    private static void assertDisplayed(TestDifficultyCache cache, IBindable<StarDifficulty> binding, double expected) =>
        Assert.That(() => { cache.Pump(); return binding.Value.Stars; }, Is.EqualTo(expected).Within(0.000001).After(3000, 10));

    private static double getTooltipStars(DifficultyIcon icon)
    {
        var tooltipContentProperty = typeof(DifficultyIcon).GetProperties(BindingFlags.Instance | BindingFlags.NonPublic)
                                                                 .Single(property => property.Name.EndsWith(".TooltipContent", StringComparison.Ordinal));
        var content = tooltipContentProperty.GetValue(icon)!;
        var difficulty = (IBindable<StarDifficulty>)content.GetType().GetField("Difficulty")!.GetValue(content)!;
        return difficulty.Value.Stars;
    }

    private sealed partial class TestDifficultyCache : BeatmapDifficultyCache
    {
        private readonly Bindable<IReadOnlyList<Mod>> mods = new([]);
        public int NativeLookups { get; private set; }
        public int PendingRequests => ((List<CancellationTokenSource>)typeof(BeatmapDifficultyCache)
            .GetField("linkedCancellationSources", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(this)!).Count;

        public TestDifficultyCache(RulesetInfo ruleset)
        {
            var type = typeof(BeatmapDifficultyCache);
            type.GetProperty("currentRuleset", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, new Bindable<RulesetInfo>(ruleset));
            type.GetProperty("currentMods", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(this, mods);
        }

        public void Pump() => Scheduler.Update();

        public void ChangeMods(IReadOnlyList<Mod> value)
        {
            mods.Value = value;
            typeof(BeatmapDifficultyCache).GetMethod("updateTrackedBindables", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(this, null);
        }

        public override Task<StarDifficulty?> GetDifficultyAsync(IBeatmapInfo beatmapInfo, IRulesetInfo? rulesetInfo = null,
                                                                IEnumerable<Mod>? mods = null, CancellationToken cancellationToken = default, int computationDelay = 0)
        {
            NativeLookups++;
            var rate = ModUtils.CalculateRateWithMods(mods ?? []);
            return Task.FromResult<StarDifficulty?>(new StarDifficulty(beatmapInfo.StarRating * rate, 0));
        }
    }
}
