using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;
using NUnit.Framework;
using osu.Framework.Platform;
using osu.Framework.Testing;
using osu.Game.Database;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Scoring;
using osu.Game.Scoring.Legacy;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamReplayImportTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void NativeImporterRejectsOldArchivesWithoutCreatingScores(bool compressed)
    {
        using var storage = new TemporaryNativeStorage($"{nameof(O2JamReplayImportTest)}-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        using var rulesets = new TestRulesetStore(new O2LazerRuleset().RulesetInfo, new ManiaRuleset().RulesetInfo);
        var importer = new TestScoreImporter(rulesets, storage, realm);

        for (var version = 1; version <= 4; version++)
        {
            var bytes = createOldPayload(version, compressed);
            using var archive = new ByteArrayArchiveReader(bytes, "player playing old chart.osr");
            Assert.That(() => importer.ReadHeaders(archive), Throws.Nothing);
            Assert.That(importer.ReadHeaders(archive), Is.Null);
        }

        Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.Zero);
    }

    [Test]
    public void StoredOldReplaysAreUnavailableButCurrentReplaysRemainPlayable()
    {
        using var storage = new TemporaryNativeStorage($"{nameof(O2JamReplayImportTest)}-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        var importer = new TestScoreImporter(rulesets, storage, realm);
        var scoreInfo = new ScoreInfo { Ruleset = ruleset };
        importer.AttachReplay(scoreInfo, createOldPayload(4, true));

        Assert.That(importer.GetScore(scoreInfo).Replay, Is.Null);
        Assert.That(scoreInfo.Files.Count, Is.EqualTo(1), "Rejecting playback must not delete the user's replay.");

        var unmarkedScoreInfo = new ScoreInfo { Ruleset = ruleset };
        importer.AttachReplay(unmarkedScoreInfo, Encoding.UTF8.GetBytes(
            """
            { "version": 5, "beatmap_hash": "difficulty-hash", "frames": [{ "time": 100, "actions": [0] }] }
            """));

        Assert.That(importer.GetScore(unmarkedScoreInfo).Replay, Is.Null);
        Assert.That(unmarkedScoreInfo.Files.Count, Is.EqualTo(1), "Rejecting an unmarked replay must not delete its stored file.");

        var currentScore = new Score
        {
            ScoreInfo = new ScoreInfo { Ruleset = ruleset },
            Replay = new Replay { Frames = [new O2JamReplayFrame(100, ManiaAction.Key1)] },
        };
        importer.AttachReplay(currentScore.ScoreInfo, O2JamReplayArchive.Create(currentScore));
        Assert.That(importer.GetScore(currentScore.ScoreInfo).Replay.Frames, Has.Count.EqualTo(1));
    }

    [Test]
    public void OtherRulesetsStillUseTheirOriginalScoreLoader()
    {
        using var storage = new TemporaryNativeStorage($"{nameof(O2JamReplayImportTest)}-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var mania = new ManiaRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(new O2LazerRuleset().RulesetInfo, mania);
        var importer = new TestScoreImporter(rulesets, storage, realm);

        Assert.That(importer.GetScore(new ScoreInfo { Ruleset = mania }), Is.TypeOf<LegacyDatabasedScore>());
    }

    [TestCase("{\"version\":3,\"frames\":[{\"time\":1,\"actions\":[0]}],\"gauge_history\":[]}")]
    [TestCase("{\"version\":5,\"beatmap_hash\":\"foreign-chart\",\"frames\":[{\"time\":1,\"actions\":[0]}]}")]
    [TestCase("{\"version\":5,\"ruleset\":\"bms\",\"beatmap_hash\":\"foreign-chart\",\"frames\":[{\"time\":1,\"actions\":[0]}]}")]
    [TestCase("native-osr-header")]
    public void ForeignImportFormatsAreNotClaimedByO2Jam(string payload)
    {
        using var archive = new ByteArrayArchiveReader(Encoding.UTF8.GetBytes(payload), "foreign.osr");
        var prefix = typeof(O2JamReplayPersistencePatch).GetMethod("readO2JamReplayMetadata", BindingFlags.Static | BindingFlags.NonPublic)!;
        object?[] arguments = [null, archive, null];

        Assert.That(prefix.Invoke(null, arguments), Is.True);
        Assert.That(arguments[2], Is.Null);
    }

    private static byte[] createOldPayload(int version, bool compressed)
    {
        var bytes = Encoding.UTF8.GetBytes($$"""
            { "version": {{version}}, "frames": [{ "time": 100, "actions": [0, 6] }], "total_score": 123 }
            """);
        if (!compressed)
            return bytes;

        using var output = new MemoryStream();
        using (var gzip = new GZipStream(output, CompressionLevel.Optimal, leaveOpen: true))
            gzip.Write(bytes);
        return output.ToArray();
    }

    private sealed class TestRulesetStore(params RulesetInfo[] rulesets) : RulesetStore
    {
        public override IEnumerable<RulesetInfo> AvailableRulesets => rulesets;
    }

    private sealed class TestScoreImporter(RulesetStore rulesets, Storage storage, RealmAccess realm)
        : ScoreImporter(rulesets, () => null!, storage, realm, null!)
    {
        public ScoreInfo? ReadHeaders(ArchiveReader archive) => CreateModel(archive, default);

        public void AttachReplay(ScoreInfo score, byte[] bytes)
        {
            using var stream = new MemoryStream(bytes);
            var file = Realm.Run(database => Files.Add(stream, database, addToRealm: false));
            score.Files.Add(new RealmNamedFileUsage(file, "replay.osr"));
        }
    }
}
