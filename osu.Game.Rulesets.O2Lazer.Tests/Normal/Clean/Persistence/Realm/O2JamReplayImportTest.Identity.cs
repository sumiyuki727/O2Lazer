using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.IO.Archives;
using osu.Game.Models;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Rulesets.Scoring;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamReplayImportTest
{
    [TestCase(ScoreRank.F, false)]
    [TestCase(ScoreRank.A, true)]
    public void FormalReplayPreservesGradeAndRecordedDataAcrossRealmReload(ScoreRank rank, bool passed)
    {
        using var storage = new TemporaryNativeStorage($"replay-grade-{Guid.NewGuid():N}");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        var source = OjnTestData.CreateChart();
        var sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)).ToLowerInvariant();
        var scoreId = Guid.Empty;

        using (var realm = new RealmAccess(storage, "client.realm"))
        {
            var beatmap = realm.Write(database =>
            {
                var owned = database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID));
                var set = new BeatmapSetInfo { Hash = sourceHash };
                using var stream = new MemoryStream(source);
                var file = new RealmFileStore(realm, storage).Add(stream, database, preferHardLinks: false);
                set.Files.Add(new RealmNamedFileUsage(file, "source.ojn"));
                var chart = new BeatmapInfo(owned)
                {
                    BeatmapSet = set,
                    Hash = O2JamBeatmapIdentity.FromSource(sourceHash, O2JamDifficulty.EX),
                    MD5Hash = O2JamBeatmapIdentity.Md5FromSource(source, O2JamDifficulty.EX),
                };
                set.Beatmaps.Add(chart);
                database.Add(set);
                return chart.Detach();
            });
            var score = new Score
            {
                ScoreInfo = new ScoreInfo(beatmap, ruleset, new RealmUser { Username = "EX player" })
                {
                    TotalScore = 10670,
                    TotalScoreWithoutMods = 10670,
                    Accuracy = 52d / 72,
                    MaxCombo = 51,
                    Rank = rank,
                    Passed = passed,
                    Statistics = new() { [HitResult.Perfect] = 52, [HitResult.Miss] = 20 },
                },
                Replay = new Replay { Frames = [new O2JamReplayFrame(100, ManiaAction.Key1)] },
            };
            var bytes = O2JamReplayArchive.Create(score);
            var importer = new TestScoreImporter(rulesets, storage, realm);
            using var archive = new ByteArrayArchiveReader(bytes, "zero-life.osr");
            var headers = importer.ReadHeaders(archive);
            Assert.That(headers, Is.Not.Null);
            Assert.That(headers!.Passed, Is.EqualTo(passed));
            scoreId = importer.StoreReplay(headers, bytes);
        }

        using var reopened = new RealmAccess(storage, "client.realm");
        var stored = reopened.Run(database => database.Find<ScoreInfo>(scoreId)!.Detach());
        var replay = new TestScoreImporter(rulesets, storage, reopened).GetScore(stored);
        Assert.Multiple(() =>
        {
            Assert.That(reopened.Run(database => database.All<ScoreInfo>().Count()), Is.EqualTo(1));
            Assert.That(stored.Rank, Is.EqualTo(rank));
            Assert.That(stored.TotalScore, Is.EqualTo(10670));
            Assert.That(stored.MaxCombo, Is.EqualTo(51));
            Assert.That(stored.Statistics[HitResult.Perfect], Is.EqualTo(52));
            Assert.That(stored.Statistics[HitResult.Miss], Is.EqualTo(20));
            Assert.That(replay.ScoreInfo.Passed, Is.EqualTo(passed));
            Assert.That(replay.Replay.Frames, Has.Count.EqualTo(1));
        });
    }

    [TestCase("canonical", true)]
    [TestCase("hash-only", true)]
    [TestCase("md5-only", true)]
    [TestCase("legacy-shared", true)]
    [TestCase("legacy-records", true)]
    [TestCase("foreign-collision", true)]
    [TestCase("hash-md5-conflict", false)]
    [TestCase("arbitrary-hash", false)]
    [TestCase("shared-hash-only", false)]
    [TestCase("duplicate-md5", false)]
    [TestCase("duplicate-hash", false)]
    [TestCase("foreign-only", false)]
    [TestCase("deleted", false)]
    public void FormalArchiveImportRequiresUniqueConsistentO2LazerIdentity(string scenario, bool accepted)
    {
        using var storage = new TemporaryNativeStorage($"replay-identity-{Guid.NewGuid():N}");
        using var realm = new RealmAccess(storage, "client.realm");
        var ruleset = new O2LazerRuleset().RulesetInfo;
        using var rulesets = new TestRulesetStore(ruleset, new ManiaRuleset().RulesetInfo);
        Assert.That(O2JamReplayPersistencePatch.InstallOnce(), Is.True);
        var importer = new TestScoreImporter(rulesets, storage, realm);
        var source = OjnTestData.CreateChart();
        var sourceHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(source)).ToLowerInvariant();
        var targetId = Guid.Empty;
        var hash = O2JamBeatmapIdentity.FromSource(sourceHash, O2JamDifficulty.NX);
        var md5 = O2JamBeatmapIdentity.Md5FromSource(source, O2JamDifficulty.NX);
        BeatmapInfo? target = null;
        realm.Write(database =>
        {
            var owned = database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID));
            var set = new BeatmapSetInfo { Hash = "set" };
            using var stream = new MemoryStream(source);
            var file = new RealmFileStore(realm, storage).Add(stream, database, preferHardLinks: false);
            set.Files.Add(new RealmNamedFileUsage(file, "source.ojn"));
            foreach (var difficulty in new[] { O2JamDifficulty.EX, O2JamDifficulty.NX, O2JamDifficulty.HX })
                set.Beatmaps.Add(new BeatmapInfo(owned)
                {
                    BeatmapSet = set,
                    Hash = O2JamBeatmapIdentity.FromSource(sourceHash, difficulty),
                    MD5Hash = O2JamBeatmapIdentity.Md5FromSource(source, difficulty),
                });
            database.Add(set);
            var nx = set.Beatmaps[1];
            targetId = nx.ID;
            target = nx.Detach();
            switch (scenario)
            {
                case "hash-only": md5 = string.Empty; break;
                case "md5-only": hash = string.Empty; break;
                case "legacy-shared": hash = sourceHash; break;
                case "legacy-records":
                case "shared-hash-only":
                    hash = sourceHash;
                    foreach (var beatmap in set.Beatmaps)
                        beatmap.Hash = sourceHash;
                    if (scenario == "shared-hash-only")
                        md5 = string.Empty;
                    break;
                case "hash-md5-conflict": hash = set.Beatmaps[0].Hash; break;
                case "arbitrary-hash": hash = "unrelated"; break;
                case "duplicate-md5": set.Beatmaps[0].MD5Hash = md5; break;
                case "duplicate-hash": set.Beatmaps[0].Hash = hash; md5 = string.Empty; break;
                case "foreign-collision":
                case "foreign-only":
                    var foreign = database.Add(new RulesetInfo("bms", "BMS", "test", -1));
                    var foreignSet = new BeatmapSetInfo { Hash = "foreign-set" };
                    foreignSet.Beatmaps.Add(new BeatmapInfo(foreign) { Hash = hash, MD5Hash = md5, BeatmapSet = foreignSet });
                    database.Add(foreignSet);
                    if (scenario == "foreign-only")
                        set.DeletePending = true;
                    break;
                case "deleted": set.DeletePending = true; break;
            }
        });
        target!.Hash = hash;
        target.MD5Hash = md5;
        var score = new Score
        {
            ScoreInfo = new ScoreInfo(target, ruleset, new RealmUser { Username = "Archive player" })
            {
                BeatmapHash = hash,
                TotalScore = 123456,
                Accuracy = 0.9,
                MaxCombo = 42,
                Rank = ScoreRank.A,
            },
            Replay = new Replay { Frames = [new O2JamReplayFrame(100, ManiaAction.Key1)] },
        };
        var bytes = O2JamReplayArchive.Create(score);
        using var archive = new ByteArrayArchiveReader(bytes, "formal.osr");
        var restored = importer.ReadHeaders(archive);
        if (accepted)
        {
            Assert.That(restored, Is.Not.Null);
            Assert.That(restored!.BeatmapInfo!.ID, Is.EqualTo(targetId));
            Assert.That(restored.BeatmapHash, Is.EqualTo(restored.BeatmapInfo.Hash));
            Assert.That(restored.TotalScore, Is.EqualTo(123456));
            Assert.That(restored.MaxCombo, Is.EqualTo(42));
            Assert.That(O2JamReplayArchive.TryReadScore(restored, bytes, out var replay), Is.True);
            Assert.That(replay.Replay.Frames, Has.Count.EqualTo(1));
        }
        else
            Assert.That(restored, Is.Null);
        Assert.That(realm.Run(database => database.All<ScoreInfo>().Count()), Is.Zero,
            "Rejected headers cannot create a score or attach it to a foreign difficulty.");
    }
}
