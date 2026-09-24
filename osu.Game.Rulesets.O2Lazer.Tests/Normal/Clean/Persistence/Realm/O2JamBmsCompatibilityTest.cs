using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.Drawables;
using osu.Game.Database;
using osu.Game.Graphics;
using osu.Game.Models;
using osu.Game.Screens.Select;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[NonParallelizable]
[Category("LocalDiagnostics")]
public partial class O2JamBmsCompatibilityTest
{
    [Test]
    [Explicit("Run separately to compare BMS before and after loading O2Lazer's bundled Harmony.")]
    public void BmsFilteringAndStarsSurviveO2LazerInitialisation() => runProbe(false);

    [Test]
    [Explicit("Run separately to verify BMS loaded after O2Lazer's bundled Harmony.")]
    public void BmsFilteringAndStarsWorkWhenLoadedLast() => runProbe(true);

    private static void runProbe(bool o2LazerFirst)
    {
        var path = Environment.GetEnvironmentVariable("O2JAM_BMS_RULESET_PATH");
        if (string.IsNullOrEmpty(path) || !File.Exists(path))
            Assert.Ignore("Set O2JAM_BMS_RULESET_PATH to the installed BmsRuleset DLL.");

        if (o2LazerFirst)
            _ = new O2LazerRuleset();
        var assembly = Assembly.LoadFrom(path!);
        var bms = (Ruleset)Activator.CreateInstance(assembly.GetType("osu.Game.Rulesets.BmsRuleset.BmsRuleset", true)!)!;

        using var host = new TestRunHeadlessGameHost($"O2JamBmsCompatibility-{Guid.NewGuid():N}");
        Exception? failure = null;
        host.Run(new ProbeGame(game =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"O2JamBmsCompatibility-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var manager = new BeatmapManager(storage, realm, null, game.AudioManager, game.Resources, host, new EmptyWorkingBeatmap());
                var beatmap = createChart(storage, bms);
                var before = checkChart(manager, bms, beatmap);
                var o2Lazer = new O2LazerRuleset();
                var after = checkChart(manager, bms, beatmap);
                Assert.That(after, Is.EqualTo(before), "Installing the mod badge must not change BMS difficulty.");
                assertIcon(new BeatmapInfo(o2Lazer.RulesetInfo), "osu.Game.Rulesets.O2Lazer.UI.Icons.O2JamRulesetIcon");

                var lampCalculator = assembly.GetType("osu.Game.Rulesets.BmsRuleset.SongSelect.BmsLampCalculator", true)!;
                var noPlay = lampCalculator.GetMethod("Calculate")!.Invoke(null, [null]);
                var lampType = assembly.GetType("osu.Game.Rulesets.BmsRuleset.SongSelect.BmsLampDisplay", true)!;
                using var display = (Drawable)Activator.CreateInstance(lampType, noPlay)!;
                var baseFill = (Drawable)lampType.GetField("baseFill", BindingFlags.NonPublic | BindingFlags.Instance)!.GetValue(display)!;
                TestContext.Progress.WriteLine($"BMS lamp without score: {noPlay}, colour: {(Color4)baseFill.Colour}");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }));
        if (failure != null)
            throw failure;
    }

    private static BeatmapInfo createChart(Storage storage, Ruleset bms)
    {
        var source = storage.GetFullPath("bms-library");
        Directory.CreateDirectory(source);
        File.WriteAllText(Path.Combine(source, "chart.bms"), """
            #PLAYER 1
            #TITLE Compatibility probe
            #BPM 150
            #PLAYLEVEL 5
            #RANK 2
            #WAV01 missing.wav
            #00111:01010101
            #00112:00010001
            #00213:01010101
            #00214:00010001
            #00315:01010101
            #00318:00010001
            """);
        var set = new BeatmapSetInfo();
        var beatmap = new BeatmapInfo(bms.RulesetInfo)
        {
            Hash = "bms-compatibility-chart",
            BeatmapSet = set,
            Metadata = new BeatmapMetadata { Source = source },
        };
        set.Beatmaps.Add(beatmap);
        set.Files.Add(new RealmNamedFileUsage(new RealmFile { Hash = beatmap.Hash }, "chart.bms"));
        return beatmap;
    }

    private static double checkChart(BeatmapManager manager, Ruleset bms, BeatmapInfo beatmap)
    {
        var criteria = new FilterCriteria
        {
            Ruleset = bms.RulesetInfo,
            RulesetCriteria = bms.CreateRulesetFilterCriteria(),
        };
        Assert.That(BeatmapCarouselFilterMatching.CheckCriteriaMatch(beatmap, criteria), Is.True);
        assertIcon(beatmap, "osu.Game.Rulesets.BmsRuleset.UI.Icons.BmsRulesetIcon");

        var working = manager.GetWorkingBeatmap(beatmap);
        TestContext.Progress.WriteLine($"Working beatmap: {working.GetType().FullName}");
        Assert.That(working.GetType().FullName, Is.EqualTo("osu.Game.Rulesets.BmsRuleset.Beatmaps.BmsWorkingBeatmap"));
        var playable = working.GetPlayableBeatmap(bms.RulesetInfo, []);
        TestContext.Progress.WriteLine($"Playable: {playable.GetType().FullName}, objects: {playable.HitObjects.Count}");
        Assert.That(playable.HitObjects.Count, Is.EqualTo(18));

        using var difficultyCache = new BeatmapDifficultyCache();
        typeof(BeatmapDifficultyCache).GetProperty("beatmapManager", BindingFlags.NonPublic | BindingFlags.Instance)!
                                    .SetValue(difficultyCache, manager);
        var stars = difficultyCache.GetDifficultyAsync(beatmap, bms.RulesetInfo, []).GetAwaiter().GetResult();
        Assert.That(stars, Is.Not.Null);
        TestContext.Progress.WriteLine($"Stars: {stars!.Value.Stars}");
        Assert.That(stars.Value.Stars, Is.GreaterThan(0));
        var colours = new OsuColour();
        Assert.That(colours.ForStarDifficulty(stars.Value.Stars), Is.Not.EqualTo(colours.ForStarDifficulty(0)));
        return stars.Value.Stars;
    }

    private static void assertIcon(BeatmapInfo beatmap, string expectedType)
    {
        using var icon = new DifficultyIcon(beatmap);
        using var rulesetIcon = (Drawable)typeof(DifficultyIcon).GetMethod("getRulesetIcon", BindingFlags.NonPublic | BindingFlags.Instance)!
                                                              .Invoke(icon, null)!;
        Assert.That(rulesetIcon.GetType().FullName, Is.EqualTo(expectedType));
    }

    private partial class ProbeGame(Action<ProbeGame> probe) : Framework.Game
    {
        internal AudioManager AudioManager => Audio;

        protected override void LoadComplete()
        {
            base.LoadComplete();
            Scheduler.Add(() =>
            {
                probe(this);
                Exit();
            });
        }
    }

    private sealed class EmptyWorkingBeatmap() : WorkingBeatmap(new BeatmapInfo(), null!)
    {
        public override Texture GetBackground() => null!;
        public override Stream GetStream(string storagePath) => throw new NotSupportedException();
        protected override IBeatmap GetBeatmap() => new Beatmap { BeatmapInfo = BeatmapInfo };
        protected override Track GetBeatmapTrack() => throw new NotSupportedException();
        protected override ISkin GetSkin() => throw new NotSupportedException();
    }
}
