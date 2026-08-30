using System;
using System.IO;
using System.Reflection;
using NUnit.Framework;
using osu.Framework.Audio;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.UI.Icons;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
[Category("LocalDiagnostics")]
public partial class O2JamRulesetCoexistenceTest
{
    [Test]
    [Explicit("Loads BMSRuleset's bundled Harmony runtime into the same process.")]
    public void SharesOverlappingDifficultyStatisticsPatchWithBmsHarmony()
    {
        var path = Environment.GetEnvironmentVariable("O2JAM_BMS_RULESET_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Assert.Ignore("Set O2JAM_BMS_RULESET_PATH to the separately-built BmsRuleset DLL.");

        var bmsAssembly = Assembly.LoadFrom(path!);
        var bmsRulesetType = bmsAssembly.GetType("osu.Game.Rulesets.BmsRuleset.BmsRuleset", throwOnError: true)!;
        _ = (Ruleset)Activator.CreateInstance(bmsRulesetType)!;
        _ = new O2LazerRuleset();

        Assert.Multiple(() =>
        {
            Assert.That(O2JamBeatmapBoundaryPatches.UsesBmsHarmonyForStatistics, Is.True);
            Assert.That(O2JamDifficultyIconPatch.UsesBmsHarmony, Is.True);
        });
    }

    [Test]
    [Explicit("Loads the separately-built BmsRuleset/O2Jam ruleset into the same process.")]
    public void LoadsBmsRulesetAndCleanO2LazerTogether()
    {
        var path = Environment.GetEnvironmentVariable("O2JAM_BMS_RULESET_PATH");
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            Assert.Ignore("Set O2JAM_BMS_RULESET_PATH to the separately-built BmsRuleset DLL.");

        var bmsAssembly = Assembly.LoadFrom(path!);
        var bmsRulesetType = bmsAssembly.GetType("osu.Game.Rulesets.BmsRuleset.BmsRuleset", throwOnError: true)!;
        var bmsRuleset = (Ruleset)Activator.CreateInstance(bmsRulesetType)!;
        var o2Lazer = new O2LazerRuleset();
        var bmsPatcher = bmsAssembly.GetType("osu.Game.Rulesets.BmsRuleset.Beatmaps.BmsWorkingBeatmapPatcher", throwOnError: true)!;
        var bmsInstalled = (bool)bmsPatcher.GetProperty("IsInstalled", BindingFlags.Public | BindingFlags.Static)!.GetValue(null)!;
        var bmsHarmonyId = (string)bmsPatcher.GetField("harmony_id", BindingFlags.NonPublic | BindingFlags.Static)!.GetRawConstantValue()!;
        var o2HarmonyId = (string)typeof(O2JamWorkingBeatmapHook)
                                  .GetField("harmony_id", BindingFlags.NonPublic | BindingFlags.Static)!
                                  .GetRawConstantValue()!;

        Assert.Multiple(() =>
        {
            Assert.That(o2Lazer.ShortName, Is.EqualTo("o2lazer"));
            Assert.That(bmsRuleset.ShortName, Is.Not.EqualTo(o2Lazer.ShortName));
            Assert.That(O2JamWorkingBeatmapHook.IsInstalled, Is.True);
            Assert.That(bmsInstalled, Is.True);
            Assert.That(o2HarmonyId, Is.Not.EqualTo(bmsHarmonyId));
            Assert.That(typeof(O2LazerRuleset).Assembly, Is.Not.SameAs(bmsAssembly));
        });

        assertWorkingBeatmapBoundaries(bmsAssembly, bmsRuleset, o2Lazer);
    }

    private static void assertWorkingBeatmapBoundaries(Assembly bmsAssembly, Ruleset bmsRuleset, Ruleset o2Lazer)
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(O2JamRulesetCoexistenceTest)}-{Guid.NewGuid():N}");
        Exception? failure = null;

        host.Run(new CoexistenceProbeGame(game =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(O2JamRulesetCoexistenceTest)}-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var manager = new BeatmapManager(storage, realm, null, game.AudioManager, game.Resources, host, new StubWorkingBeatmap());
                var bmsSet = new BeatmapSetInfo();
                var bmsBeatmap = new BeatmapInfo(bmsRuleset.RulesetInfo)
                {
                    BeatmapSet = bmsSet,
                };
                bmsSet.Beatmaps.Add(bmsBeatmap);

                var bmsWorking = manager.GetWorkingBeatmap(bmsBeatmap);
                var expectedType = bmsAssembly.GetType("osu.Game.Rulesets.BmsRuleset.Beatmaps.BmsWorkingBeatmap", throwOnError: true)!;

                Assert.That(bmsWorking.GetType(), Is.EqualTo(expectedType),
                    "O2Lazer's independent WorkingBeatmap hook must not displace BMSRuleset's wrapper.");

                var sourceDirectory = storage.GetFullPath("o2jam-library");
                Directory.CreateDirectory(sourceDirectory);
                File.WriteAllBytes(Path.Combine(sourceDirectory, "chart.ojn"), OjnReaderTest.CreateChart());
                var o2Set = new BeatmapSetInfo();
                var o2Beatmap = new BeatmapInfo(o2Lazer.RulesetInfo)
                {
                    BeatmapSet = o2Set,
                    Metadata = new BeatmapMetadata
                    {
                        Source = sourceDirectory,
                        AudioFile = "chart.ojn",
                    },
                };
                o2Set.Beatmaps.Add(o2Beatmap);

                Assert.That(manager.GetWorkingBeatmap(o2Beatmap), Is.TypeOf<O2JamWorkingBeatmap>(),
                    "BMSRuleset's inner cache hook must not displace O2Lazer's manager wrapper.");
            }
            catch (Exception exception)
            {
                failure = exception;
            }
        }));

        if (failure != null)
            throw failure;
    }

    private partial class CoexistenceProbeGame(Action<CoexistenceProbeGame> probe) : Framework.Game
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

    private sealed class StubWorkingBeatmap : WorkingBeatmap
    {
        public StubWorkingBeatmap()
            : base(new BeatmapInfo(), null!)
        {
        }

        public override Texture GetBackground() => null!;

        public override Stream GetStream(string storagePath) => throw new NotSupportedException();

        protected override IBeatmap GetBeatmap() => new Beatmap { BeatmapInfo = BeatmapInfo };

        protected override Track GetBeatmapTrack() => throw new NotSupportedException();

        protected override ISkin GetSkin() => throw new NotSupportedException();
    }
}
