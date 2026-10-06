using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Import;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void KnownSourceHintCannotHideAPathConflictAddedAfterTheSnapshot() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("first.ojn");
        var other = storage.GetFullPath("second.ojn");
        var bytes = OjnTestData.CreateChart();
        File.WriteAllBytes(path, bytes);
        BitConverter.GetBytes(750).CopyTo(bytes, 0);
        File.WriteAllBytes(other, bytes);
        var planner = new O2JamImportPlanner();
        var writer = new O2JamLibraryWriter(realm, storage);
        var plan = planner.Create(path);
        writer.Write(plan);
        var hint = writer.GetImportedSources()[path].SetId;
        writer.Write(planner.Create(other));
        var otherId = writer.GetImportedSources()[other].SetId;
        realm.Write(database => database.Find<BeatmapSetInfo>(otherId)!.Beatmaps.Single().Metadata.AudioFile = plan.FileName);
        Assert.Throws<InvalidDataException>(() => writer.WriteBatch([new O2JamLibraryWriteRequest(plan, hint)]));
        realm.Run(database =>
        {
            Assert.That(database.Find<BeatmapSetInfo>(hint)!.DeletePending, Is.False);
            Assert.That(database.Find<BeatmapSetInfo>(otherId)!.DeletePending, Is.False);
            Assert.That(database.All<BeatmapSetInfo>().Count(), Is.EqualTo(2));
        });
    });

    [Test]
    public void LegacyPathFallbackIgnoresANonOjnAudioReference() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("legacy.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var writer = new O2JamLibraryWriter(realm, storage);
        var plan = new O2JamImportPlanner().Create(path);
        writer.Write(plan);
        var id = writer.GetImportedSources()[path].SetId;
        realm.Write(database =>
        {
            var beatmap = database.Find<BeatmapSetInfo>(id)!.Beatmaps.Single();
            beatmap.Metadata.AudioFile = "preview.mp3";
            beatmap.Hash = plan.SourceHash;
            var lookup = new O2JamLibraryLookup(database, [new O2JamLibraryWriteRequest(plan with
            {
                SourceHash = "unrelated-source-hash",
                SetHash = "unrelated-set-hash",
            })]);
            Assert.That(lookup.FindPath(path)?.ID, Is.EqualTo(id));
        });
    });

    [Test]
    public void ReadOnlyNativeCopyCanUseLookupWithoutReclaimingReservations() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("stored.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var id = writer.GetImportedSources()[path].SetId;
        var copy = storage.GetFullPath("lookup-copy.realm");
        var reservedHash = new string('b', 64);
        var version = realm.Write(database =>
        {
            database.Add(new RealmFile { Hash = reservedHash });
            database.WriteCopy(new RealmConfiguration(copy) { SchemaVersion = database.Config.SchemaVersion });
            return database.Config.SchemaVersion;
        });
        var before = SHA256.HashData(File.ReadAllBytes(copy));
        using (var database = Realm.GetInstance(new RealmConfiguration(copy) { IsReadOnly = true, SchemaVersion = version }))
        {
            var lookup = new O2JamLibraryLookup(database, [new O2JamLibraryWriteRequest(plan)]);
            Assert.That(lookup.FindPath(path)?.ID, Is.EqualTo(id));
            Assert.That(lookup.FindContent(plan.SourceHash)?.ID, Is.EqualTo(id));
            Assert.That(lookup.ContainsSetHash(plan.SetHash), Is.True);
            Assert.That(database.Find<RealmFile>(reservedHash), Is.Not.Null);
            Assert.That(((RealmConfiguration)database.Config).IsReadOnly, Is.True);
        }
        Assert.That(SHA256.HashData(File.ReadAllBytes(copy)), Is.EqualTo(before));
    });
}