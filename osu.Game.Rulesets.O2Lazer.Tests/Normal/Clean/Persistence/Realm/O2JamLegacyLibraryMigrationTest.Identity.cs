using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Replays;
using osu.Game.Rulesets;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Replays;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void ThreeLegacySlotsMoveAndMigrateScoresWithoutChangingReplayFiles() => runProjectionTest((realm, storage) =>
    {
        var oldPath = storage.GetFullPath("old-name.ojn");
        var newPath = storage.GetFullPath("renamed.ojn");
        File.WriteAllBytes(oldPath, createThreeSlotSource());
        var planner = new O2JamImportPlanner();
        var plan = planner.Create(oldPath);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var original = realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => beatmap.Detach()).ToArray());
        var setId = original[0].BeatmapSet!.ID;
        var dateAdded = original[0].BeatmapSet!.DateAdded;
        var scores = new Dictionary<Guid, (Guid ScoreId, byte[] Replay)>();
        var unlinkedId = Guid.NewGuid();
        var fileStore = new RealmFileStore(realm, storage);
        realm.Write(database =>
        {
            var set = database.Find<BeatmapSetInfo>(setId)!;
            set.Hash = string.Concat(plan.Charts.Select(chart => chart.Md5Hash).Order(StringComparer.Ordinal));
            foreach (var beatmap in set.Beatmaps)
            {
                beatmap.Hash = plan.SourceHash;
                beatmap.Metadata.Tags = "o2jam keep-user-tag";
                beatmap.UserSettings.Offset = 12.5;
                beatmap.Hidden = true;
                beatmap.BeatDivisor = 8;
                var score = new ScoreInfo(beatmap, beatmap.Ruleset, new RealmUser { Username = "Player" })
                {
                    TotalScore = 10000 + scores.Count,
                    MaxCombo = 123,
                    Accuracy = 0.95,
                    Rank = ScoreRank.A,
                    Date = new DateTimeOffset(2026, 9, 1, 1, 2, 3, TimeSpan.Zero),
                };
                var replay = O2JamReplayArchive.Create(new Score
                {
                    ScoreInfo = score,
                    Replay = new Replay { Frames = [new O2JamReplayFrame(100, ManiaAction.Key1)] },
                });
                using var stream = new MemoryStream(replay);
                score.Files.Add(new RealmNamedFileUsage(fileStore.Add(stream, database, preferHardLinks: false), "replay.osr"));
                database.Add(score);
                scores.Add(beatmap.ID, (score.ID, replay));
            }
            database.Add(new ScoreInfo { ID = unlinkedId, Ruleset = set.Beatmaps[0].Ruleset, BeatmapInfo = null, BeatmapHash = plan.SourceHash, TotalScore = 42 });
        });

        var sources = writer.GetImportedSources();
        File.Move(oldPath, newPath);
        var summary = new O2JamImportService(planner, writer).Refresh([newPath], sources);
        Assert.That(summary.Updated, Is.EqualTo(1));
        Assert.That(summary.Imported + summary.Failed, Is.Zero);
        realm.Run(database =>
        {
            var set = database.Find<BeatmapSetInfo>(setId)!;
            Assert.That(set.DeletePending, Is.False, "The old scan index must not delete an in-place moved set.");
            Assert.That(set.DateAdded, Is.EqualTo(dateAdded));
            Assert.That(set.Hash, Is.EqualTo(plan.SetHash));
            Assert.That(set.Beatmaps.Select(beatmap => beatmap.ID), Is.EquivalentTo(original.Select(beatmap => beatmap.ID)));
            foreach (var beatmap in set.Beatmaps)
            {
                var previous = original.Single(candidate => candidate.ID == beatmap.ID);
                var score = database.Find<ScoreInfo>(scores[beatmap.ID].ScoreId)!;
                Assert.Multiple(() =>
                {
                    Assert.That(beatmap.Hash, Is.EqualTo(previous.Hash));
                    Assert.That(beatmap.MD5Hash, Is.EqualTo(previous.MD5Hash));
                    Assert.That(beatmap.Metadata.AudioFile, Is.EqualTo("renamed.ojn"));
                    Assert.That(beatmap.Metadata.Tags, Does.Contain("keep-user-tag"));
                    Assert.That(beatmap.UserSettings.Offset, Is.EqualTo(12.5));
                    Assert.That(beatmap.Hidden, Is.True);
                    Assert.That(beatmap.BeatDivisor, Is.EqualTo(8));
                    Assert.That(score.BeatmapInfo!.ID, Is.EqualTo(beatmap.ID));
                    Assert.That(score.BeatmapHash, Is.EqualTo(beatmap.Hash));
                    Assert.That(score.TotalScore, Is.InRange(10000, 10002));
                    Assert.That(score.MaxCombo, Is.EqualTo(123));
                    Assert.That(score.Accuracy, Is.EqualTo(0.95));
                    Assert.That(score.Rank, Is.EqualTo(ScoreRank.A));
                    Assert.That(score.Date, Is.EqualTo(new DateTimeOffset(2026, 9, 1, 1, 2, 3, TimeSpan.Zero)));
                });
                using var stream = fileStore.Store.GetStream(score.Files.Single().File.GetStoragePath());
                using var buffer = new MemoryStream();
                stream!.CopyTo(buffer);
                Assert.That(buffer.ToArray(), Is.EqualTo(scores[beatmap.ID].Replay));
                Assert.That(O2JamReplayBeatmapResolver.Resolve(database, plan.SourceHash, beatmap.MD5Hash)?.ID, Is.EqualTo(beatmap.ID));
            }
            var unlinked = database.Find<ScoreInfo>(unlinkedId)!;
            Assert.That(unlinked.BeatmapInfo, Is.Null);
            Assert.That(unlinked.BeatmapHash, Is.EqualTo(plan.SourceHash));
            Assert.That(unlinked.TotalScore, Is.EqualTo(42));
        });
        Assert.That(writer.GetImportedSources()[newPath].HasCurrentMetadata, Is.True);
        Assert.That(writer.Write(planner.Create(newPath)), Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
    });

    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    public void CollectionMd5MigrationRequiresAGloballyUniqueOldOwner(int collision) => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("collections.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        const string oldMd5 = "legacy-md5";
        realm.Write(database =>
        {
            var set = database.All<BeatmapSetInfo>().Single();
            var ex = set.Beatmaps.Single(beatmap => beatmap.MD5Hash == plan.Charts[0].Md5Hash);
            ex.MD5Hash = oldMd5;
            ex.Hash = plan.SourceHash;
            ex.Metadata.Tags = "o2jam";
            if (collision is 1 or 3 or 4)
            {
                var foreign = database.Add(new RulesetInfo("bms", "BMS", "test", -1));
                var beatmap = new BeatmapInfo(foreign) { MD5Hash = collision == 3 ? oldMd5.ToUpperInvariant() : oldMd5, Hash = "foreign" };
                if (collision == 4)
                {
                    var pending = new BeatmapSetInfo { DeletePending = true };
                    beatmap.BeatmapSet = pending;
                    pending.Beatmaps.Add(beatmap);
                    database.Add(pending);
                }
                else
                    database.Add(beatmap);
            }
            if (collision == 2)
            {
                var nx = set.Beatmaps.Single(beatmap => beatmap.MD5Hash == plan.Charts[1].Md5Hash);
                nx.MD5Hash = oldMd5;
                nx.Hash = plan.SourceHash;
                nx.Metadata.Tags = "o2jam";
            }
            var collection = new BeatmapCollection { Name = "User collection" };
            collection.BeatmapMD5Hashes.Add(oldMd5);
            collection.BeatmapMD5Hashes.Add("unrelated");
            database.Add(collection);
        });
        Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.Updated));
        realm.Run(database =>
        {
            Assert.That(database.All<BeatmapCollection>().Single().BeatmapMD5Hashes,
                Is.EquivalentTo(new[] { collision == 0 ? plan.Charts[0].Md5Hash : oldMd5, "unrelated" }));
            Assert.That(database.All<BeatmapInfo>().AsEnumerable().Count(beatmap => string.Equals(beatmap.MD5Hash, oldMd5, StringComparison.OrdinalIgnoreCase)), Is.EqualTo(collision is 1 or 3 or 4 ? 1 : 0));
        });
    });

    [Test]
    public void ProvenEmptyLegacySlotRetainsScoreAndMissingPlayableSlotGetsANewId() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("slots.ojn");
        File.WriteAllBytes(path, createThreeSlotSource(false));
        var plan = new O2JamImportPlanner().Create(path);
        Assert.That(plan.Slots.Single(slot => slot.Difficulty == O2JamDifficulty.NX).IsPlayable, Is.False);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var removedPlayableId = Guid.Empty;
        var emptyId = Guid.NewGuid();
        var scoreId = Guid.NewGuid();
        realm.Write(database =>
        {
            var set = database.All<BeatmapSetInfo>().Single();
            var hx = set.Beatmaps.Single(beatmap => beatmap.MD5Hash == plan.Charts.Single(chart => chart.Difficulty == O2JamDifficulty.HX).Md5Hash);
            removedPlayableId = hx.ID;
            set.Beatmaps.Remove(hx);
            database.Remove(hx.Metadata);
            database.Remove(hx);
            var empty = new BeatmapInfo(set.Beatmaps[0].Ruleset)
            {
                ID = emptyId, BeatmapSet = set, Hash = plan.SourceHash,
                MD5Hash = plan.Slots.Single(slot => slot.Difficulty == O2JamDifficulty.NX).Md5Hash,
                DifficultyName = "NX Lv.10",
            };
            set.Beatmaps.Add(empty);
            database.Add(new ScoreInfo(empty, empty.Ruleset, new RealmUser { Username = "Historical" }) { ID = scoreId, TotalScore = 9876 });
        });
        Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.Updated));
        realm.Run(database =>
        {
            Assert.That(database.Find<BeatmapInfo>(emptyId), Is.Null);
            var score = database.Find<ScoreInfo>(scoreId)!;
            Assert.That(score.BeatmapInfo, Is.Null);
            Assert.That(score.BeatmapHash, Is.EqualTo(plan.SourceHash));
            Assert.That(score.TotalScore, Is.EqualTo(9876));
            var set = database.All<BeatmapSetInfo>().Single();
            Assert.That(set.Beatmaps, Has.Count.EqualTo(2));
            Assert.That(set.DeletePending, Is.False);
            Assert.That(set.Beatmaps.Any(beatmap => beatmap.ID == removedPlayableId), Is.False);
        });
        Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
    });

    [TestCase("duplicate-slot")]
    [TestCase("hash-md5-conflict")]
    [TestCase("unknown-slot")]
    [TestCase("mixed-owner")]
    [TestCase("duplicate-set")]
    public void UnprovenOrConflictingIdentityCannotPartiallyMigrate(string conflict) => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("conflict.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        realm.Write(database =>
        {
            var set = database.All<BeatmapSetInfo>().Single();
            foreach (var beatmap in set.Beatmaps)
            {
                beatmap.Hash = plan.SourceHash;
                beatmap.Metadata.Tags = "o2jam";
            }
            var ex = set.Beatmaps[0];
            var nx = set.Beatmaps[1];
            switch (conflict)
            {
                case "duplicate-slot": nx.MD5Hash = ex.MD5Hash; nx.DifficultyName = ex.DifficultyName; break;
                case "hash-md5-conflict": ex.Hash = O2JamBeatmapIdentity.FromSource(plan.SourceHash, O2JamDifficulty.NX); break;
                case "unknown-slot": ex.MD5Hash = "unknown"; ex.DifficultyName = "User label"; break;
                case "mixed-owner": nx.Ruleset = database.Add(new RulesetInfo("bms", "BMS", "test", -1)); break;
                case "duplicate-set":
                    var duplicate = new BeatmapSetInfo { Hash = "old-duplicate" };
                    duplicate.Files.Add(new RealmNamedFileUsage(set.Files[0].File, plan.FileName));
                    duplicate.Beatmaps.Add(new BeatmapInfo(ex.Ruleset) { Hash = plan.SourceHash, DifficultyName = "EX Lv.5", BeatmapSet = duplicate });
                    database.Add(duplicate);
                    break;
            }
        });
        var before = realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => (beatmap.ID, beatmap.Hash, beatmap.MD5Hash, beatmap.Metadata.Tags)).ToArray());
        var notifications = 0;
        writer.BeatmapUpdated += _ => notifications++;
        Assert.Throws<InvalidDataException>(() => writer.Write(plan));
        realm.Run(database =>
        {
            Assert.That(database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => (beatmap.ID, beatmap.Hash, beatmap.MD5Hash, beatmap.Metadata.Tags)), Is.EquivalentTo(before));
            Assert.That(database.All<BeatmapSetInfo>().Any(set => set.DeletePending), Is.False);
        });
        Assert.That(notifications, Is.Zero);
    });

    [Test]
    public void MissingCleanupRequiresAReadableParentAndASuccessfulRefresh() => runProjectionTest((realm, storage) =>
    {
        var directory = storage.GetFullPath("source-parent");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "chart.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var writer = new O2JamLibraryWriter(realm, storage);
        var planner = new O2JamImportPlanner();
        writer.Write(planner.Create(path));
        var sources = writer.GetImportedSources();
        File.Delete(path);
        var invalid = storage.GetFullPath("invalid.ojn");
        File.WriteAllBytes(invalid, [1, 2, 3]);
        Assert.That(new O2JamImportService(planner, writer).Refresh([invalid], sources).Failed, Is.EqualTo(1));
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Single().DeletePending), Is.False);
        var unavailableParent = storage.GetFullPath("parent-file");
        File.WriteAllBytes(unavailableParent, [0]);
        realm.Write(database => database.All<BeatmapInfo>().Single().Metadata.Source = unavailableParent);
        Assert.That(writer.MarkDeleted(sources.Values.Select(source => source.SetId)), Is.Zero, "An existing but untraversable parent cannot prove absence.");
        realm.Write(database => database.All<BeatmapInfo>().Single().Metadata.Source = directory);
        Directory.Delete(directory);
        Assert.That(writer.MarkDeleted(sources.Values.Select(source => source.SetId)), Is.EqualTo(1), "A readable ancestor can prove that the whole source directory disappeared.");
    });

    [Test]
    public void RefreshRepairsMissingPlayableSlotsInAnOtherwiseCurrentSet() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("incomplete.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var planner = new O2JamImportPlanner();
        var plan = planner.Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        realm.Write(database =>
        {
            var set = database.All<BeatmapSetInfo>().Single();
            var missing = set.Beatmaps.Single(beatmap => beatmap.MD5Hash == plan.Charts[2].Md5Hash);
            set.Beatmaps.Remove(missing);
            database.Remove(missing.Metadata);
            database.Remove(missing);
        });
        var retainedIds = realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => beatmap.ID).ToArray());
        var sources = writer.GetImportedSources();
        Assert.That(sources[path].HasCurrentMetadata, Is.False, "A set hash also commits its complete playable membership.");
        var summary = new O2JamImportService(planner, writer).Refresh([path], sources);
        Assert.That(summary.Updated, Is.EqualTo(1));
        realm.Run(database =>
        {
            Assert.That(database.All<BeatmapInfo>().Count(), Is.EqualTo(3));
            Assert.That(database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => beatmap.ID), Is.SupersetOf(retainedIds));
        });
        Assert.That(new O2JamImportService(planner, writer).Refresh([path], writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(1));
    });
    private static byte[] createThreeSlotSource(bool playableNx = true)
    {
        var original = OjnTestData.CreateChart();
        var bytes = new byte[516];
        Array.Copy(original, bytes, 300);
        for (var slot = 0; slot < 3; slot++)
        {
            Array.Copy(original, 300, bytes, 300 + slot * 72, 72);
            BitConverter.GetBytes((uint)(slot == 1 && !playableNx ? 0 : 4)).CopyTo(bytes, 64 + slot * 4);
            BitConverter.GetBytes((uint)(300 + slot * 72)).CopyTo(bytes, 284 + slot * 4);
        }
        BitConverter.GetBytes((uint)516).CopyTo(bytes, 296);
        return bytes;
    }
}
