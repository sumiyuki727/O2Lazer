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
    [TestCase(0)]
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    public void PathLookupRecognisesCurrentAndLegacySourceRepresentations(int format) => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("nested/chart.ojn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var id = writer.GetImportedSources()[path].SetId;
        realm.Write(database =>
        {
            var set = database.Find<BeatmapSetInfo>(id)!;
            var beatmap = set.Beatmaps.Single();
            switch (format)
            {
                case 1:
                case 5:
                    beatmap.Metadata.Source = storage.GetFullPath(string.Empty);
                    beatmap.Metadata.AudioFile = format == 1 ? "nested\\chart.ojn" : "nested/chart.ojn";
                    break;

                case 2:
                    beatmap.Metadata.Source = storage.GetFullPath("unrelated");
                    beatmap.Metadata.AudioFile = path;
                    break;

                case 3:
                    beatmap.Metadata.Source = storage.GetFullPath(string.Empty);
                    beatmap.Metadata.AudioFile = string.Empty;
                    beatmap.Hash = plan.SourceHash;
                    set.Files.Single().Filename = "nested\\chart.ojn";
                    break;

                case 4:
                    beatmap.Metadata.AudioFile = "CHART.OJN";
                    break;

                case 6:
                    beatmap.Metadata.AudioFile = $"{Path.GetPathRoot(path)![0]}:chart.ojn";
                    break;
            }

            // Matching hashes must not hide a missing path candidate.
            var expectedPath = format == 6 ? Path.GetFullPath(beatmap.Metadata.AudioFile) : path;
            var lookup = new O2JamLibraryLookup(database, [new O2JamLibraryWriteRequest(plan with
            {
                SourcePath = expectedPath,
                SetHash = "unrelated-set-hash",
                SourceHash = "unrelated-source-hash",
            })]);
            Assert.That(lookup.FindPath(expectedPath)?.ID, Is.EqualTo(id));
        });
    });

    [Test]
    public void PathLookupRejectsConflictingCurrentAndLegacyRepresentations() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("nested/chart.ojn");
        var other = storage.GetFullPath("other.ojn");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        var bytes = OjnTestData.CreateChart();
        File.WriteAllBytes(path, bytes);
        BitConverter.GetBytes(650).CopyTo(bytes, 0);
        File.WriteAllBytes(other, bytes);
        var planner = new O2JamImportPlanner();
        var writer = new O2JamLibraryWriter(realm, storage);
        var plan = planner.Create(path);
        writer.Write(plan);
        writer.Write(planner.Create(other));
        var sources = writer.GetImportedSources();
        realm.Write(database =>
        {
            var beatmap = database.Find<BeatmapSetInfo>(sources[other].SetId)!.Beatmaps.Single();
            beatmap.Metadata.Source = storage.GetFullPath(string.Empty);
            beatmap.Metadata.AudioFile = "nested\\chart.ojn";
        });
        Assert.Throws<InvalidDataException>(() => writer.Write(plan));
        realm.Run(database =>
        {
            Assert.That(database.All<BeatmapSetInfo>().Count(), Is.EqualTo(2));
            foreach (var source in sources.Values)
            {
                var set = database.Find<BeatmapSetInfo>(source.SetId)!;
                Assert.That(set.DeletePending, Is.False);
                Assert.That(set.Beatmaps.Single().MD5Hash, Is.EqualTo(source.DifficultyIdentities!.Single().Md5Hash));
            }
        });
    });

    [TestCase("foreign")]
    [TestCase("deleted")]
    [TestCase("orphan")]
    public void BatchLookupIgnoresForeignDeletedAndOrphanMatches(string state) => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("excluded.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var id = writer.GetImportedSources()[path].SetId;
        realm.Write(database =>
        {
            var set = database.Find<BeatmapSetInfo>(id)!;
            var beatmap = set.Beatmaps.Single();
            switch (state)
            {
                case "foreign":
                    beatmap.Ruleset = database.Add(new RulesetInfo { ShortName = "foreign" });
                    break;

                case "deleted":
                    set.DeletePending = true;
                    break;

                case "orphan":
                    beatmap.Hash = plan.SourceHash;
                    beatmap.BeatmapSet = null;
                    set.Beatmaps.Clear();
                    Assert.That(database.Find<BeatmapInfo>(beatmap.ID), Is.Not.Null);
                    break;
            }

            var lookup = new O2JamLibraryLookup(database, [new O2JamLibraryWriteRequest(plan)]);
            Assert.Multiple(() =>
            {
                Assert.That(lookup.FindPath(path), Is.Null);
                Assert.That(lookup.FindContent(plan.SourceHash), Is.Null);
                Assert.That(lookup.ContainsSetHash(plan.SetHash), Is.False);
            });
        });
    });

    [Test]
    public void PathLookupDoesNotUseAnotherRulesetsMetadata() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("owned.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var id = writer.GetImportedSources()[path].SetId;
        realm.Write(database =>
        {
            var set = database.Find<BeatmapSetInfo>(id)!;
            set.Beatmaps.Single().Metadata.AudioFile = "other.ojn";
            var foreign = database.Add(new RulesetInfo { ShortName = "foreign" });
            set.Beatmaps.Add(new BeatmapInfo(foreign, metadata: new BeatmapMetadata
            {
                Source = plan.SourceDirectory,
                AudioFile = plan.FileName,
            }) { BeatmapSet = set });
            var lookup = new O2JamLibraryLookup(database, [new O2JamLibraryWriteRequest(plan with
            {
                SetHash = "unrelated-set-hash",
                SourceHash = "unrelated-source-hash",
            })]);
            Assert.That(lookup.FindPath(path), Is.Null);
        });
    });

    [Test]
    public void PathLookupUsesMatchingDifficultyMetadataInsteadOfSetFirst() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("three-slots.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var id = writer.GetImportedSources()[path].SetId;
        realm.Write(database =>
        {
            var set = database.Find<BeatmapSetInfo>(id)!;
            Assert.That(set.Beatmaps, Has.Count.EqualTo(3));
            set.Beatmaps[0].Metadata.AudioFile = "other.ojn";
            var lookup = new O2JamLibraryLookup(database, [new O2JamLibraryWriteRequest(plan with
            {
                SetHash = "unrelated-set-hash",
                SourceHash = "unrelated-source-hash",
            })]);
            Assert.That(lookup.FindPath(path)?.ID, Is.EqualTo(id));
        });
    });

    [Test]
    public void BatchLookupKeepsSameFilenameSourcesInDifferentFoldersSeparate() => runProjectionTest((realm, storage) =>
    {
        string[] paths = [storage.GetFullPath("first/chart.ojn"), storage.GetFullPath("second/chart.ojn")];
        var plans = paths.Select((path, index) =>
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var bytes = OjnTestData.CreateChart();
            BitConverter.GetBytes(index + 400).CopyTo(bytes, 0);
            File.WriteAllBytes(path, bytes);
            return new O2JamImportPlanner().Create(path);
        }).ToArray();
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.WriteBatch(plans.Select(plan => new O2JamLibraryWriteRequest(plan)).ToArray());
        var original = writer.GetImportedSources();
        var updated = plans.Select((plan, index) => new O2JamLibraryWriteRequest(plan with { Title = $"Changed {index}" })).ToArray();
        Assert.That(writer.WriteBatch(updated), Is.All.EqualTo(O2JamLibraryWriteResult.Updated));
        var current = writer.GetImportedSources();
        foreach (var path in paths)
            Assert.That(current[path].SetId, Is.EqualTo(original[path].SetId));
        realm.Run(database =>
        {
            for (var index = 0; index < paths.Length; index++)
                Assert.That(database.Find<BeatmapSetInfo>(current[paths[index]].SetId)!.Beatmaps.Single().Metadata.Title, Is.EqualTo($"Changed {index}"));
        });
    });

    [Test]
    public void BatchLookupSeesRelocatedLegacyContentAfterItsHashChanges() => runProjectionTest((realm, storage) =>
    {
        var original = storage.GetFullPath("legacy.ojn");
        var moved = storage.GetFullPath("moved.ojn");
        var copy = storage.GetFullPath("copy.ojn");
        File.WriteAllBytes(original, OjnTestData.CreateChart());
        var planner = new O2JamImportPlanner();
        var plan = planner.Create(original);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var id = writer.GetImportedSources()[original].SetId;
        realm.Write(database =>
        {
            var set = database.Find<BeatmapSetInfo>(id)!;
            set.Files.Clear();
            set.Beatmaps.Single().Hash = plan.SourceHash.ToUpperInvariant();
            set.Beatmaps.Single().Metadata.Tags = "o2jam";
        });
        File.Move(original, moved);
        File.Copy(moved, copy);
        var results = writer.WriteBatch([new O2JamLibraryWriteRequest(planner.Create(moved)), new O2JamLibraryWriteRequest(planner.Create(copy))]);
        Assert.That(results, Is.EqualTo(new[] { O2JamLibraryWriteResult.Updated, O2JamLibraryWriteResult.AlreadyPresent }));
        Assert.That(writer.GetImportedSources().Keys, Is.EqualTo(new[] { moved }));
        Assert.That(writer.GetImportedSources()[moved].SetId, Is.EqualTo(id));
    });

    [Test]
    public void StoredSetHashCollisionIsRejectedCaseInsensitive() => runProjectionTest((realm, storage) =>
    {
        var original = storage.GetFullPath("unrelated.ojn");
        var replacement = storage.GetFullPath("new-source.ojn");
        var bytes = OjnTestData.CreateChart();
        File.WriteAllBytes(original, bytes);
        BitConverter.GetBytes(550).CopyTo(bytes, 0);
        File.WriteAllBytes(replacement, bytes);
        var planner = new O2JamImportPlanner();
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(planner.Create(original));
        var id = writer.GetImportedSources()[original].SetId;
        var plan = planner.Create(replacement);
        realm.Write(database => database.Find<BeatmapSetInfo>(id)!.Hash = plan.SetHash.ToUpperInvariant());
        Assert.Throws<InvalidDataException>(() => writer.Write(plan));
        Assert.That(writer.GetImportedSources().Keys, Is.EqualTo(new[] { original }));
        Assert.That(writer.GetImportedSources()[original].SetId, Is.EqualTo(id));
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.EqualTo(1));
    });

    [Test]
    public void BatchLookupRejectsSetHashCollisionIntroducedEarlierInTransaction() => runProjectionTest((realm, storage) =>
    {
        var a = storage.GetFullPath("first.ojn");
        var b = storage.GetFullPath("second.ojn");
        var bytes = OjnTestData.CreateChart();
        File.WriteAllBytes(a, bytes);
        BitConverter.GetBytes(500).CopyTo(bytes, 0);
        File.WriteAllBytes(b, bytes);
        var planner = new O2JamImportPlanner();
        var first = planner.Create(a);
        var second = planner.Create(b) with { SetHash = first.SetHash };
        var writer = new O2JamLibraryWriter(realm, storage);
        Assert.Throws<InvalidDataException>(() => writer.WriteBatch([new O2JamLibraryWriteRequest(first), new O2JamLibraryWriteRequest(second)]));
        Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.Zero);
        Assert.That(realm.Run(database => database.All<BeatmapInfo>().Count()), Is.Zero);
    });

    [TestCase(false)]
    [TestCase(true)]
    public void LegacySourceLookupUsesNativeFileOrHashOwnership(bool missingUsage) => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("lookup.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var planner = new O2JamImportPlanner();
        var plan = planner.Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var beatmapId = realm.Run(database => database.All<BeatmapInfo>().Single().ID);
        var scoreId = Guid.NewGuid();
        realm.Write(database =>
        {
            var beatmap = database.Find<BeatmapInfo>(beatmapId)!;
            beatmap.Hash = plan.SourceHash;
            beatmap.Metadata.Tags = "o2jam";
            if (missingUsage)
                beatmap.BeatmapSet!.Files.Clear();
            else
                beatmap.Metadata.AudioFile = string.Empty;
            database.Add(new ScoreInfo(beatmap, beatmap.Ruleset) { ID = scoreId, TotalScore = 1234 });
        });
        var summary = new O2JamImportService(planner, writer).Refresh([path], writer.GetImportedSources());
        Assert.That(summary.Updated, Is.EqualTo(1));
        Assert.That(summary.Imported + summary.Failed, Is.Zero);
        realm.Run(database =>
        {
            Assert.That(database.All<BeatmapSetInfo>().Count(), Is.EqualTo(1));
            Assert.That(database.Find<BeatmapInfo>(beatmapId)!.Hash, Is.Not.EqualTo(plan.SourceHash));
            var score = database.Find<ScoreInfo>(scoreId)!;
            Assert.That(score.BeatmapInfo!.ID, Is.EqualTo(beatmapId));
            Assert.That(score.BeatmapHash, Is.EqualTo(score.BeatmapInfo.Hash));
        });
    });

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
    public void SameBatchCopiesUseNativeLookupAndKeepOneSet(bool reverse) => runProjectionTest((realm, storage) =>
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
        for (var index = 0; index < 18; index++)
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
            Assert.That(summary.Imported, Is.EqualTo(18));
            Assert.That(summary.AlreadyPresent, Is.EqualTo(36));
            Assert.That(summary.Failed, Is.Zero);
            Assert.That(writer.GetImportedSources().Keys.Select(Path.GetFileName), Is.All.StartsWith("a-"));
            Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.EqualTo(18));
        });
        var ids = writer.GetImportedSources().Values.Select(source => source.SetId).ToHashSet();
        summary = service.Refresh(paths, writer.GetImportedSources());
        Assert.That(summary.AlreadyPresent, Is.EqualTo(54));
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
        for (var index = 0; index < 18; index++)
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
            Assert.That(exception.Summary.Updated, Is.EqualTo(16));
            Assert.That(exception.Summary.Failed, Is.Zero);
            Assert.That(invalidated, Has.Count.EqualTo(16));
            Assert.That(writer.GetImportedSources().Values.Count(source => source.HasCurrentMetadata), Is.EqualTo(16));
            Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count()), Is.EqualTo(18));
        });
        Assert.That(service.Refresh(paths, writer.GetImportedSources()).Updated, Is.EqualTo(2));
    });
}
