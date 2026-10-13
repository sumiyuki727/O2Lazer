using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Replays;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamReplayImportTest
{
    private const string metadata_envelope = """
        {"version":5,"ruleset":"o2lazer","beatmap_hash":"replay-budget-chart","frames":[{"time":-10,"actions":[0,6]}]
        """;

    private static IEnumerable<TestCaseData> invalidImportedMetadata()
    {
        (string, string)[] fields =
        [
            ("accuracy", "1.25"), ("max_combo", "-12"), ("total_score", "-7"),
            ("total_score_without_mods", "-1"), ("rank", "-2"),
            ("statistics", "{\"Meh\":-1}"), ("maximum_statistics", "{\"100\":0}"),
            ("statistics", "{\"Perfect\":2147483647,\"Miss\":1}"),
        ];
        bool[] compressionModes = [false, true];
        foreach (var (property, value) in fields)
        {
            foreach (var compressed in compressionModes)
                yield return new TestCaseData(property, value, compressed);
        }
    }

    [TestCaseSource(nameof(invalidImportedMetadata))]
    public void NativeImportRejectsInvalidMetadataAndPreservesExistingScoreAndReplay(string property, string value, bool compressed)
    {
        using var storage = new TemporaryNativeStorage($"invalid-metadata-{Guid.NewGuid():N}");
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
        var replayHash = realm.Run(database => database.Find<ScoreInfo>(scoreId)!.Files.Single().File.Hash);
        var path = storage.GetFullPath("invalid.osr");
        File.WriteAllBytes(path, encodeMetadata($"{metadata_envelope},\"{property}\":{value}}}", compressed));

        Assert.That(importer.Import(new ImportTask(path)).GetAwaiter().GetResult(), Is.Null);
        var existing = realm.Run(database => database.Find<ScoreInfo>(scoreId)!.Detach());
        Assert.Multiple(() =>
        {
            Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(1));
            Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.EqualTo(filesBefore));
            Assert.That(existing.TotalScore, Is.EqualTo(12345));
            Assert.That(existing.BeatmapHash, Is.EqualTo(beatmap.Hash));
            Assert.That(existing.BeatmapInfo!.ID, Is.EqualTo(beatmap.ID));
            Assert.That(existing.Files.Single().File.Hash, Is.EqualTo(replayHash));
            Assert.That(importer.GetScore(existing).Replay.Frames, Has.Count.EqualTo(1));
            Assert.That(File.Exists(path), Is.True);
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void IdentifiedInvalidMetadataStopsBeforeRealmLookupOrForeignFallback(bool compressed)
    {
        using var archive = new StreamingReplayArchive(encodeMetadata($"{metadata_envelope},\"accuracy\":1.25}}", compressed));
        var prefix = typeof(O2JamReplayPersistencePatch).GetMethod("readO2JamReplayMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [null, archive, null];
        Assert.That(prefix.Invoke(null, arguments), Is.False);
        Assert.That(arguments[2], Is.Null);
        Assert.That(archive.StreamOpens, Is.EqualTo(1));
    }

    [TestCase("bms", false)]
    [TestCase("bms", true)]
    [TestCase("", false)]
    [TestCase("", true)]
    public void InvalidMetadataInForeignOrUnmarkedReplaysRemainsUnclaimed(string ruleset, bool compressed)
    {
        var json = metadata_envelope.Replace("o2lazer", ruleset, StringComparison.Ordinal) + ",\"accuracy\":1.25}";
        using var archive = new StreamingReplayArchive(encodeMetadata(json, compressed));
        var prefix = typeof(O2JamReplayPersistencePatch).GetMethod("readO2JamReplayMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [null, archive, null];
        Assert.That(prefix.Invoke(null, arguments), Is.True);
        Assert.That(arguments[2], Is.Null);
        Assert.That(archive.StreamOpens, Is.EqualTo(1));
    }

    [TestCase(false, false)]
    [TestCase(false, true)]
    [TestCase(true, false)]
    [TestCase(true, true)]
    public void NativeMetadataImportAndReloadPreserveRecordedModeAndFailure(bool maniaScore, bool compressed)
    {
        using var storage = new TemporaryNativeStorage($"valid-metadata-{Guid.NewGuid():N}");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        var mods = maniaScore ? "[{\"acronym\":\"MS\"},{\"acronym\":\"NF\"}]" : "[{\"acronym\":\"EX\"}]";
        var json = $"{metadata_envelope},\"mods_json\":{System.Text.Json.JsonSerializer.Serialize(mods)}," +
                   "\"user_id\":-1,\"rank\":-1,\"accuracy\":0.5,\"max_combo\":0,\"total_score\":42," +
                   "\"total_score_without_mods\":35,\"statistics\":{\"Meh\":1},\"maximum_statistics\":null,\"pauses\":[-20]}";
        var bytes = encodeMetadata(json, compressed);
        Guid scoreId;
        using (var realm = new RealmAccess(storage, "client.realm"))
        {
            createBudgetBeatmap(realm, ruleset);
            // Native Mod reconstruction requires the installed ruleset to be marked available.
            realm.Write(database => database.Find<osu.Game.Rulesets.RulesetInfo>(ruleset.ShortName)!.Available = true);
            var importer = new TestScoreImporter(rulesets, storage, realm);
            using var archive = new StreamingReplayArchive(bytes);
            var headers = importer.ReadHeaders(archive);
            Assert.That(headers, Is.Not.Null);
            Assert.That(archive.StreamOpens, Is.EqualTo(1));
            scoreId = importer.StoreReplay(headers!, bytes);
        }

        using var reopened = new RealmAccess(storage, "client.realm");
        var stored = reopened.Run(database => database.Find<ScoreInfo>(scoreId)!.Detach());
        var replay = new TestScoreImporter(rulesets, storage, reopened).GetScore(stored);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Accuracy, Is.EqualTo(0.5));
            Assert.That(stored.MaxCombo, Is.Zero);
            Assert.That(stored.TotalScore, Is.EqualTo(42));
            Assert.That(stored.TotalScoreWithoutMods, Is.EqualTo(35));
            Assert.That(stored.Rank, Is.EqualTo(ScoreRank.F));
            Assert.That(stored.Passed, Is.False);
            Assert.That(stored.User.OnlineID, Is.EqualTo(-1));
            Assert.That(stored.ModsJson, Is.EqualTo(mods));
            Assert.That(stored.Statistics[HitResult.Meh], Is.EqualTo(1));
            Assert.That(stored.MaximumStatistics, Is.Empty);
            Assert.That(stored.Pauses, Is.EqualTo(new[] { -20 }));
            Assert.That(replay.Replay.Frames.Single().Time, Is.EqualTo(-10));
        });
    }

    [Test]
    public void StoredPlaybackUsesNativeScoreWithoutReimportingOldEnvelopeFields()
    {
        using var storage = new TemporaryNativeStorage($"stored-metadata-{Guid.NewGuid():N}");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        var bytes = encodeMetadata($"{metadata_envelope},\"accuracy\":1.25,\"max_combo\":-12}}", true);
        Guid scoreId;
        using (var realm = new RealmAccess(storage, "client.realm"))
        {
            var beatmap = createBudgetBeatmap(realm, ruleset);
            var score = new ScoreInfo(beatmap, ruleset, new RealmUser { Username = "saved" })
            {
                Accuracy = 0.9, MaxCombo = 8, TotalScore = 123, Rank = ScoreRank.A,
            };
            scoreId = new TestScoreImporter(rulesets, storage, realm).StoreReplay(score, bytes);
        }
        using var reopened = new RealmAccess(storage, "client.realm");
        var importer = new TestScoreImporter(rulesets, storage, reopened);
        var stored = reopened.Run(database => database.Find<ScoreInfo>(scoreId)!.Detach());
        var replay = importer.GetScore(stored);
        Assert.Multiple(() =>
        {
            Assert.That(stored.Accuracy, Is.EqualTo(0.9));
            Assert.That(stored.MaxCombo, Is.EqualTo(8));
            Assert.That(stored.TotalScore, Is.EqualTo(123));
            Assert.That(stored.Rank, Is.EqualTo(ScoreRank.A));
            Assert.That(replay.Replay.Frames.Single().Time, Is.EqualTo(-10));
            Assert.That(stored.Files, Has.Count.EqualTo(1));
            Assert.That(reopened.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(1));
        });
    }

    private static byte[] encodeMetadata(string json, bool compressed)
    {
        var bytes = Encoding.UTF8.GetBytes(json);
        if (!compressed)
            return bytes;
        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }
}
