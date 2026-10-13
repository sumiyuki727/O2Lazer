using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Overlays.Notifications;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamReplayImportTest
{
    [Test]
    public void StreamingArchiveUsesNativeGetStreamWithoutMaterializingGet()
    {
        using var storage = new TemporaryNativeStorage($"streaming-replay-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        var importer = new TestScoreImporter(rulesets, storage, realm);
        var beatmap = createBudgetBeatmap(realm, ruleset);
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(beatmap, ruleset, new RealmUser { Username = "player" }),
            Replay = new Replay { Frames = [new O2JamReplayFrame(100, ManiaAction.Key1)] },
        };
        using var archive = new StreamingReplayArchive(O2JamReplayArchive.Create(score));
        Assert.That(importer.ReadHeaders(archive), Is.Not.Null);
        Assert.That(archive.StreamOpens, Is.EqualTo(1));
        Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.Zero);
    }

    [Test]
    public void NativeImportRejectsOversizeO2JamWithoutWritingOrDeletingExistingData()
    {
        using var storage = new TemporaryNativeStorage($"oversize-import-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        var importer = new TestScoreImporter(rulesets, storage, realm);
        var beatmap = createBudgetBeatmap(realm, ruleset);
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(beatmap, ruleset, new RealmUser { Username = "existing" }) { TotalScore = 12345 },
            Replay = new Replay { Frames = [new O2JamReplayFrame(100, ManiaAction.Key1)] },
        };
        var scoreId = importer.StoreReplay(score.ScoreInfo, O2JamReplayArchive.Create(score));
        var filesBefore = realm.Run(database => database.All<RealmFile>().Count());
        var notices = new ConcurrentBag<Notification>();
        importer.PostNotification = notices.Add;
        var path = storage.GetFullPath("rejected.osr");
        File.WriteAllBytes(path, oversizedReplay());

        Assert.That(importer.Import(new ImportTask(path)).GetAwaiter().GetResult(), Is.Null);
        var existing = realm.Run(database => database.Find<ScoreInfo>(scoreId)!.Detach());
        Assert.Multiple(() =>
        {
            Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(1));
            Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.EqualTo(filesBefore));
            Assert.That(existing.TotalScore, Is.EqualTo(12345));
            Assert.That(existing.Files, Has.Count.EqualTo(1));
            Assert.That(importer.GetScore(existing).Replay.Frames, Has.Count.EqualTo(1));
            Assert.That(File.Exists(path), Is.True, "Native failed imports keep their source file.");
            Assert.That(notices.OfType<SimpleNotification>().Count(item => item.Text.Equals(O2LazerStrings.ReplayReadLimitExceeded)), Is.EqualTo(1));
        });
    }

    [Test]
    public void StoredOversizeReplayIsUnavailableAndItsScoreAndFileSurviveReload()
    {
        using var storage = new TemporaryNativeStorage($"oversize-stored-{Guid.NewGuid():N}");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        Guid scoreId;
        using (var realm = new RealmAccess(storage, "client.realm"))
        {
            var importer = new TestScoreImporter(rulesets, storage, realm);
            var beatmap = createBudgetBeatmap(realm, ruleset);
            var score = new ScoreInfo(beatmap, ruleset, new RealmUser { Username = "oversize" }) { TotalScore = 54321 };
            scoreId = importer.StoreReplay(score, oversizedReplay());
        }

        using var reopened = new RealmAccess(storage, "client.realm");
        var stored = reopened.Run(database => database.Find<ScoreInfo>(scoreId)!.Detach());
        var reader = new TestScoreImporter(rulesets, storage, reopened);
        var notices = new List<Notification>();
        reader.PostNotification = notices.Add;
        Assert.That(reader.GetScore(stored).Replay, Is.Null);
        Assert.Multiple(() =>
        {
            Assert.That(stored.TotalScore, Is.EqualTo(54321));
            Assert.That(stored.Files, Has.Count.EqualTo(1));
            Assert.That(reopened.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(1));
            Assert.That(reopened.Run(database => database.All<RealmFile>().Count()), Is.EqualTo(1));
            Assert.That(notices.OfType<SimpleNotification>().Single().Text, Is.EqualTo(O2LazerStrings.ReplayReadLimitExceeded));
        });
    }

    [TestCase("bms")]
    [TestCase("")]
    public void OversizeForeignOrUnmarkedEnvelopeRemainsUnclaimed(string ruleset)
    {
        using var archive = new StreamingReplayArchive(oversizedReplay(ruleset));
        var prefix = typeof(O2JamReplayPersistencePatch).GetMethod("readO2JamReplayMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [null, archive, null];
        Assert.That(prefix.Invoke(null, arguments), Is.True);
        Assert.That(arguments[2], Is.Null);
        Assert.That(archive.StreamOpens, Is.EqualTo(1));
    }

    [TestCase("{\"version\":5,\"ruleset\":\"o2lazer\",\"beatmap_hash\":\"chart\",\"frames\":[")]
    [TestCase("{\"version\":5,\"ruleset\":\"o2lazer\",\"frames\":[{\"time\":1,\"actions\":[0]}]}")]
    [TestCase("{\"version\":5,\"ruleset\":\"o2lazer\",\"beatmap_hash\":\"chart\",\"frames\":[{\"time\":1,\"actions\":[7]}]}")]
    public void IdentifiedInvalidO2JamPayloadDoesNotRetryForeignDecoders(string json)
    {
        using var archive = new StreamingReplayArchive(Encoding.UTF8.GetBytes(json));
        var prefix = typeof(O2JamReplayPersistencePatch).GetMethod("readO2JamReplayMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [null, archive, null];
        Assert.That(prefix.Invoke(null, arguments), Is.False);
        Assert.That(arguments[2], Is.Null);
        Assert.That(archive.StreamOpens, Is.EqualTo(1));
    }
    private static BeatmapInfo createBudgetBeatmap(RealmAccess realm, RulesetInfo ruleset) => realm.Write(database =>
    {
        var owned = database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID));
        var set = new BeatmapSetInfo { Hash = "replay-budget-set" };
        var chart = new BeatmapInfo(owned) { Hash = "replay-budget-chart", MD5Hash = "replay-budget-md5", BeatmapSet = set };
        set.Beatmaps.Add(chart);
        database.Add(set);
        return chart.Detach();
    });

    private static byte[] oversizedReplay(string ruleset = "o2lazer") => Encoding.UTF8.GetBytes(
        $"{{\"version\":5,\"ruleset\":\"{ruleset}\",\"beatmap_hash\":\"replay-budget-chart\",\"player\":\"{new string('x', O2JamReplayResourceLimits.Default.TextCharacters + 1)}\",\"frames\":[{{\"time\":100,\"actions\":[0]}}]}}");

    private sealed class StreamingReplayArchive(byte[] bytes) : ArchiveReader("streaming.osr")
    {
        public int StreamOpens { get; private set; }
        public override IEnumerable<string> Filenames => ["streaming.osr"];
        public override byte[] Get(string name) => throw new InvalidOperationException("The importer must use the native stream API.");
        public override Stream GetStream(string name)
        {
            StreamOpens++;
            return new MemoryStream(bytes, writable: false);
        }
        public override void Dispose() { }
    }
}