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
    public void ResultsKeepLevelTextSeparateFromTheColourScale(bool o2lazer, bool ms, bool inLibrary, double expected)
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
        cache.ChangeMods(ms ? [new O2JamModManiaScore()] : []);
        var score = new ScoreInfo(ruleset: ruleset) { BeatmapInfo = beatmap, Mods = ms ? [new O2JamModManiaScore()] : [] };
        using var panel = new ExpandedPanelMiddleContent(score);
        typeof(ExpandedPanelMiddleContent).GetMethod("load", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(panel, [realm, cache]);

        var display = panel.ChildrenOfType<StarRatingDisplay>().Single();
        // Native text now initialises on LoadComplete, which this reflection-only fixture
        // does not run. Drive its actual value callback before inspecting rendered text.
        typeof(StarRatingDisplay).GetProperty("colours", BindingFlags.Instance | BindingFlags.NonPublic)!
                                 .SetValue(display, new OsuColour());
        var nativeTextUpdate = AccessTools.GetDeclaredMethods(typeof(StarRatingDisplay))
                                         .Single(method => method.Name.StartsWith("<LoadComplete>b__", StringComparison.Ordinal)
                                                           && method.GetParameters().Length == 1
                                                           && method.GetParameters()[0].ParameterType == typeof(ValueChangedEvent<double>));
        nativeTextUpdate.Invoke(display, [new ValueChangedEvent<double>(0, display.Current.Value.Stars)]);
        var icon = panel.ChildrenOfType<DifficultyIcon>().Single();
        var expectedIconStars = o2lazer && !ms ? 7.5 : expected;
        var expectedColourStars = o2lazer && !ms ? 7.5 : expected;
        var starIcon = (SpriteIcon)typeof(StarRatingDisplay).GetField("starIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
        var starsText = (OsuSpriteText)typeof(StarRatingDisplay).GetField("starsText", BindingFlags.Instance | BindingFlags.NonPublic)!.GetValue(display)!;
        Assert.Multiple(() =>
        {
            Assert.That(O2JamStarRatingPresentationPatch.IsInstalled, Is.True);
            Assert.That(display.Current.Value.Stars, Is.EqualTo(expectedColourStars),
                "The internal value drives the native colour animation; the badge text is asserted separately.");
            Assert.That(icon.Current.Value.Stars, Is.EqualTo(expectedIconStars), "The ruleset icon must use the score's display-mode colour.");
            Assert.That(getTooltipStars(icon), Is.EqualTo(expectedIconStars), "The ruleset icon tooltip must receive the same colour-driving value.");

            if (o2lazer && !ms)
            {
                Assert.That(starIcon.Width, Is.Zero);
                Assert.That(starsText.X, Is.EqualTo(-1.5f));
                Assert.That(starsText.Font.FixedWidth, Is.False);
                Assert.That(starsText.Spacing.X, Is.Zero);
                Assert.That(starsText.Text.ToString(), Is.EqualTo(O2LazerStrings.LevelBadge(75).ToString()));
            }
            else
            {
                Assert.That(starIcon.Width, Is.EqualTo(8));
                Assert.That(starsText.X, Is.Zero);
                Assert.That(starsText.Font.FixedWidth, Is.True);
                Assert.That(starsText.Spacing.X, Is.EqualTo(-1.4f));
                Assert.That(starsText.Text.ToString(), Is.EqualTo(expected.FormatStarRating().ToString()));
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
            Assert.That(display.Current.Value.Stars, Is.EqualTo(7.5));
            Assert.That(icon.Current.Value.Stars, Is.EqualTo(7.5));
            Assert.That(getTooltipStars(icon), Is.EqualTo(7.5));
            Assert.That(cache.NativeLookups, Is.EqualTo(1));
        });
    }

    [Test]
    public void ColourBindingKeepsLevelColourSeparateFromNativeStars()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        var native = new Bindable<StarDifficulty>(new StarDifficulty(3.25, 100));
        var binding = new O2JamDifficultyColourBinding(native, beatmap, ruleset.RulesetInfo, []);

        Assert.Multiple(() =>
        {
            Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(7.5));
            Assert.That(binding.ColourDifficulty.Value.MaxCombo, Is.Zero);
        });
        native.Value = new StarDifficulty(4.875, 100);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(7.5),
            "Native recalculation must not create a second colour update while level presentation is active.");

        binding.UpdateProfile(ruleset.RulesetInfo, [new O2JamModManiaScore()]);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(4.875));
        native.Value = new StarDifficulty(3.25, 100);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(3.25));

        binding.UpdateProfile(ruleset.RulesetInfo, []);
        Assert.That(binding.ColourDifficulty.Value.Stars, Is.EqualTo(7.5));
        Assert.That(native.Value.Stars, Is.EqualTo(3.25), "Presentation must never overwrite the native mania difficulty.");
    }

    [Test]
    public void NativeDifficultyCacheUsesStoredManiaStarsAcrossMSChanges()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        using var cache = new TestDifficultyCache(ruleset.RulesetInfo);
        var binding = cache.GetBindableDifficulty(beatmap);

        assertDisplayed(cache, binding, 3.25);
        cache.ChangeMods([new O2JamModManiaScore()]);
        assertDisplayed(cache, binding, 3.25);
        cache.ChangeMods([]);
        assertDisplayed(cache, binding, 3.25);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamDifficultyCachePatch.IsInstalled, Is.True);
            Assert.That(cache.NativeLookups, Is.Zero);
            Assert.That(beatmap.StarRating, Is.EqualTo(3.25));
        });
    }

    [Test]
    public void NativeDifficultyCacheAlwaysUsesManiaStars()
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
    public void SetSpreadOrdersByChartedLevelRatherThanChartLetter()
    {
        var ruleset = new O2LazerRuleset().RulesetInfo;

        // The NX chart out-levelling the HX chart mirrors real O2Jam sets, where the letters only
        // name a tier while the charted level decides how hard the difficulty actually is.
        var nx = createBeatmap(ruleset, 70, 1);
        nx.DifficultyName = "NX Lv.70";
        var hx = createBeatmap(ruleset, 40, 0.5);
        hx.DifficultyName = "HX Lv.40";
        var ex = createBeatmap(ruleset, 20, 5);
        ex.DifficultyName = "EX Lv.20";
        BeatmapInfo[] beatmaps = [nx, hx, ex];

        Assert.Multiple(() =>
        {
            Assert.That(O2JamLevelSpreadAdapter.OrderSpreadBeatmaps(beatmaps, true).Select(beatmap => beatmap.DifficultyName),
                Is.EqualTo(new[] { "EX Lv.20", "HX Lv.40", "NX Lv.70" }));
            Assert.That(O2JamLevelSpreadAdapter.OrderSpreadBeatmaps(beatmaps, false).Select(beatmap => beatmap.DifficultyName),
                Is.EqualTo(new[] { "HX Lv.40", "NX Lv.70", "EX Lv.20" }));
            Assert.That(O2JamLevelSpreadAdapter.GetLevelColourStars(nx), Is.EqualTo(7.0),
                "Level / 10 is only the input to the native colour gradient.");
        });
    }

    [Test]
    public void LevelSpreadPreservesNativeStarsForOtherRulesetsInMixedSets()
    {
        var o2 = createBeatmap(new O2LazerRuleset().RulesetInfo, 70, 1);
        o2.DifficultyName = "O2 Lv.70";
        var mania = createBeatmap(new ManiaRuleset().RulesetInfo, 75, 3.25);
        mania.DifficultyName = "Mania";
        BeatmapInfo[] beatmaps = [o2, mania];

        Assert.Multiple(() =>
        {
            Assert.That(O2JamLevelSpreadAdapter.OrderSpreadBeatmaps(beatmaps, true).Select(beatmap => beatmap.DifficultyName),
                Is.EqualTo(new[] { "Mania", "O2 Lv.70" }));
            Assert.That(O2JamLevelSpreadAdapter.GetColourStars(mania, true), Is.EqualTo(3.25));
            Assert.That(O2JamLevelSpreadAdapter.GetColourStars(o2, true), Is.EqualTo(7.0));
        });
    }

    [Test]
    public void NativeDifficultyCacheCalculatesAMissingBaseline()
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
            ruleset, beatmap, [new O2JamModDoubleTime()], 4.875).ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(attributes.Select(attribute => attribute.OriginalValue), Is.EqualTo(new[] { float.Epsilon, 3.25f, 75 }));
            Assert.That(attributes.Select(attribute => attribute.AdjustedValue), Is.EqualTo(new[] { float.Epsilon, 4.875f, 75 }));
        });
    }

    [Test]
    public void SongSelectStarAttributeNeverUsesTheLevelColourScale()
    {
        var ruleset = new O2LazerRuleset();
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        var attributes = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            ruleset, beatmap, [], 3.25).ToArray();
        var stars = attributes.Single(attribute => attribute.Label == O2LazerStrings.StarRating);

        Assert.Multiple(() =>
        {
            Assert.That(stars.OriginalValue, Is.EqualTo(3.25));
            Assert.That(stars.AdjustedValue, Is.EqualTo(3.25));
            Assert.That(stars.AdjustedValue, Is.Not.EqualTo(7.5), "Level / 10 is a colour input, not a star rating.");
        });
    }

    [Test]
    public void SongSelectAnimatesO2AttributesBeforeManiaStarsAreReady()
    {
        var ruleset = new O2LazerRuleset();
        Assert.That(O2JamStarRatingDisplayPatch.IsInstalled, Is.True,
            "The native title update must use the call-site adapter.");
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, -1);
        var pending = O2JamBeatmapAttributes.Create(beatmap)
                                             .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                             .ToArray();
        var ready = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(ruleset, beatmap, [], 3.25)
                                               .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                               .ToArray();
        var modded = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            ruleset, beatmap, [new O2JamModDoubleTime()], 4.875)
                                               .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                               .ToArray();
        BeatmapTitleWedge.StatisticDifficulty.Data[] otherRuleset = [new("OD", 4, 4, 10)];

        using var display = new BeatmapTitleWedge.DifficultyStatisticsDisplay();
        O2JamStarRatingDisplayPatch.WriteSongSelectStatistics(display, otherRuleset);
        O2JamStarRatingDisplayPatch.WriteSongSelectStatistics(display, pending);
        Assert.That(display.Statistics.Select(data => data.Label),
            Is.EqualTo(pending.Select(data => data.Label)), "Native O2MA/LV entries should appear without waiting for SR.");
        var waitingStar = display.Statistics.Single(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating);
        Assert.Multiple(() =>
        {
            Assert.That(waitingStar.Content, Is.Empty);
            Assert.That(waitingStar.AdjustedValue, Is.Zero);
            Assert.That(waitingStar.BeatmapAttribute!.AdjustedValue, Is.Zero);
            Assert.That(display.Statistics.Where(data => data.BeatmapAttribute?.Label != O2LazerStrings.StarRating)
                                          .Select(data => data.AdjustedValue),
                Is.EqualTo(pending.Where(data => data.BeatmapAttribute?.Label != O2LazerStrings.StarRating)
                                   .Select(data => data.AdjustedValue)));
        });

        O2JamStarRatingDisplayPatch.WriteSongSelectStatistics(display, ready);
        Assert.That(display.Statistics, Is.SameAs(ready));
        O2JamStarRatingDisplayPatch.WriteSongSelectStatistics(display, pending);
        var retainedStar = display.Statistics.Single(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating);
        Assert.That(retainedStar.AdjustedValue, Is.EqualTo(3.25));
        Assert.That(retainedStar.BeatmapAttribute!.AdjustedValue, Is.EqualTo(3.25));

        O2JamStarRatingDisplayPatch.WriteSongSelectStatistics(display, modded);
        Assert.That(display.Statistics, Is.SameAs(modded));

        using var cold = new BeatmapTitleWedge.DifficultyStatisticsDisplay();
        O2JamStarRatingDisplayPatch.WriteSongSelectStatistics(cold, pending);
        Assert.That(cold.Statistics, Has.Count.EqualTo(3));
        Assert.That(cold.Statistics.Single(data => data.BeatmapAttribute?.Label == O2LazerStrings.StarRating).Content,
            Is.Empty);
    }

    [Test]
    public void TitleStartsO2EntryFromATransientOtherRulesetBeatmap()
    {
        var o2Ruleset = new O2LazerRuleset();
        var maniaBeatmap = createBeatmap(new ManiaRuleset().RulesetInfo, 75, 3.25);
        var transition = O2JamStarRatingDisplayPatch.GetSongSelectAttributes(
            o2Ruleset, maniaBeatmap, [], -1)
                                                .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                                .ToArray();
        var prepared = O2JamStarRatingDisplayPatch.PrepareSongSelectStatistics(transition, pendingPreviousStars: 5.75);

        Assert.Multiple(() =>
        {
            Assert.That(transition.Single(data => data.Label == O2LazerStrings.StarRating).AdjustedValue, Is.LessThan(0),
                "Attribute generation should leave unresolved SR selection to the write adapter.");
            Assert.That(prepared.Select(data => data.Label),
                Is.EqualTo(new[] { O2LazerStrings.O2Ma, O2LazerStrings.StarRating, O2LazerStrings.O2JamLevel }));
            var stars = prepared.Single(data => data.Label == O2LazerStrings.StarRating);
            Assert.That(stars.AdjustedValue, Is.EqualTo(5.75));
            Assert.That(stars.BeatmapAttribute!.Description, Is.EqualTo(O2LazerStrings.MissingManiaStarRatingDescription));
        });

        var o2Pending = O2JamBeatmapAttributes.Create(createBeatmap(o2Ruleset.RulesetInfo, 75, -1))
                                              .Select(attribute => new BeatmapTitleWedge.StatisticDifficulty.Data(attribute))
                                              .ToArray();
        var awaitingCalculation = O2JamStarRatingDisplayPatch.PrepareSongSelectStatistics(o2Pending, prepared);
        var heldStars = awaitingCalculation.Single(data => data.Label == O2LazerStrings.StarRating);
        Assert.Multiple(() =>
        {
            Assert.That(heldStars.AdjustedValue, Is.EqualTo(5.75));
            Assert.That(heldStars.Content, Is.Null, "Native formatting should continue showing the previous star number.");
        });
    }

    [Test]
    public void SpreadDifficultyColoursUseLevelOnlyForNonMSO2Lazer()
    {
        var o2Beatmap = createBeatmap(new O2LazerRuleset().RulesetInfo, 75, 3.25);
        var maniaBeatmap = createBeatmap(new ManiaRuleset().RulesetInfo, 75, 3.25);

        Assert.Multiple(() =>
        {
            Assert.That(O2JamLevelSpreadAdapter.GetColourStars(o2Beatmap, true), Is.EqualTo(7.5));
            Assert.That(O2JamLevelSpreadAdapter.GetColourStars(o2Beatmap, false), Is.EqualTo(3.25));
            Assert.That(O2JamLevelSpreadAdapter.GetColourStars(maniaBeatmap, true), Is.EqualTo(3.25));
        });
    }

    [Test]
    public void LevelColourValueNeverReplacesLevelTextAndMSRestoresStars()
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
        callback.Invoke(display, [new ValueChangedEvent<double>(3.25, 7.5)]);
        Assert.Multiple(() =>
        {
            Assert.That(starIcon.Width, Is.Zero);
            Assert.That(starsText.X, Is.EqualTo(-1.5f));
            Assert.That(starsText.Font.FixedWidth, Is.False);
            Assert.That(starsText.Spacing.X, Is.Zero);
            Assert.That(starsText.Text.ToString(), Is.EqualTo(O2LazerStrings.LevelBadge(75).ToString()));
            Assert.That(background.Colour.AverageColour.SRGB, Is.EqualTo(new OsuColour().ForStarDifficulty(7.5)));
        });

        O2JamStarRatingPresentationPatch.Configure(display, beatmap, [new O2JamModManiaScore()]);
        callback.Invoke(display, [new ValueChangedEvent<double>(3.25, 4.875)]);
        Assert.Multiple(() =>
        {
            Assert.That(starIcon.Width, Is.EqualTo(8));
            Assert.That(starsText.X, Is.Zero);
            Assert.That(starsText.Font.FixedWidth, Is.True);
            Assert.That(starsText.Spacing.X, Is.EqualTo(-1.4f));
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
