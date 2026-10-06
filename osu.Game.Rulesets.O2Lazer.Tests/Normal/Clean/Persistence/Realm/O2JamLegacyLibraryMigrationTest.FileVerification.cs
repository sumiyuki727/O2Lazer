using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Framework.Extensions;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase(true)]
    [TestCase(false)]
    public void NativeFileCheckReadsHealthyStoredBytesOnlyOnce(bool observe) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        Assert.That(O2JamFileVerificationPatch.IsInstalled, Is.True);
        var reads = new List<string>();
        var writer = new O2JamLibraryWriter(realm, new FileReadProbeStorage(storage, reads), null, observe);
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        Assert.That(writer.Write(saved.Plan), Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
        foreach (var hash in saved.FileHashes)
            Assert.That(reads.Count(path => path == new RealmFile { Hash = hash }.GetStoragePath()), Is.EqualTo(observe ? 1 : 2));
        Assert.That(notices, Is.Zero);
        assertSavedLibrary(realm, storage, saved);
    });

    [TestCase(false, true)]
    [TestCase(true, true)]
    [TestCase(false, false)]
    [TestCase(true, false)]
    public void NativeFileCheckPreservesSameLengthRepairAndInvalidation(bool background, bool observe) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var hash = saved.FileHashes[background ? 1 : 0];
        var path = new RealmFile { Hash = hash }.GetStoragePath();
        var filePath = storage.GetStorageForDirectory("files").GetFullPath(path);
        var bytes = File.ReadAllBytes(filePath);
        bytes[0] ^= 1;
        File.WriteAllBytes(filePath, bytes);
        var reads = new List<string>();
        var writer = new O2JamLibraryWriter(realm, new FileReadProbeStorage(storage, reads), null, observe);
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        Assert.That(writer.Write(saved.Plan), Is.EqualTo(O2JamLibraryWriteResult.Updated));
        Assert.That(reads.Count(candidate => candidate == path), Is.EqualTo(observe ? 1 : 2));
        Assert.That(notices, Is.EqualTo(1));
        Assert.That(File.ReadAllBytes(filePath), Is.EqualTo(background ? saved.Plan.Background : saved.Plan.SourceData));
        assertSavedLibrary(realm, storage, saved);
        Assert.That(writer.Write(saved.Plan), Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
        Assert.That(notices, Is.EqualTo(1));
    });

    [TestCase(false)]
    [TestCase(true)]
    public void NativeFileCheckDetectsDamageAfterReservation(bool background) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var hash = saved.FileHashes[background ? 1 : 0];
        var path = storage.GetStorageForDirectory("files").GetFullPath(new RealmFile { Hash = hash }.GetStoragePath());
        var writer = new O2JamLibraryWriter(realm, storage, (stage, _) =>
        {
            if (stage != O2JamLibraryWriteStage.FilesReserved)
                return;
            var bytes = File.ReadAllBytes(path);
            bytes[0] ^= 1;
            File.WriteAllBytes(path, bytes);
        });
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        Assert.That(writer.Write(saved.Plan), Is.EqualTo(O2JamLibraryWriteResult.Updated));
        Assert.That(notices, Is.EqualTo(1));
        Assert.That(File.ReadAllBytes(path), Is.EqualTo(background ? saved.Plan.Background : saved.Plan.SourceData));
        assertSavedLibrary(realm, storage, saved);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void NativeRepairFailureRetainsInvalidationAfterBytesReachDisk(bool background) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var hash = saved.FileHashes[background ? 1 : 0];
        storage.GetStorageForDirectory("files").Delete(new RealmFile { Hash = hash }.GetStoragePath());
        var fault = new FileWriteFailure { Hash = hash };
        var writer = new O2JamLibraryWriter(realm, new FileWriteFaultStorage(storage, fault));
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        Assert.Throws<IOException>(() => writer.Write(saved.Plan));
        Assert.That(storedFileExists(storage, hash), Is.True);
        Assert.That(notices, Is.Zero);
        fault.Hash = null;
        Assert.That(writer.Write(saved.Plan), Is.EqualTo(O2JamLibraryWriteResult.Updated));
        Assert.That(notices, Is.EqualTo(1));
        assertSavedLibrary(realm, storage, saved);
        Assert.That(writer.Write(saved.Plan), Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
        Assert.That(notices, Is.EqualTo(1));
    });

    [Test]
    public void NativeFileObservationIsScopedToStoreAndRestoresNestedScope() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var first = new RealmFileStore(realm, storage);
        var second = new RealmFileStore(realm, storage);
        var callbacks = 0;
        realm.Write(database =>
        {
            using var source = new MemoryStream(saved.Plan.SourceData, writable: false);
            using var outer = O2JamFileVerificationPatch.Observe(first, saved.FileHashes[0], _ => callbacks++, new Stopwatch());
            second.Add(source, database);
            Assert.That(outer.WasObserved, Is.False, "Another store must not publish into the active writer's scope.");
            using (var inner = O2JamFileVerificationPatch.Observe(second, saved.FileHashes[0], _ => callbacks++, new Stopwatch()))
            {
                first.Add(source, database);
                Assert.That(outer.WasObserved, Is.False);
                second.Add(source, database);
                Assert.That(inner.WasObserved, Is.True);
            }
            first.Add(source, database);
            Assert.That(outer.WasObserved, Is.True);
            Assert.That(callbacks, Is.Zero);
        });
        assertSavedLibrary(realm, storage, saved);
    });

    [Test]
    public void NativeFileObservationFailureCannotInterruptNativeRepair() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var hash = saved.FileHashes[0];
        storage.GetStorageForDirectory("files").Delete(new RealmFile { Hash = hash }.GetStoragePath());
        var files = new RealmFileStore(realm, storage);
        realm.Write(database =>
        {
            using var stream = new MemoryStream(saved.Plan.SourceData, writable: false);
            using var observation = O2JamFileVerificationPatch.Observe(files, hash, _ => throw new IOException("Injected observer failure."), new Stopwatch());
            Assert.That(files.Add(stream, database).Hash, Is.EqualTo(hash));
            Assert.That(observation.WasObserved, Is.False);
        });
        using var repaired = files.Storage.GetStream(new RealmFile { Hash = hash }.GetStoragePath());
        Assert.That(repaired.ComputeSHA2Hash(), Is.EqualTo(hash));
        assertSavedLibrary(realm, storage, saved);
    });
}