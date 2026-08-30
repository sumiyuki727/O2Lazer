using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Online.API.Requests.Responses;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Replays;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamReplayCompatibilityTest
{
    [Test]
    public void NativeFrameRemainsNativeInInputHandler()
    {
        var replay = new Replay
        {
            Frames = [new O2JamReplayFrame(125, ManiaAction.Key2, ManiaAction.Key7)],
        };

        _ = new O2JamFramedReplayInputHandler(replay);

        var converted = replay.Frames[0] as O2JamReplayFrame;
        Assert.Multiple(() =>
        {
            Assert.That(converted, Is.Not.Null);
            Assert.That(converted!.Time, Is.EqualTo(125));
            Assert.That(converted.Actions, Is.EqualTo(new[] { ManiaAction.Key2, ManiaAction.Key7 }));
        });
    }

    [Test]
    public void CannotRecordInterimManiaFramesAsNativeO2Jam()
    {
        var replay = new Replay
        {
            Frames = [new ManiaReplayFrame(250, ManiaAction.Key1, ManiaAction.Key4)],
        };

        Assert.Throws<InvalidDataException>(() => O2JamReplayArchive.Create(new Score { Replay = replay }));
    }

    [Test]
    public void OldReplayRuntimeAdaptersAreRemoved()
    {
        var assembly = typeof(O2LazerRuleset).Assembly;
        Assert.Multiple(() =>
        {
            Assert.That(assembly.GetType("osu.Game.Rulesets.O2Lazer.Replays.O2LazerReplayFrame"), Is.Null);
            Assert.That(assembly.GetType("osu.Game.Rulesets.O2Lazer.IO.Input.O2LazerAction"), Is.Null);
        });
    }

    [Test]
    public void RulesetDoesNotOfferStableReplayConversion()
    {
        Assert.That(new O2LazerRuleset().CreateConvertibleReplayFrame(), Is.Null);
    }

    [Test]
    public void AutoGeneratorUsesO2JamFramesAndExactHoldRelease()
    {
        var timingMap = new O2JamTimingMap(120);
        var beatmap = new O2JamBeatmap(O2JamDifficulty.EX, timingMap);
        beatmap.HitObjects.Add(new O2JamNote { StartTime = 100, Column = 0, TimingMap = timingMap });
        beatmap.HitObjects.Add(new O2JamHoldNote { StartTime = 200, Duration = 150, Column = 1, TimingMap = timingMap });

        var frames = new O2JamAutoGenerator(beatmap).Generate().Frames.Cast<O2JamReplayFrame>().ToArray();

        Assert.Multiple(() =>
        {
            Assert.That(frames.Select(frame => frame.Time), Is.EqualTo(new[] { 100, 120, 200, 350 }));
            Assert.That(frames[0].Actions, Is.EqualTo(new[] { ManiaAction.Key1 }));
            Assert.That(frames[1].Actions, Is.Empty);
            Assert.That(frames[2].Actions, Is.EqualTo(new[] { ManiaAction.Key2 }));
            Assert.That(frames[3].Actions, Is.Empty);
        });
    }

    [Test]
    public void RulesetProvidesNativeAutoplayForSkinEditor()
    {
        var ruleset = new O2LazerRuleset();
        var autoplay = ruleset.GetAutoplayMod();

        Assert.Multiple(() =>
        {
            Assert.That(autoplay, Is.TypeOf<O2JamModAutoplay>());
            Assert.That(ruleset.GetModsFor(ModType.Automation).Single(), Is.TypeOf<O2JamModAutoplay>());
            Assert.That(O2JamReplayPersistencePatch.IsInstalled, Is.True);
        });
    }

    [Test]
    public void ReplayArchiveRoundTripsFramesAndMetadata()
    {
        var ruleset = new O2LazerRuleset();
        var beatmapInfo = new BeatmapInfo(ruleset.RulesetInfo)
        {
            Hash = "difficulty-hash",
            MD5Hash = "difficulty-md5",
        };
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(beatmapInfo, ruleset.RulesetInfo)
            {
                User = new APIUser { Id = 42, Username = "player" },
                Date = new System.DateTimeOffset(2026, 8, 30, 12, 34, 0, System.TimeSpan.Zero),
                TotalScore = 123456,
                MaxCombo = 87,
                Accuracy = 0.987,
                Statistics = new() { [HitResult.Perfect] = 10, [HitResult.Miss] = 1 },
            },
            Replay = new Replay
            {
                Frames =
                [
                    new O2JamReplayFrame(100, ManiaAction.Key1),
                    new O2JamReplayFrame(125),
                ],
            },
        };

        var bytes = O2JamReplayArchive.Create(score);
        var restoredInfo = score.ScoreInfo.DeepClone();

        Assert.That(O2JamReplayArchive.TryReadScore(restoredInfo, bytes, out var restored), Is.True);
        Assert.That(O2JamReplayArchive.TryReadMetadata(bytes, out var metadata), Is.True);
        Assert.Multiple(() =>
        {
            Assert.That(restored.Replay.Frames, Has.Count.EqualTo(2));
            Assert.That(restored.Replay.Frames[0], Is.TypeOf<O2JamReplayFrame>());
            Assert.That(((O2JamReplayFrame)restored.Replay.Frames[0]).Actions, Is.EqualTo(new[] { ManiaAction.Key1 }));
            Assert.That(metadata.BeatmapHash, Is.EqualTo("difficulty-hash"));
            Assert.That(metadata.BeatmapMd5, Is.EqualTo("difficulty-md5"));
            Assert.That(metadata.Player, Is.EqualTo("player"));
            Assert.That(metadata.TotalScore, Is.EqualTo(123456));
        });
    }

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(6)]
    public void RejectsUnsupportedReplayVersions(int version)
    {
        var bytes = Encoding.UTF8.GetBytes(
            $$"""
            {
              "version": {{version}},
              "has_received_all_frames": true,
              "beatmap_hash": "difficulty-hash",
              "frames": [
                { "time": 250.0, "actions": [0, 6], "branch_decisions": "" }
              ],
              "total_score": 999
            }
            """);

        assertRejected(bytes);
        using var compressed = new MemoryStream();
        using (var gzip = new GZipStream(compressed, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        assertRejected(compressed.ToArray());
    }

    [Test]
    public void ReadsExistingCleanVersionFiveWithoutRulesetMarker()
    {
        var bytes = Encoding.UTF8.GetBytes(
            """
            { "version": 5, "beatmap_hash": "difficulty-hash", "frames": [{ "time": 250, "actions": [0, 6] }] }
            """);

        Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), bytes, out var restored), Is.True);
        Assert.That(O2JamReplayArchive.TryReadMetadata(bytes, out _), Is.True);
        Assert.That(((O2JamReplayFrame)restored.Replay.Frames.Single()).Actions,
            Is.EqualTo(new[] { ManiaAction.Key1, ManiaAction.Key7 }));
    }

    [TestCase("{}")]
    [TestCase("{\"frames\":[{\"time\":1,\"actions\":[0]}]}")]
    [TestCase("{\"version\":5,\"frames\":null}")]
    [TestCase("{\"version\":5,\"frames\":[null]}")]
    [TestCase("{\"version\":5,\"frames\":[{\"time\":1,\"actions\":null}]}")]
    [TestCase("{\"version\":5,\"frames\":[{\"time\":1,\"actions\":[7]}]}")]
    [TestCase("{\"version\":5,\"frames\":[{\"time\":\"NaN\",\"actions\":[0]}]}")]
    [TestCase("{\"version\":5,\"frames\":[{\"time\":2,\"actions\":[0]},{\"time\":1,\"actions\":[]}]}")]
    [TestCase("{\"version\":5,\"ruleset\":\"bms\",\"frames\":[{\"time\":1,\"actions\":[0]}]}")]
    public void RejectsMalformedOrForeignPayloads(string json) => assertRejected(Encoding.UTF8.GetBytes(json));

    private static void assertRejected(byte[] bytes)
    {
        Assert.Multiple(() =>
        {
            Assert.That(O2JamReplayArchive.TryReadScore(new ScoreInfo(), bytes, out var score), Is.False);
            Assert.That(score.Replay, Is.Null, "An empty Replay would still be offered to the player by osu!.");
            Assert.That(O2JamReplayArchive.TryReadMetadata(bytes, out _), Is.False);
        });
    }
}
