using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void LaterBatchRechecksCommittedContentWithoutScanHints() => runProjectionTest((realm, storage) =>
    {
        var a = storage.GetFullPath("first.ojn");
        var b = storage.GetFullPath("later.ojn");
        File.WriteAllBytes(a, OjnTestData.CreateChart());
        File.Copy(a, b);
        var writer = new O2JamLibraryWriter(realm, storage);
        var planner = new O2JamImportPlanner();
        Assert.That(writer.WriteBatch([new O2JamLibraryWriteRequest(planner.Create(a))])[0], Is.EqualTo(O2JamLibraryWriteResult.Imported));
        var setId = writer.GetImportedSources()[a].SetId;
        Assert.That(writer.WriteBatch([new O2JamLibraryWriteRequest(planner.Create(b))])[0], Is.EqualTo(O2JamLibraryWriteResult.AlreadyPresent));
        Assert.That(writer.GetImportedSources().Keys, Is.EqualTo(new[] { a }));
        Assert.That(writer.GetImportedSources()[a].SetId, Is.EqualTo(setId));
    });

    [Test]
    public void LegacyContentWithoutLengthStillMovesToStableCopy() => runProjectionTest((realm, storage) =>
    {
        var original = storage.GetFullPath("original.ojn");
        var a = storage.GetFullPath("a-copy.ojn");
        var z = storage.GetFullPath("z-copy.ojn");
        File.WriteAllBytes(original, OjnTestData.CreateChart());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(original));
        var id = writer.GetImportedSources()[original].SetId;
        realm.Write(database => database.All<BeatmapInfo>().Single().Metadata.Tags = "o2jam");
        var sources = writer.GetImportedSources();
        Assert.That(sources[original].SourceLength, Is.Null);
        File.Copy(original, z);
        File.Move(original, a);
        var summary = new O2JamImportService(new O2JamImportPlanner(), writer).Refresh([z, a], sources);
        Assert.That(summary.Updated, Is.EqualTo(1));
        Assert.That(summary.AlreadyPresent, Is.EqualTo(1));
        Assert.That(summary.Failed, Is.Zero);
        Assert.That(writer.GetImportedSources()[a].SetId, Is.EqualTo(id));
    });

    [Test]
    public void ExchangedRegisteredPathsPreserveBothSetsAsConflict() => runProjectionTest((realm, storage) =>
    {
        var a = storage.GetFullPath("a.ojn");
        var b = storage.GetFullPath("b.ojn");
        var first = OjnTestData.CreateChart();
        var second = OjnTestData.CreateChart();
        BitConverter.GetBytes(180f).CopyTo(second, 16);
        File.WriteAllBytes(a, first);
        File.WriteAllBytes(b, second);
        var writer = new O2JamLibraryWriter(realm, storage);
        var planner = new O2JamImportPlanner();
        writer.Write(planner.Create(a));
        writer.Write(planner.Create(b));
        var sources = writer.GetImportedSources();
        File.WriteAllBytes(a, second);
        File.WriteAllBytes(b, first);
        var summary = new O2JamImportService(planner, writer).Refresh([b, a], sources);
        Assert.That(summary.Failed, Is.EqualTo(2));
        Assert.That(summary.Imported + summary.Updated, Is.Zero);
        var after = writer.GetImportedSources();
        Assert.That(after.Keys, Is.EquivalentTo(sources.Keys));
        foreach (var (path, source) in sources)
        {
            Assert.That(after[path].SetId, Is.EqualTo(source.SetId));
            Assert.That(after[path].SourceHash, Is.EqualTo(source.SourceHash));
            Assert.That(after[path].LastLocalUpdate, Is.EqualTo(source.LastLocalUpdate));
            Assert.That(after[path].DifficultyIdentities, Is.EqualTo(source.DifficultyIdentities));
        }
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Any(set => set.DeletePending)), Is.False);
    });
    [TestCase(false)]
    [TestCase(true)]
    public void SameBatchCopiesUseFreshIndexAndKeepOneSet(bool reverse) => runProjectionTest((realm, storage) =>
    {
        var a = storage.GetFullPath("a.ojn");
        var b = storage.GetFullPath("b.ojn");
        File.WriteAllBytes(a, OjnTestData.CreateChart());
        File.WriteAllBytes(b, OjnTestData.CreateChart());
        var writer = new O2JamLibraryWriter(realm, storage);
        var planner = new O2JamImportPlanner();
        var paths = reverse ? new[] { b, a } : [a, b];
        var results = writer.WriteBatch(paths.Select(path => new O2JamLibraryWriteRequest(planner.Create(path))).ToArray());
        Assert.That(results, Is.EqualTo(new[] { O2JamLibraryWriteResult.Imported, O2JamLibraryWriteResult.AlreadyPresent }));
        Assert.That(writer.GetImportedSources().Keys, Is.EqualTo(new[] { paths[0] }));
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.EqualTo(1));
    });

    [Test]
    public void RefreshGroupsCrossBatchCopiesAndSelectsStablePaths() => runProjectionTest((realm, storage) =>
    {
        var paths = new List<string>();
        for (var index = 0; index < 10; index++)
        {
            var bytes = OjnTestData.CreateChart();
            BitConverter.GetBytes(index + 100).CopyTo(bytes, 0);
            foreach (var prefix in new[] { "z", "a", "m" })
            {
                var path = storage.GetFullPath($"{prefix}-{index:D2}.ojn");
                File.WriteAllBytes(path, bytes);
                paths.Add(path);
            }
        }
        var writer = new O2JamLibraryWriter(realm, storage);
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var summary = service.Refresh(paths.AsEnumerable().Reverse().Concat(paths).ToArray(), writer.GetImportedSources());
        Assert.Multiple(() =>
        {
            Assert.That(summary.Imported, Is.EqualTo(10));
            Assert.That(summary.AlreadyPresent, Is.EqualTo(20));
            Assert.That(summary.Failed, Is.Zero);
            Assert.That(writer.GetImportedSources().Keys.Select(Path.GetFileName), Is.All.StartsWith("a-"));
            Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.EqualTo(10));
        });
        var ids = writer.GetImportedSources().Values.Select(source => source.SetId).ToHashSet();
        summary = service.Refresh(paths, writer.GetImportedSources());
        Assert.That(summary.AlreadyPresent, Is.EqualTo(30));
        Assert.That(writer.GetImportedSources().Values.Select(source => source.SetId), Is.EquivalentTo(ids));
    });

    [TestCase(false)]
    [TestCase(true)]
    public void VerifiedRegisteredCopyIsPreferredOverEarlierPath(bool includeRegisteredPath) => runProjectionTest((realm, storage) =>
    {
        var registered = storage.GetFullPath("z-registered.ojn");
        var copy = storage.GetFullPath("a-copy.ojn");
        File.WriteAllBytes(registered, OjnTestData.CreateChart());
        File.Copy(registered, copy);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(registered));
        var setId = writer.GetImportedSources()[registered].SetId;
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var summary = service.Refresh(includeRegisteredPath ? [copy, registered] : [copy], writer.GetImportedSources());
        Assert.That(summary.AlreadyPresent, Is.EqualTo(includeRegisteredPath ? 2 : 1));
        Assert.That(writer.GetImportedSources().Keys, Is.EqualTo(new[] { registered }));
        Assert.That(writer.GetImportedSources()[registered].SetId, Is.EqualTo(setId));
    });

    [Test]
    public void ChangedRegisteredBytesMoveOldIdentityToVerifiedCopyBeforeReplacement() => runProjectionTest((realm, storage) =>
    {
        var original = storage.GetFullPath("a-original.ojn");
        var copy = storage.GetFullPath("z-copy.ojn");
        var bytes = OjnTestData.CreateChart();
        File.WriteAllBytes(original, bytes);
        var planner = new O2JamImportPlanner();
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(planner.Create(original));
        var beatmapId = realm.Run(database => database.All<BeatmapInfo>().Single().ID);
        var source = writer.GetImportedSources()[original];
        var scoreId = Guid.NewGuid();
        realm.Write(database =>
        {
            var beatmap = database.Find<BeatmapInfo>(beatmapId)!;
            database.Add(new ScoreInfo(beatmap, beatmap.Ruleset) { ID = scoreId, TotalScore = 1234 });
        });
        File.Copy(original, copy);
        BitConverter.GetBytes(180f).CopyTo(bytes, 16);
        File.WriteAllBytes(original, bytes);
        File.SetLastWriteTimeUtc(original, source.LastLocalUpdate!.Value.UtcDateTime);
        var service = new O2JamImportService(planner, writer);
        var summary = service.Refresh([original, copy], writer.GetImportedSources());
        Assert.Multiple(() =>
        {
            Assert.That(summary.Imported, Is.EqualTo(1));
            Assert.That(summary.Updated, Is.EqualTo(1));
            Assert.That(summary.Failed, Is.Zero);
            Assert.That(writer.GetImportedSources()[copy].SetId, Is.EqualTo(source.SetId));
            Assert.That(writer.GetImportedSources()[original].SetId, Is.Not.EqualTo(source.SetId));
            Assert.That(realm.Run(database => database.Find<ScoreInfo>(scoreId)!.BeatmapInfo!.ID), Is.EqualTo(beatmapId));
            Assert.That(realm.Run(database => database.Find<BeatmapSetInfo>(source.SetId)!.DeletePending), Is.False);
        });
        Assert.That(service.Refresh([copy, original], writer.GetImportedSources()).AlreadyPresent, Is.EqualTo(2));
    });

    [Test]
    public void UnreadableRegisteredCopyIsPreservedAndReported() => runProjectionTest((realm, storage) =>
    {
        var original = storage.GetFullPath("original.ojn");
        var copy = storage.GetFullPath("copy.ojn");
        File.WriteAllBytes(original, OjnTestData.CreateChart());
        File.Copy(original, copy);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(original));
        var sources = writer.GetImportedSources();
        using var guard = new FileStream(original, FileMode.Open, FileAccess.Read, FileShare.None);
        var summary = new O2JamImportService(new O2JamImportPlanner(), writer).Refresh([copy], sources);
        Assert.That(summary.Failed, Is.EqualTo(1));
        Assert.That(writer.GetImportedSources()[original].SetId, Is.EqualTo(sources[original].SetId));
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Single().DeletePending), Is.False);
    });

    [Test]
    public void NativeNotificationFailureRetainsCommitAndRetriesOnlyFailedRecipient() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("notifications.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        realm.Write(database =>
        {
            foreach (var beatmap in database.All<BeatmapInfo>())
                beatmap.Metadata.Tags += " o2lazer-o2jam-stars:1:0.5";
        });
        var fail = true;
        var successfulCalls = 0;
        var recovered = new List<Guid>();
        writer.BeatmapUpdated += beatmap =>
        {
            Assert.That(beatmap.IsManaged, Is.False);
            if (fail)
                throw new IOException("Injected cache failure.");
            recovered.Add(beatmap.ID);
        };
        writer.BeatmapUpdated += _ => successfulCalls++;
        var service = new O2JamImportService(new O2JamImportPlanner(), writer);
        var summary = service.Refresh([path], writer.GetImportedSources());
        Assert.Multiple(() =>
        {
            Assert.That(summary.Updated, Is.EqualTo(1));
            Assert.That(summary.Failed, Is.Zero);
            Assert.That(summary.PendingNotifications, Is.EqualTo(3));
            Assert.That(successfulCalls, Is.EqualTo(3));
            Assert.That(writer.GetImportedSources()[path].HasCurrentMetadata, Is.True);
        });
        fail = false;
        summary = service.Refresh([path], writer.GetImportedSources());
        Assert.That(summary.AlreadyPresent, Is.EqualTo(1));
        Assert.That(summary.PendingNotifications, Is.Zero);
        Assert.That(recovered.Distinct().Count(), Is.EqualTo(3));
        Assert.That(successfulCalls, Is.EqualTo(3));
    });

    [Test]
    public void CancelAfterCommitAccountsForEntireBatchAndFinishesCacheInvalidation() => runProjectionTest((realm, storage) =>
    {
        var paths = new List<string>();
        var writer = new O2JamLibraryWriter(realm, storage);
        var planner = new O2JamImportPlanner();
        for (var index = 0; index < 10; index++)
        {
            var bytes = OjnTestData.CreateChart();
            BitConverter.GetBytes(index + 100).CopyTo(bytes, 0);
            var path = storage.GetFullPath($"cancel-{index:D2}.ojn");
            File.WriteAllBytes(path, bytes);
            paths.Add(path);
            writer.Write(planner.Create(path));
        }
        realm.Write(database =>
        {
            foreach (var beatmap in database.All<BeatmapInfo>())
                beatmap.Metadata.Tags += " o2lazer-o2jam-stars:1:0.5";
        });
        using var cancellation = new CancellationTokenSource();
        writer.BeatmapUpdated += _ => cancellation.Cancel();
        var invalidated = new List<string>();
        var service = new O2JamImportService(planner, writer, invalidated.Add);
        var exception = Assert.Throws<O2JamImportCancelledException>(() =>
            service.Refresh(paths, writer.GetImportedSources(), cancellationToken: cancellation.Token))!;
        Assert.Multiple(() =>
        {
            Assert.That(exception.Summary.Updated, Is.EqualTo(8));
            Assert.That(exception.Summary.Failed, Is.Zero);
            Assert.That(invalidated, Has.Count.EqualTo(8));
            Assert.That(writer.GetImportedSources().Values.Count(source => source.HasCurrentMetadata), Is.EqualTo(8));
            Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.EqualTo(10));
        });
        Assert.That(service.Refresh(paths, writer.GetImportedSources()).Updated, Is.EqualTo(2));
    });
}
