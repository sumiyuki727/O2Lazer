using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void OrdinaryRefreshOnlyProbesStoredFileAvailability() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var reads = new List<string>();
        var writer = new O2JamLibraryWriter(realm, new FileReadProbeStorage(storage, reads));
        var sources = writer.GetImportedSources();
        var summary = new O2JamImportService(new O2JamImportPlanner(), writer).Refresh([saved.Plan.SourcePath], sources);
        Assert.That(summary.AlreadyPresent, Is.EqualTo(1));
        Assert.That(reads, Is.Empty, "Healthy native files must not be hashed on an ordinary refresh.");
        assertSavedLibrary(realm, storage, saved);
    });

    [Test]
    public void InternalRepairFindsSameLengthStoredCorruption() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var path = storage.GetStorageForDirectory("files").GetFullPath(new RealmFile { Hash = saved.FileHashes[0] }.GetStoragePath());
        var bytes = File.ReadAllBytes(path);
        bytes[0] ^= 1;
        File.WriteAllBytes(path, bytes);
        var writer = new O2JamLibraryWriter(realm, storage);
        Assert.That(writer.GetImportedSources()[saved.Plan.SourcePath].CanReuseFiles, Is.True);
        var sources = writer.GetImportedSources(mode: O2JamLibraryRefreshMode.Repair);
        Assert.That(sources[saved.Plan.SourcePath].CanReuseFiles, Is.False);
        using var backend = new O2JamRealmLibraryBackend(realm, storage, null, null);
        var summary = backend.Refresh(storage.GetFullPath(string.Empty), null, CancellationToken.None, O2JamLibraryRefreshMode.Repair);
        Assert.That(summary.Updated, Is.EqualTo(1));
        assertSavedLibrary(realm, storage, saved);
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(saved.Plan.SourceData));
    });
    [TestCase(false)]
    [TestCase(true)]
    public void OutdatedProjectionDefersFileVerificationToNativeWrite(bool encoding) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        realm.Write(database =>
        {
            var metadata = database.Find<BeatmapInfo>(saved.BeatmapId)!.Metadata;
            metadata.Tags = encoding
                ? metadata.Tags.Replace(O2JamLibraryWriter.EncodingMarker, "o2lazer-encoding:1", StringComparison.Ordinal)
                : metadata.Tags.Replace($"{O2JamImportMetadata.ProjectionPrefix}1:{O2JamImportMetadata.ProjectionVersion}",
                    $"{O2JamImportMetadata.ProjectionPrefix}1:20260930", StringComparison.Ordinal);
        });
        File.WriteAllBytes(storage.GetStorageForDirectory("files").GetFullPath(new RealmFile { Hash = saved.FileHashes[0] }.GetStoragePath()), [1, 2, 3]);
        var reads = new List<string>();
        var writer = new O2JamLibraryWriter(realm, new FileReadProbeStorage(storage, reads));
        var imported = writer.GetImportedSources();
        Assert.Multiple(() =>
        {
            Assert.That(reads, Is.Empty, "A mandatory projection update should not hash its files twice before native Add.");
            Assert.That(imported[saved.Plan.SourcePath].CanReuseFiles, Is.False);
            Assert.That(imported[saved.Plan.SourcePath].ManiaCache, Is.Not.Null, "Deferring file checks must preserve a valid difficulty cache.");
        });
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        Assert.That(service.Refresh([saved.Plan.SourcePath], imported).Updated, Is.EqualTo(1));
        assertSavedLibrary(realm, storage, saved);
        using var repaired = storage.GetStorageForDirectory("files").GetStream(new RealmFile { Hash = saved.FileHashes[0] }.GetStoragePath());
        Assert.That(repaired.ComputeSHA2Hash(), Is.EqualTo(saved.FileHashes[0]));
        Assert.That(writer.GetImportedSources()[saved.Plan.SourcePath].CanReuseFiles, Is.True);
    });

    [Test]
    public void IndexProgressCanCancelBeforeReadingStoredBytes() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var reads = new List<string>();
        var writer = new O2JamLibraryWriter(realm, new FileReadProbeStorage(storage, reads));
        var progress = new List<O2JamLibraryProgress>();
        using var cancellation = new CancellationTokenSource();
        Assert.Throws<OperationCanceledException>(() => writer.GetImportedSources(cancellation.Token, value =>
        {
            progress.Add(value);
            if (value.Stage == O2JamLibraryStage.CheckingStoredFiles)
                cancellation.Cancel();
        }, O2JamLibraryRefreshMode.Repair));
        Assert.Multiple(() =>
        {
            Assert.That(progress.First(), Is.EqualTo(new O2JamLibraryProgress(0, 1, O2JamLibraryStage.ReadingImportedMetadata)));
            Assert.That(progress, Does.Contain(new O2JamLibraryProgress(1, 1, O2JamLibraryStage.ReadingImportedMetadata)));
            Assert.That(progress.Last(), Is.EqualTo(new O2JamLibraryProgress(0, 1, O2JamLibraryStage.CheckingStoredFiles)));
            Assert.That(reads, Is.Empty);
        });
        assertSavedLibrary(realm, storage, saved);
    });

    [Test]
    public void IndexProgressObserverFailureDoesNotChangeFileHealth() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var writer = new O2JamLibraryWriter(realm, storage);
        var imported = writer.GetImportedSources(CancellationToken.None, _ => throw new InvalidOperationException("Injected progress observer failure."));
        Assert.That(imported[saved.Plan.SourcePath].CanReuseFiles, Is.True);
        assertSavedLibrary(realm, storage, saved);
    });

    [Test]
    public void RefreshReportsPreparationBeforeChartProgress() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        using var backend = new O2JamRealmLibraryBackend(realm, storage, null, null);
        var progress = new List<O2JamLibraryProgress>();
        var summary = backend.Refresh(storage.GetFullPath(string.Empty), progress.Add, CancellationToken.None);
        Assert.That(summary.AlreadyPresent, Is.EqualTo(1));
        O2JamLibraryStage[] expectedStages =
        [
            O2JamLibraryStage.ScanningSources,
            O2JamLibraryStage.ReadingImportedMetadata,
            O2JamLibraryStage.ReadingSourceFiles,
            O2JamLibraryStage.MatchingSources,
            O2JamLibraryStage.RefreshingCharts,
        ];
        Assert.That(progress.Select(value => value.Stage).Distinct(), Is.EqualTo(expectedStages));
        Assert.That(progress, Does.Contain(new O2JamLibraryProgress(1, 0, O2JamLibraryStage.ScanningSources)));
        assertSavedLibrary(realm, storage, saved);
    });

    private sealed class FileReadProbeStorage(Storage root, List<string> reads) : NativeStorage(root.GetFullPath(string.Empty), null)
    {
        public override Storage GetStorageForDirectory(string path) => new FileReadProbeStorage(root.GetStorageForDirectory(path), reads);

        public override Stream GetStream(string path, FileAccess access = FileAccess.Read, FileMode mode = FileMode.OpenOrCreate)
        {
            if (access == FileAccess.Read)
                reads.Add(path);
            return base.GetStream(path, access, mode);
        }
    }
    [TestCase(false, false)]
    [TestCase(true, false)]
    [TestCase(false, true)]
    [TestCase(true, true)]
    public void RefreshRepairsActiveFilesWithoutReplacingIdentity(bool background, bool corrupt) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var writer = new O2JamLibraryWriter(realm, storage);
        var hash = saved.FileHashes[background ? 1 : 0];
        var fileStorage = storage.GetStorageForDirectory("files");
        var filePath = new RealmFile { Hash = hash }.GetStoragePath();
        if (corrupt)
            File.WriteAllBytes(fileStorage.GetFullPath(filePath), [1, 2, 3]);
        else
            fileStorage.Delete(filePath);
        var imported = writer.GetImportedSources(mode: O2JamLibraryRefreshMode.Repair);
        Assert.That(imported[saved.Plan.SourcePath].HasCurrentMetadata, Is.True);
        Assert.That(imported[saved.Plan.SourcePath].CanReuseFiles, Is.False);
        Assert.That(imported[saved.Plan.SourcePath].ManiaCache, Is.Not.Null);
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var summary = service.Refresh([saved.Plan.SourcePath], imported);
        Assert.That(summary.Updated, Is.EqualTo(1));
        Assert.That(summary.Imported, Is.Zero);
        Assert.That(summary.Failed, Is.Zero);
        Assert.That(notices, Is.EqualTo(1));
        assertSavedLibrary(realm, storage, saved);
        using var repaired = fileStorage.GetStream(filePath);
        Assert.That(repaired.ComputeSHA2Hash(), Is.EqualTo(hash));
        Assert.That(writer.GetImportedSources()[saved.Plan.SourcePath].CanReuseFiles, Is.True);
        Assert.That(service.Refresh([saved.Plan.SourcePath], writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(1));
        Assert.That(notices, Is.EqualTo(1));
    });

    [Test]
    public void RefreshRestoresMissingCoverReferenceAndPreservesSharedUsage() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var foreignId = realm.Write(database =>
        {
            var foreign = database.Add(new RulesetInfo("foreign", "Foreign", "test", -1));
            var set = new BeatmapSetInfo { Hash = "foreign-set" };
            set.Beatmaps.Add(new BeatmapInfo(foreign) { BeatmapSet = set });
            set.Files.Add(new RealmNamedFileUsage(database.Find<RealmFile>(saved.FileHashes[1])!, "cover.png"));
            database.Add(set);
            var owned = database.Find<BeatmapSetInfo>(saved.SetId)!;
            owned.Files.Remove(owned.Files.Single(usage => usage.Filename == "o2jam-background.png"));
            return set.ID;
        });
        var writer = new O2JamLibraryWriter(realm, storage);
        Assert.That(writer.GetImportedSources()[saved.Plan.SourcePath].CanReuseFiles, Is.False);
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        Assert.That(service.Refresh([saved.Plan.SourcePath], writer.GetImportedSources()).Updated, Is.EqualTo(1));
        assertSavedLibrary(realm, storage, saved, expectedSets: 2);
        Assert.That(realm.Run(database => database.Find<RealmFile>(saved.FileHashes[1])!.Usages.Count()), Is.EqualTo(2));
        Assert.That(realm.Run(database => database.Find<BeatmapSetInfo>(foreignId)!.Files.Single().Filename), Is.EqualTo("cover.png"));
        Assert.That(writer.GetImportedSources()[saved.Plan.SourcePath].CanReuseFiles, Is.True);
    });

    [Test]
    public void FailedActiveRepairStillInvalidatesOnSuccessfulRetry() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        storage.GetStorageForDirectory("files").Delete(new RealmFile { Hash = saved.FileHashes[0] }.GetStoragePath());
        var fail = true;
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (fail && stage == O2JamLibraryWriteStage.BeforeCommit)
                throw new IOException("Injected failure after active bytes were repaired.");
        });
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        Assert.Throws<IOException>(() => writer.Write(saved.Plan));
        Assert.That(storedFileExists(storage, saved.FileHashes[0]), Is.True);
        Assert.That(notices, Is.Zero);
        Assert.That(writer.GetImportedSources()[saved.Plan.SourcePath].CanReuseFiles, Is.False);
        fail = false;
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        Assert.That(service.Refresh([saved.Plan.SourcePath], writer.GetImportedSources()).Updated, Is.EqualTo(1));
        Assert.That(notices, Is.EqualTo(1));
        assertSavedLibrary(realm, storage, saved);
        Assert.That(service.Refresh([saved.Plan.SourcePath], writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(1));
        Assert.That(notices, Is.EqualTo(1));
    });

    [Test]
    public void SharedCoverRepairInvalidatesEverySetAcrossRefreshBatches() => runProjectionTest((realm, storage) =>
    {
        var plans = Enumerable.Range(300, 17).Select(id =>
        {
            var bytes = recoverySource((uint)id);
            Array.Clear(bytes, bytes.Length - 4, 4);
            var path = storage.GetFullPath($"shared-{id}.ojn");
            File.WriteAllBytes(path, bytes);
            return new O2JamImportPlanner().Create(path);
        }).ToArray();
        var writer = new O2JamLibraryWriter(realm, storage);
        Assert.That(writer.WriteBatch(plans.Select(plan => new O2JamLibraryWriteRequest(plan)).ToArray()), Is.All.EqualTo(O2JamLibraryWriteResult.Imported));
        var coverHash = recoveryHashes([new O2JamLibraryWriteRequest(plans[0])])[1];
        Assert.That(realm.Run(database => database.Find<RealmFile>(coverHash)!.Usages.Count()), Is.EqualTo(17));
        storage.GetStorageForDirectory("files").Delete(new RealmFile { Hash = coverHash }.GetStoragePath());
        var notices = new List<Guid>();
        writer.BeatmapUpdated += beatmap => notices.Add(beatmap.ID);
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        Assert.That(service.Refresh(plans.Select(plan => plan.SourcePath).ToArray(), writer.GetImportedSources()).Updated, Is.EqualTo(17));
        Assert.That(notices, Has.Count.EqualTo(17));
        Assert.That(notices.Distinct().Count(), Is.EqualTo(17));
        Assert.That(writer.PendingNotifications, Is.Zero);
        Assert.That(writer.GetImportedSources().Values.All(source => source.CanReuseFiles), Is.True);
        Assert.That(service.Refresh(plans.Select(plan => plan.SourcePath).ToArray(), writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(17));
        Assert.That(notices, Has.Count.EqualTo(17));
    });

    [Test]
    public void RefreshDoesNotProbeOrRewriteStoredReplayBytes() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var path = storage.GetStorageForDirectory("files").GetFullPath(new RealmFile { Hash = saved.ReplayHash }.GetStoragePath());
        File.WriteAllBytes(path, [9, 8, 7]);
        var writer = new O2JamLibraryWriter(realm, storage);
        Assert.That(writer.GetImportedSources()[saved.Plan.SourcePath].CanReuseFiles, Is.True);
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        Assert.That(service.Refresh([saved.Plan.SourcePath], writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(1));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(new byte[] { 9, 8, 7 }));
    });
}
