using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase(nameof(O2JamLibraryWriteStage.FilesReserved), -1)]
    [TestCase(nameof(O2JamLibraryWriteStage.RequestWritten), 0)]
    [TestCase(nameof(O2JamLibraryWriteStage.RequestWritten), 1)]
    [TestCase(nameof(O2JamLibraryWriteStage.BeforeCommit), -1)]
    public void FailedModelBatchRetainsReservationsAndRetriesIdempotently(string failureStageName, int failureIndex)
        => runProjectionTest((realm, storage) =>
        {
            var failureStage = Enum.Parse<O2JamLibraryWriteStage>(failureStageName);
            var saved = seedRecoveryLibrary(realm, storage);
            var requests = replacementRequests(storage, saved.Plan.SourcePath);
            var hashes = recoveryHashes(requests);
            var inject = true;
            var notices = 0;
            var writer = new O2JamLibraryWriter(realm, storage, (stage, index) =>
            {
                if (inject && stage == failureStage && index == failureIndex)
                    throw new IOException("Injected model transaction failure.");
            });
            writer.BeatmapUpdated += _ => notices++;
            Assert.Throws<IOException>(() => writer.WriteBatch(requests));
            assertSavedLibrary(realm, storage, saved);
            assertReservations(realm, hashes);
            Assert.That(notices, Is.Zero);
            Assert.That(hashes.Count(hash => storedFileExists(storage, hash)),
                Is.EqualTo(failureStage == O2JamLibraryWriteStage.FilesReserved ? 0 : failureIndex == 0 ? 2 : 4));

            inject = false;
            Assert.That(writer.WriteBatch(requests), Is.All.EqualTo(O2JamLibraryWriteResult.Imported));
            var importedIds = writer.GetImportedSources().Values.Select(source => source.SetId).ToArray();
            Assert.That(importedIds, Has.Length.EqualTo(2));
            Assert.That(writer.WriteBatch(requests), Is.All.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
            Assert.That(writer.GetImportedSources().Values.Select(source => source.SetId), Is.EquivalentTo(importedIds));
            Assert.That(realm.Run(database => database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapHash), Is.EqualTo(saved.BeatmapHash));
            Assert.That(realm.Run(database => database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapInfo!.ID), Is.EqualTo(saved.BeatmapId));
            Assert.That(realm.Run(database => hashes.All(hash => database.Find<RealmFile>(hash)!.Usages.Count() == 1)), Is.True);
        });

    [TestCase(nameof(O2JamLibraryWriteStage.FilesReserved))]
    [TestCase(nameof(O2JamLibraryWriteStage.RequestWritten))]
    [TestCase(nameof(O2JamLibraryWriteStage.BeforeCommit))]
    public void CancellationRollsBackOnlyModelsAndLeavesTrackableFiles(string cancellationStageName)
        => runProjectionTest((realm, storage) =>
        {
            var cancellationStage = Enum.Parse<O2JamLibraryWriteStage>(cancellationStageName);
            var saved = seedRecoveryLibrary(realm, storage);
            var requests = replacementRequests(storage, saved.Plan.SourcePath);
            using var cancellation = new CancellationTokenSource();
            var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
            {
                if (stage == cancellationStage)
                    cancellation.Cancel();
            });
            var notices = 0;
            writer.BeatmapUpdated += _ => notices++;
            Assert.Throws<OperationCanceledException>(() => writer.WriteBatch(requests, cancellation.Token));
            assertSavedLibrary(realm, storage, saved);
            assertReservations(realm, recoveryHashes(requests));
            Assert.That(notices, Is.Zero);
            new RealmFileStore(realm, storage).Cleanup();
            Assert.That(realm.Run(database => recoveryHashes(requests).All(hash => database.Find<RealmFile>(hash) == null)), Is.True);
            assertSavedLibrary(realm, storage, saved);
        });

    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void NativeDiskWriteFailureKeepsRowAndRepairsOnRetry(bool failBackground, bool corruptFile) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var request = replacementRequests(storage, saved.Plan.SourcePath)[0];
        var hashes = recoveryHashes([request]);
        var fault = new FileWriteFailure { Hash = failBackground ? hashes[1] : hashes[0] };
        var writer = new O2JamLibraryWriter(realm, new FileWriteFaultStorage(storage, fault));
        Assert.Throws<IOException>(() => writer.WriteBatch([request]));
        assertSavedLibrary(realm, storage, saved);
        assertReservations(realm, hashes);
        Assert.That(storedFileExists(storage, fault.Hash!), Is.True);
        var sourceMoves = fault.Moves.GetValueOrDefault(hashes[0]);
        if (corruptFile)
            File.WriteAllBytes(storage.GetStorageForDirectory("files").GetFullPath(new RealmFile { Hash = fault.Hash! }.GetStoragePath()), [1, 2, 3]);
        fault.Hash = null;
        Assert.That(writer.WriteBatch([request])[0], Is.EqualTo(O2JamLibraryWriteResult.Imported));
        Assert.That(fault.Moves.GetValueOrDefault(hashes[0]), Is.EqualTo(sourceMoves + (corruptFile && !failBackground ? 1 : 0)),
            "Retry must reuse verified files and rewrite corrupted bytes through the native store.");
        foreach (var hash in hashes)
        {
            using var stream = new RealmFileStore(realm, storage).Store.GetStream(new RealmFile { Hash = hash }.GetStoragePath());
            Assert.That(stream.ComputeSHA2Hash(), Is.EqualTo(hash));
        }
        Assert.That(writer.WriteBatch([request])[0], Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
        Assert.That(realm.Run(database => hashes.All(hash => database.Find<RealmFile>(hash)!.Usages.Count() == 1)), Is.True);
    });

    [Test]
    public void NativeStartupReclaimsFailedReservationsButPreservesScoresAndReplay() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var requests = replacementRequests(storage, saved.Plan.SourcePath);
        var hashes = recoveryHashes(requests);
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (stage == O2JamLibraryWriteStage.BeforeCommit)
                throw new IOException("Injected failure before commit.");
        });
        Assert.Throws<IOException>(() => writer.WriteBatch(requests));
        Assert.That(hashes.All(hash => storedFileExists(storage, hash)), Is.True);
        realm.Dispose();
        using var restarted = new RealmAccess(storage, "client.realm");
        assertSavedLibrary(restarted, storage, saved);
        Assert.That(restarted.Run(database => hashes.All(hash => database.Find<RealmFile>(hash) == null)), Is.True);
        Assert.That(hashes.Any(hash => storedFileExists(storage, hash)), Is.False);
        Assert.That(File.Exists(saved.Plan.SourcePath), Is.True);
        var retry = new O2JamLibraryWriter(restarted, storage);
        Assert.That(retry.WriteBatch(requests), Is.All.EqualTo(O2JamLibraryWriteResult.Imported));
        Assert.That(retry.WriteBatch(requests), Is.All.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
    });

    [Test]
    public void RemovedReservationCannotBeRecreatedDuringModelWrite() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var requests = replacementRequests(storage, saved.Plan.SourcePath);
        var hashes = recoveryHashes(requests);
        var cleanup = true;
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (cleanup && stage == O2JamLibraryWriteStage.FilesReserved)
                new RealmFileStore(realm, storage).Cleanup();
        });
        Assert.Throws<IOException>(() => writer.WriteBatch(requests));
        assertSavedLibrary(realm, storage, saved);
        Assert.That(realm.Run(database => hashes.Any(hash => database.Find<RealmFile>(hash) != null)), Is.False);
        Assert.That(hashes.Any(hash => storedFileExists(storage, hash)), Is.False);
        cleanup = false;
        Assert.That(writer.WriteBatch(requests), Is.All.EqualTo(O2JamLibraryWriteResult.Imported));
    });

    [Test]
    public void FailedImportAndCleanupPreserveOtherRulesetFileReferences() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var request = replacementRequests(storage, saved.Plan.SourcePath)[0];
        var hashes = recoveryHashes([request]);
        var fileStore = new RealmFileStore(realm, storage);
        var foreignId = realm.Write(database =>
        {
            var ruleset = database.Add(new RulesetInfo("bms", "BMS", "BMS reference", -1) { Available = true });
            var set = new BeatmapSetInfo { Hash = "foreign-file-owner" };
            set.Beatmaps.Add(new BeatmapInfo { Ruleset = ruleset, BeatmapSet = set, Hash = "foreign-chart", MD5Hash = "foreign-md5" });
            using var source = new MemoryStream(request.Plan.SourceData, writable: false);
            using var background = new MemoryStream(request.Plan.Background, writable: false);
            set.Files.Add(new RealmNamedFileUsage(fileStore.Add(source, database), "foreign-source.ojn"));
            set.Files.Add(new RealmNamedFileUsage(fileStore.Add(background, database), "foreign-background.png"));
            database.Add(set);
            return set.ID;
        });
        var fail = true;
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (fail && stage == O2JamLibraryWriteStage.BeforeCommit)
                throw new IOException("Injected failure for a shared file.");
        });
        Assert.Throws<IOException>(() => writer.WriteBatch([request]));
        fileStore.Cleanup();
        Assert.That(realm.Run(database => hashes.All(hash => database.Find<RealmFile>(hash)!.Usages.Count() == 1)), Is.True);
        Assert.That(realm.Run(database => database.Find<BeatmapSetInfo>(foreignId)!.DeletePending), Is.False);
        Assert.That(realm.Run(database => database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapInfo!.ID), Is.EqualTo(saved.BeatmapId));
        Assert.That(hashes.All(hash => storedFileExists(storage, hash)), Is.True);
        fail = false;
        Assert.That(writer.WriteBatch([request])[0], Is.EqualTo(O2JamLibraryWriteResult.Imported));
        Assert.That(realm.Run(database => hashes.All(hash => database.Find<RealmFile>(hash)!.Usages.Count() == 2)), Is.True);
        Assert.That(realm.Run(database => database.Find<BeatmapSetInfo>(foreignId)!.Files.Select(file => file.File.Hash).ToArray()), Is.EquivalentTo(hashes));
    });

    [Test]
    public void ReservationCancellationThroughRefreshIsNotReportedAsFailure() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("cancel-refresh.ojn");
        File.WriteAllBytes(path, recoverySource(200));
        using var cancellation = new CancellationTokenSource();
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (stage == O2JamLibraryWriteStage.FilesReserved)
                cancellation.Cancel();
        });
        var failures = 0;
        var invalidations = 0;
        var service = new O2JamImportService(new O2JamImportPlanner(), writer, _ => invalidations++);
        Assert.Throws<OperationCanceledException>(() => service.Refresh([path], writer.GetImportedSources(),
            failure: (_, _) => failures++, cancellationToken: cancellation.Token));
        Assert.That(failures, Is.Zero);
        Assert.That(invalidations, Is.Zero);
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.Zero);
        Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.EqualTo(2));
    });

    [Test]
    public void UnavailableRulesetAndPreCancelledWriteReserveNothing() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("unavailable.ojn");
        File.WriteAllBytes(path, recoverySource(200));
        var request = new O2JamLibraryWriteRequest(new O2JamImportPlanner().Create(path));
        var writer = new O2JamLibraryWriter(realm, storage);
        Assert.Throws<OperationCanceledException>(() => writer.WriteBatch([request], new CancellationToken(true)));
        realm.Write(database => database.Find<RulesetInfo>(O2LazerIdentity.ShortName)!.Available = false);
        Assert.That(writer.WriteBatch([request])[0], Is.EqualTo(O2JamLibraryWriteResult.RulesetUnavailable));
        Assert.That(realm.Run(database => database.All<RealmFile>().Count()), Is.Zero);
        Assert.That(recoveryHashes([request]).Any(hash => storedFileExists(storage, hash)), Is.False);
    });

    [Test]
    public void ReservedBytesAreIndependentOfCallerMutations() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("snapshot.ojn");
        File.WriteAllBytes(path, recoverySource(200));
        var plan = new O2JamImportPlanner().Create(path);
        var hashes = recoveryHashes([new O2JamLibraryWriteRequest(plan)]);
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (stage == O2JamLibraryWriteStage.FilesReserved)
            {
                plan.SourceData[0] ^= 1;
                plan.Background[0] ^= 1;
            }
        });
        Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.Imported));
        Assert.That(realm.Run(database => database.All<RealmFile>().AsEnumerable().Select(file => file.Hash).ToArray()), Is.EquivalentTo(hashes));
        var files = new RealmFileStore(realm, storage);
        foreach (var hash in hashes)
        {
            using var stream = files.Store.GetStream(new RealmFile { Hash = hash }.GetStoragePath());
            Assert.That(stream.ComputeSHA2Hash(), Is.EqualTo(hash));
        }
    });

    [Test]
    public void CancellationAfterLaterReservationRetainsEarlierCommitCounts() => runProjectionTest((realm, storage) =>
    {
        var paths = new List<string>();
        for (uint index = 200; index < 210; index++)
        {
            var path = storage.GetFullPath($"batch-{index}.ojn");
            File.WriteAllBytes(path, recoverySource(index));
            paths.Add(path);
        }
        using var cancellation = new CancellationTokenSource();
        var reservations = 0;
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (stage == O2JamLibraryWriteStage.FilesReserved && ++reservations == 2)
                cancellation.Cancel();
        });
        var failures = 0;
        var invalidations = 0;
        var service = new O2JamImportService(new O2JamImportPlanner(), writer, _ => invalidations++);
        var exception = Assert.Throws<O2JamImportCancelledException>(() => service.Refresh(paths, writer.GetImportedSources(),
            failure: (_, _) => failures++, cancellationToken: cancellation.Token))!;
        Assert.Multiple(() =>
        {
            Assert.That(exception.Summary.Imported, Is.EqualTo(8));
            Assert.That(exception.Summary.Failed, Is.Zero);
            Assert.That(failures, Is.Zero);
            Assert.That(invalidations, Is.EqualTo(8));
            Assert.That(writer.GetImportedSources(), Has.Count.EqualTo(8));
            Assert.That(realm.Run(database => database.All<RealmFile>().AsEnumerable().Count(file => file.Usages.Count() == 0)), Is.EqualTo(4));
        });
        var retry = service.Refresh(paths, writer.GetImportedSources());
        Assert.That(retry.Imported, Is.EqualTo(2));
        Assert.That(retry.AlreadyPresent, Is.EqualTo(8));
        Assert.That(writer.GetImportedSources(), Has.Count.EqualTo(10));
        Assert.That(realm.Run(database => database.All<RealmFile>().AsEnumerable().Any(file => file.Usages.Count() == 0)), Is.False);
    });
    private static byte[] recoverySource(uint songId)
    {
        var bytes = OjnTestData.CreateChart();
        byte[] image = [0x89, (byte)'P', (byte)'N', (byte)'G', 13, 10, 26, 10, (byte)songId];
        BitConverter.GetBytes(songId).CopyTo(bytes, 0);
        BitConverter.GetBytes((uint)image.Length).CopyTo(bytes, 268);
        return [.. bytes, .. image];
    }

    private static O2JamLibraryWriteRequest[] replacementRequests(Storage storage, string originalPath)
    {
        File.WriteAllBytes(originalPath, recoverySource(200));
        var secondPath = storage.GetFullPath("second.ojn");
        File.WriteAllBytes(secondPath, recoverySource(201));
        var planner = new O2JamImportPlanner();
        return [new O2JamLibraryWriteRequest(planner.Create(originalPath)), new O2JamLibraryWriteRequest(planner.Create(secondPath))];
    }

    private static string[] recoveryHashes(IEnumerable<O2JamLibraryWriteRequest> requests) => requests.SelectMany(request =>
    {
        using var stream = new MemoryStream(request.Plan.Background, writable: false);
        return new[] { request.Plan.SourceHash, stream.ComputeSHA2Hash() };
    }).Distinct().ToArray();

    private static bool storedFileExists(Storage storage, string hash) => storage.GetStorageForDirectory("files").Exists(new RealmFile { Hash = hash }.GetStoragePath());

    private static void assertReservations(RealmAccess realm, string[] hashes) => realm.Run(database =>
    {
        foreach (var hash in hashes)
        {
            var file = database.Find<RealmFile>(hash);
            Assert.That(file, Is.Not.Null);
            Assert.That(file!.Usages.Count(), Is.Zero);
        }
    });

    private static SavedRecoveryLibrary seedRecoveryLibrary(RealmAccess realm, Storage storage)
    {
        var path = storage.GetFullPath("original.ojn");
        File.WriteAllBytes(path, recoverySource(100));
        var plan = new O2JamImportPlanner().Create(path);
        new O2JamLibraryWriter(realm, storage).Write(plan);
        var files = new RealmFileStore(realm, storage);
        return realm.Write(database =>
        {
            var beatmap = database.All<BeatmapInfo>().Single();
            beatmap.UserSettings.Offset = 12.5;
            beatmap.Hidden = true;
            var score = new ScoreInfo(beatmap, beatmap.Ruleset) { TotalScore = 12345, MaxCombo = 123, BeatmapHash = beatmap.Hash };
            using var replay = new MemoryStream(recovery_replay, writable: false);
            var replayFile = files.Add(replay, database);
            score.Files.Add(new RealmNamedFileUsage(replayFile, "replay.osr"));
            database.Add(score);
            database.Add(new BeatmapCollection("User collection", [beatmap.MD5Hash]));
            return new SavedRecoveryLibrary(plan, beatmap.BeatmapSet!.ID, beatmap.ID, score.ID, beatmap.Hash, beatmap.MD5Hash,
                beatmap.BeatmapSet.Files.Select(file => file.File.Hash).ToArray(), replayFile.Hash);
        });
    }

    private static readonly byte[] recovery_replay = [1, 3, 5, 7];

    private static void assertSavedLibrary(RealmAccess realm, Storage storage, SavedRecoveryLibrary saved)
    {
        realm.Run(database =>
        {
            var set = database.Find<BeatmapSetInfo>(saved.SetId)!;
            var beatmap = database.Find<BeatmapInfo>(saved.BeatmapId)!;
            var score = database.Find<ScoreInfo>(saved.ScoreId)!;
            Assert.Multiple(() =>
            {
                Assert.That(database.All<BeatmapSetInfo>().Count(), Is.EqualTo(1));
                Assert.That(database.All<BeatmapInfo>().Count(), Is.EqualTo(1));
                Assert.That(set.DeletePending, Is.False);
                Assert.That(set.Hash, Is.EqualTo(saved.Plan.SetHash));
                Assert.That(set.Files.Select(file => file.File.Hash), Is.EquivalentTo(saved.FileHashes));
                Assert.That(beatmap.Hash, Is.EqualTo(saved.BeatmapHash));
                Assert.That(beatmap.MD5Hash, Is.EqualTo(saved.Md5));
                Assert.That(beatmap.Metadata.Title, Is.EqualTo(saved.Plan.Title));
                Assert.That(beatmap.UserSettings.Offset, Is.EqualTo(12.5));
                Assert.That(beatmap.Hidden, Is.True);
                Assert.That(score.BeatmapInfo!.ID, Is.EqualTo(saved.BeatmapId));
                Assert.That(score.BeatmapHash, Is.EqualTo(saved.BeatmapHash));
                Assert.That(score.TotalScore, Is.EqualTo(12345));
                Assert.That(score.MaxCombo, Is.EqualTo(123));
                Assert.That(score.Files.Single().File.Hash, Is.EqualTo(saved.ReplayHash));
                Assert.That(database.All<BeatmapCollection>().Single().BeatmapMD5Hashes, Is.EqualTo(new[] { saved.Md5 }));
            });
        });
        using var stream = new RealmFileStore(realm, storage).Store.GetStream(new RealmFile { Hash = saved.ReplayHash }.GetStoragePath());
        using var memory = new MemoryStream();
        stream.CopyTo(memory);
        Assert.That(memory.ToArray(), Is.EqualTo(recovery_replay));
        Assert.That(saved.FileHashes.All(hash => storedFileExists(storage, hash)), Is.True);
    }

    private sealed record SavedRecoveryLibrary(O2JamImportPlan Plan, Guid SetId, Guid BeatmapId, Guid ScoreId,
        string BeatmapHash, string Md5, string[] FileHashes, string ReplayHash);

    private sealed class FileWriteFailure
    {
        public string? Hash;
        public Dictionary<string, int> Moves { get; } = [];
    }

    private sealed class FileWriteFaultStorage(Storage root, FileWriteFailure failure) : NativeStorage(root.GetFullPath(string.Empty), null)
    {
        public override Storage GetStorageForDirectory(string path) => new FileWriteFaultStorage(root.GetStorageForDirectory(path), failure);

        public override void Move(string from, string to)
        {
            base.Move(from, to);
            var hash = Path.GetFileName(to);
            failure.Moves[hash] = failure.Moves.GetValueOrDefault(hash) + 1;
            if (hash == failure.Hash)
                throw new IOException("Injected disk failure after native safe write.");
        }
    }
}
