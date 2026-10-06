using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using NUnit.Framework;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void SourceFolderCollectionDisablePreservesUntrackedPrefixCollections() => runProjectionTest((realm, storage) =>
    {
        var id = Guid.NewGuid();
        realm.Write(database => database.Add(new BeatmapCollection(O2LazerStrings.SourceFolderCollectionName("User collection").ToString(), ["user"])
        {
            ID = id,
        }));

        var backend = new O2JamRealmLibraryBackend(realm, storage, null, null);
        backend.UpdateCollections(string.Empty, false);
        backend.UpdateCollections(string.Empty, false);

        Assert.That(realm.Run(database => database.Find<BeatmapCollection>(id)!.BeatmapMD5Hashes.ToArray()), Is.EqualTo(new[] { "user" }));
    });

    [TestCase(false)]
    [TestCase(true)]
    public void SourceFolderCollectionsDoNotClaimUntrackedNamesOrMatchingHashes(bool matchesPlan) => runProjectionTest((realm, storage) =>
    {
        var root = storage.GetFullPath("library");
        var name = O2LazerStrings.SourceFolderCollectionName("Song").ToString();
        var userId = Guid.NewGuid();
        var legacyId = Guid.NewGuid();
        var userHash = matchesPlan ? "chart" : "foreign";
        realm.Write(database =>
        {
            addSet(database, database.All<RulesetInfo>().Single(), Path.Combine(root, "Song"), "chart");
            database.Add(new BeatmapCollection(name, [userHash]) { ID = userId });
            database.Add(new BeatmapCollection(O2LazerStrings.SourceFolderCollectionName("Legacy").ToString(), ["legacy"]) { ID = legacyId });
        });

        var service = new O2JamSourceFolderCollectionService(realm);
        Assert.That(service.Synchronise(root).Created, Is.EqualTo(1));
        Assert.That(service.Synchronise(root), Is.EqualTo(new O2JamSourceFolderCollectionResult(0, 0, 0)));
        Assert.That(realm.Run(database => database.All<BeatmapCollection>().Count()), Is.EqualTo(3));
        Assert.That(service.DeleteFeatureCollections(), Is.EqualTo(1));
        Assert.That(service.DeleteFeatureCollections(), Is.Zero);

        realm.Run(database => Assert.Multiple(() =>
        {
            Assert.That(database.Find<BeatmapCollection>(userId)!.BeatmapMD5Hashes, Is.EqualTo(new[] { userHash }));
            Assert.That(database.Find<BeatmapCollection>(legacyId)!.BeatmapMD5Hashes, Is.EqualTo(new[] { "legacy" }));
            Assert.That(database.All<BeatmapCollection>().Count(), Is.EqualTo(2));
        }));
    });

    [Test]
    public void SourceFolderCollectionOwnershipSurvivesRestartAndUserRename() => runProjectionTest((realm, storage) =>
    {
        var root = storage.GetFullPath("library");
        realm.Write(database =>
        {
            addSet(database, database.All<RulesetInfo>().Single(), Path.Combine(root, "Song"), "before");
            database.Add(new RealmRulesetSetting
            {
                RulesetName = O2LazerIdentity.ShortName,
                Key = nameof(O2JamRulesetSetting.ScrollSpeed),
                Value = "16",
            });
        });

        new O2JamSourceFolderCollectionService(realm).Synchronise(root);
        var id = realm.Run(database => database.All<BeatmapCollection>().Single().ID);
        realm.Write(database =>
        {
            database.Find<BeatmapCollection>(id)!.Name = "My renamed collection";
            database.All<osu.Game.Beatmaps.BeatmapInfo>().Single().MD5Hash = "after";
        });
        realm.Dispose();

        using var restarted = new RealmAccess(storage, "client.realm");
        using var config = new O2JamRulesetConfigManager(new SettingsStore(restarted), new O2LazerRuleset().RulesetInfo);
        Assert.That(config.Get<double>(O2JamRulesetSetting.ScrollSpeed), Is.EqualTo(16));
        var service = new O2JamSourceFolderCollectionService(restarted);
        Assert.That(service.Synchronise(root), Is.EqualTo(new O2JamSourceFolderCollectionResult(0, 1, 0)));
        restarted.Run(database => Assert.Multiple(() =>
        {
            Assert.That(database.All<BeatmapCollection>().Count(), Is.EqualTo(1));
            Assert.That(database.Find<BeatmapCollection>(id)!.Name, Is.EqualTo("My renamed collection"));
            Assert.That(database.Find<BeatmapCollection>(id)!.BeatmapMD5Hashes, Is.EqualTo(new[] { "after" }));
        }));
        Assert.That(service.DeleteFeatureCollections(), Is.EqualTo(1));
        Assert.That(config.Get<double>(O2JamRulesetSetting.ScrollSpeed), Is.EqualTo(16));
    });

    [Test]
    public void DeletedSourceFolderCollectionDoesNotClaimUserReplacement() => runProjectionTest((realm, storage) =>
    {
        var root = storage.GetFullPath("library");
        realm.Write(database => addSet(database, database.All<RulesetInfo>().Single(), Path.Combine(root, "Song"), "chart"));
        var service = new O2JamSourceFolderCollectionService(realm);
        service.Synchronise(root);
        var oldId = realm.Run(database => database.All<BeatmapCollection>().Single().ID);
        var replacementId = Guid.NewGuid();
        realm.Write(database =>
        {
            var old = database.Find<BeatmapCollection>(oldId)!;
            var name = old.Name;
            database.Remove(old);
            database.Add(new BeatmapCollection(name, ["user"]) { ID = replacementId });
        });

        Assert.That(service.Synchronise(root).Created, Is.EqualTo(1));
        Assert.That(service.DeleteFeatureCollections(), Is.EqualTo(1));
        realm.Run(database => Assert.Multiple(() =>
        {
            Assert.That(database.All<BeatmapCollection>().Count(), Is.EqualTo(1));
            Assert.That(database.Find<BeatmapCollection>(replacementId)!.BeatmapMD5Hashes, Is.EqualTo(new[] { "user" }));
        }));
    });

    [Test]
    public void SourceFolderCollectionWithRemovedSourceIsDeletedByOwnedIdAfterRename() => runProjectionTest((realm, storage) =>
    {
        var root = storage.GetFullPath("library");
        realm.Write(database => addSet(database, database.All<RulesetInfo>().Single(), Path.Combine(root, "Song"), "chart"));
        var service = new O2JamSourceFolderCollectionService(realm);
        service.Synchronise(root);
        realm.Write(database =>
        {
            database.All<BeatmapCollection>().Single().Name = "Renamed";
            database.All<osu.Game.Beatmaps.BeatmapSetInfo>().Single().DeletePending = true;
        });

        Assert.That(service.Synchronise(root), Is.EqualTo(new O2JamSourceFolderCollectionResult(0, 0, 1)));
        Assert.That(realm.Run(database => database.All<BeatmapCollection>().Count()), Is.Zero);
        Assert.That(service.DeleteFeatureCollections(), Is.Zero);
    });

    [TestCase("not-json")]
    [TestCase("null")]
    [TestCase("[null]")]
    public void InvalidSourceFolderOwnershipStopsWithoutChangingCollections(string value) => runProjectionTest((realm, storage) =>
    {
        var root = storage.GetFullPath("library");
        realm.Write(database => addSet(database, database.All<RulesetInfo>().Single(), Path.Combine(root, "Song"), "chart"));
        var service = new O2JamSourceFolderCollectionService(realm);
        service.Synchronise(root);
        var id = realm.Run(database => database.All<BeatmapCollection>().Single().ID);
        realm.Write(database => database.All<RealmRulesetSetting>().Single(setting => setting.Key == O2JamSourceFolderCollectionService.OwnershipSettingKey).Value = value);

        Assert.Throws<InvalidDataException>(() => service.Synchronise(root));
        Assert.Throws<InvalidDataException>(() => service.DeleteFeatureCollections());
        realm.Run(database => Assert.Multiple(() =>
        {
            Assert.That(database.All<BeatmapCollection>().Count(), Is.EqualTo(1));
            Assert.That(database.Find<BeatmapCollection>(id)!.BeatmapMD5Hashes, Is.EqualTo(new[] { "chart" }));
        }));
    });

    [TestCase("mania", 0)]
    [TestCase("o2lazer", 1)]
    public void OtherRulesetOrVariantOwnershipCannotClaimCollections(string shortName, int variant) => runProjectionTest((realm, storage) =>
    {
        var id = Guid.NewGuid();
        realm.Write(database =>
        {
            database.Add(new BeatmapCollection("User", ["user"]) { ID = id });
            database.Add(new RealmRulesetSetting
            {
                RulesetName = shortName,
                Variant = variant,
                Key = O2JamSourceFolderCollectionService.OwnershipSettingKey,
                Value = JsonSerializer.Serialize(new[] { new { ID = id, Name = "Song" } }),
            });
        });

        Assert.That(new O2JamSourceFolderCollectionService(realm).DeleteFeatureCollections(), Is.Zero);
        Assert.That(realm.Run(database => database.Find<BeatmapCollection>(id)!.BeatmapMD5Hashes.ToArray()), Is.EqualTo(new[] { "user" }));
    });

    [TestCase(false)]
    [TestCase(true)]
    public void AmbiguousSourceFolderOwnershipStopsWithoutChangingCollections(bool duplicateName) => runProjectionTest((realm, storage) =>
    {
        var firstId = Guid.NewGuid();
        var secondId = duplicateName ? Guid.NewGuid() : firstId;
        realm.Write(database =>
        {
            database.Add(new BeatmapCollection("User", ["user"]) { ID = firstId });
            database.Add(new RealmRulesetSetting
            {
                RulesetName = O2LazerIdentity.ShortName,
                Key = O2JamSourceFolderCollectionService.OwnershipSettingKey,
                Value = JsonSerializer.Serialize(new[] { new { ID = firstId, Name = "Song" }, new { ID = secondId, Name = duplicateName ? "Song" : "Other" } }),
            });
        });

        var service = new O2JamSourceFolderCollectionService(realm);
        Assert.Throws<InvalidDataException>(() => service.Synchronise(storage.GetFullPath("library")));
        Assert.Throws<InvalidDataException>(() => service.DeleteFeatureCollections());
        Assert.That(realm.Run(database => database.Find<BeatmapCollection>(firstId)!.BeatmapMD5Hashes.ToArray()), Is.EqualTo(new[] { "user" }));
    });
}
