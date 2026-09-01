using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Bindables;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Database;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.SongSelect;
using osu.Game.Scoring;
using osu.Game.Screens.Ranking.Expanded;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamStarRatingDisplayTest
{
    [TestCase(true, false, false, 7.5)]
    [TestCase(true, false, true, 7.5)]
    [TestCase(true, true, false, 3.25)]
    [TestCase(true, true, true, 3.25)]
    [TestCase(false, false, false, 3.25)]
    [TestCase(false, false, true, 3.25)]
    public void NativeResultsBadgeUsesTheRecordedScoreMods(bool o2lazer, bool ms, bool inLibrary, double expected)
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
        Assert.Multiple(() =>
        {
            Assert.That(display.Current.Value.Stars, Is.EqualTo(expected));
            Assert.That(icon.Current.Value.Stars, Is.EqualTo(expected), "The ruleset icon colour must use the displayed score difficulty.");
            Assert.That(getTooltipStars(icon), Is.EqualTo(expected), "The ruleset icon tooltip must use the displayed score difficulty.");
        });
        Assert.That(beatmap.StarRating, Is.EqualTo(3.25), "The display must never overwrite native mania stars.");
        Assert.That(cache.NativeLookups, Is.EqualTo(inLibrary ? 1 : 0));
    }

    [Test]
    public async Task NativeDisplayBindingSwitchesWithoutChangingSearchOrCalculationValues()
    {
        var ruleset = new O2LazerRuleset();
        Assert.That(O2JamStarRatingDisplayPatch.IsInstalled, Is.True);
        var beatmap = createBeatmap(ruleset.RulesetInfo, 75, 3.25);
        using var cache = new TestDifficultyCache(ruleset.RulesetInfo);
        var binding = cache.GetBindableDifficulty(beatmap);

        assertDisplayed(cache, binding, 7.5);
        cache.ChangeMods([new O2JamModManiaScore()]);
        assertDisplayed(cache, binding, 3.25);
        cache.ChangeMods([]);
        assertDisplayed(cache, binding, 7.5);
        Assert.That(cache.NativeLookups, Is.Zero);

        var native = await cache.GetDifficultyAsync(beatmap, ruleset.RulesetInfo, []);
        Assert.That(native?.Stars, Is.EqualTo(3.25));
        Assert.That(beatmap.StarRating, Is.EqualTo(3.25));

        var refreshed = createBeatmap(ruleset.RulesetInfo, 99, 4.5);
        refreshed.ID = beatmap.ID;
        cache.Invalidate(beatmap, refreshed);
        assertDisplayed(cache, binding, 9.9);
        cache.ChangeMods([new O2JamModManiaScore()]);
        assertDisplayed(cache, binding, 4.5);
        Assert.That(cache.NativeLookups, Is.EqualTo(1));
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
            return Task.FromResult<StarDifficulty?>(new StarDifficulty(beatmapInfo.StarRating, 0));
        }
    }
}
