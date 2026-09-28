using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Testing;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    [Category("Isolated")]
    [Explicit("Bootstraps the native ruleset store; run separately from subsequent Realm lifetime tests in a shared test host.")]
    public void NativeRulesetStorePreservesAssociationsAfterVersionRestart()
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}");
        Exception? failure = null;

        host.Run(new MigrationTestGame(() =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var current = new O2LazerRuleset().RulesetInfo;
                var formerAssembly = new AssemblyName(typeof(O2LazerRuleset).Assembly.FullName!) { Version = new Version(2026, 804, 1, 0) };
                var formerInstantiation = $"{typeof(O2LazerRuleset).FullName}, {formerAssembly.FullName}";
                var beatmapId = Guid.NewGuid();
                var scoreId = Guid.NewGuid();

                realm.Write(database =>
                {
                    var formerRuleset = database.Add(new RulesetInfo(current.ShortName, current.Name, formerInstantiation, current.OnlineID));
                    var beatmap = database.Add(new BeatmapInfo { ID = beatmapId, Ruleset = formerRuleset });
                    database.Add(new ScoreInfo { ID = scoreId, Ruleset = formerRuleset, BeatmapInfo = beatmap, MaxCombo = 123 });
                });

                using var store = new RealmRulesetStore(realm);
                Assert.That(store.GetRuleset(current.ShortName)?.Available, Is.True);

                realm.Run(database =>
                {
                    var ruleset = database.All<RulesetInfo>().Single(info => info.ShortName == current.ShortName);
                    var beatmap = database.Find<BeatmapInfo>(beatmapId)!;
                    var score = database.Find<ScoreInfo>(scoreId)!;

                    Assert.Multiple(() =>
                    {
                        Assert.That(ruleset.InstantiationInfo, Is.EqualTo(current.InstantiationInfo));
                        Assert.That(beatmap.Ruleset, Is.EqualTo(ruleset));
                        Assert.That(score.Ruleset, Is.EqualTo(ruleset));
                        Assert.That(score.BeatmapInfo?.ID, Is.EqualTo(beatmapId));
                        Assert.That(score.MaxCombo, Is.EqualTo(123));
                    });
                });
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            return Task.CompletedTask;
        }));

        if (failure != null)
            throw failure;
    }

    [Test]
    public void RefreshSkipsUnchangedSourcesAndDeletesMissingSources()
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}");
        Exception? failure = null;

        host.Run(new MigrationTestGame(async () =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var sourceDirectory = storage.GetFullPath("refresh-library");
                Directory.CreateDirectory(sourceDirectory);
                var sourcePath = Path.Combine(sourceDirectory, "chart.ojn");
                File.WriteAllBytes(sourcePath, OjnTestData.CreateChart());

                realm.Write(database =>
                {
                    var ruleset = new O2LazerRuleset().RulesetInfo;
                    database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID)
                    {
                        Available = true,
                    });
                });

                var writer = new O2JamLibraryWriter(realm, storage);
                Assert.That(writer.Write(new O2JamImportPlanner().Create(sourcePath)), Is.EqualTo(O2JamLibraryWriteResult.Imported));

                var originalBeatmapId = realm.Run(database => database.All<BeatmapInfo>().Single().ID);
                realm.Write(database =>
                {
                    var beatmap = database.Find<BeatmapInfo>(originalBeatmapId)!;
                    beatmap.Metadata.Tags = string.Join(' ', beatmap.Metadata.Tags.Split(' ')
                        .Where(tag => !tag.StartsWith(O2JamStarRatingMetadata.ManiaVersionPrefix))) + " o2lazer-mania-version:1:0";
                    beatmap.StarRating = 99;
                });
                var outdatedSources = writer.GetImportedSources();
                Assert.That(outdatedSources[sourcePath].HasCurrentMetadata, Is.False);
                Assert.That(outdatedSources[sourcePath].SourceHash, Is.Not.Null.And.Not.Empty);
                var updates = new System.Collections.Generic.List<BeatmapInfo>();
                writer.BeatmapUpdated += updates.Add;
                var migration = new O2JamImportService(new O2JamImportPlanner(), writer).Refresh([sourcePath], outdatedSources);
                Assert.Multiple(() =>
                {
                    Assert.That(migration.Updated, Is.EqualTo(1));
                    Assert.That(migration.Failed, Is.Zero);
                    Assert.That(updates, Has.Count.EqualTo(1));
                    Assert.That(updates[0].IsManaged, Is.False);
                    Assert.That(updates[0].ID, Is.EqualTo(originalBeatmapId));
                    Assert.That(O2JamStarRatingMetadata.ReadMania(updates[0]), Is.Zero);
                    Assert.That(realm.Run(database => O2JamStarRatingMetadata.ReadMania(database.Find<BeatmapInfo>(originalBeatmapId)!)), Is.Zero);
                    Assert.That(writer.GetImportedSources()[sourcePath].HasCurrentMetadata, Is.True);
                });

                realm.Write(database =>
                    database.Find<BeatmapInfo>(originalBeatmapId)!.Metadata.Tags += " o2lazer-o2jam-stars:1:0.5");
                var legacyTagSources = writer.GetImportedSources();
                Assert.That(legacyTagSources[sourcePath].HasCurrentMetadata, Is.False,
                    "Legacy tags must trigger cleanup even when mania stars and the source are current.");
                var cleanup = new O2JamImportService(new O2JamImportPlanner(), writer).Refresh([sourcePath], legacyTagSources);
                Assert.That(cleanup.Updated, Is.EqualTo(1));
                Assert.That(realm.Run(database => database.Find<BeatmapInfo>(originalBeatmapId)!.Metadata.Tags),
                    Does.Not.Contain(O2JamStarRatingMetadata.O2JamTagPrefix));

                var sources = writer.GetImportedSources();
                var archivePath = Path.Combine(sourceDirectory, "o2ma100.ojm");
                File.WriteAllBytes(archivePath,
                    [(byte)'O', (byte)'J', (byte)'M', 0, 0, 0, 0, 0,
                        20, 0, 0, 0, 20, 0, 0, 0, 20, 0, 0, 0]);
                var cachedArchive = OjmArchiveCache.Shared.GetAll(sourcePath, archivePath);
                var progress = new System.Collections.Generic.List<(int Processed, int Total)>();
                var summary = new O2JamImportService(new O2JamImportPlanner(), writer)
                              .Refresh([sourcePath], sources, (processed, total) => progress.Add((processed, total)));

                Assert.Multiple(() =>
                {
                    Assert.That(OjmArchiveCache.Shared.IsCurrentArchive(sourcePath, cachedArchive), Is.False,
                        "An explicit refresh must discard OJM even when the OJN was skipped.");
                    Assert.That(summary.AlreadyPresent, Is.EqualTo(1));
                    Assert.That(summary.Imported + summary.Updated + summary.Failed, Is.Zero);
                    Assert.That(progress, Is.EqualTo(new[] { (0, 1), (1, 1) }));
                    Assert.That(updates, Has.Count.EqualTo(2));
                });

                using (var cancellation = new CancellationTokenSource())
                {
                    cancellation.Cancel();
                    Assert.Throws<OperationCanceledException>(() =>
                        new O2JamImportService(new O2JamImportPlanner(), writer)
                            .Refresh([sourcePath], writer.GetImportedSources(), cancellationToken: cancellation.Token));
                }

                File.Delete(sourcePath);
                new O2JamImportService(new O2JamImportPlanner(), writer).Refresh([], writer.GetImportedSources());

                Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Single().DeletePending), Is.True);
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await Task.CompletedTask;
        }));

        if (failure != null)
            throw failure;
    }

    [Test]
    public void RefreshMovedSourcePreservesBeatmapIdentity()
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}");
        Exception? failure = null;

        host.Run(new MigrationTestGame(async () =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var originalDirectory = storage.GetFullPath("original-library");
                var movedDirectory = storage.GetFullPath("moved-library");
                Directory.CreateDirectory(originalDirectory);
                Directory.CreateDirectory(movedDirectory);
                var originalPath = Path.Combine(originalDirectory, "chart.ojn");
                var movedPath = Path.Combine(movedDirectory, "chart.ojn");
                File.WriteAllBytes(originalPath, OjnTestData.CreateChart());

                realm.Write(database =>
                {
                    var ruleset = new O2LazerRuleset().RulesetInfo;
                    database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID)
                    {
                        Available = true,
                    });
                });

                var writer = new O2JamLibraryWriter(realm, storage);
                var planner = new O2JamImportPlanner();
                var plan = planner.Create(originalPath);
                Assert.That(writer.Write(plan), Is.EqualTo(O2JamLibraryWriteResult.Imported));
                var originalBeatmapId = realm.Run(database => database.All<BeatmapInfo>().Single().ID);
                var sources = writer.GetImportedSources();
                Assert.That(sources[originalPath].SourceHash, Is.EqualTo(plan.SourceHash));

                File.Move(originalPath, movedPath);
                Assert.That(File.ReadAllBytes(movedPath), Is.EqualTo(plan.SourceData));
                var summary = new O2JamImportService(planner, writer).Refresh([movedPath], sources);

                Assert.Multiple(() =>
                {
                    Assert.That(summary.Updated, Is.EqualTo(1));
                    Assert.That(summary.Imported + summary.Failed, Is.Zero);
                    Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Single().DeletePending), Is.False);
                    Assert.That(realm.Run(database => database.All<BeatmapInfo>().Single().ID), Is.EqualTo(originalBeatmapId));
                    Assert.That(writer.GetImportedSources().Keys, Is.EquivalentTo(new[] { movedPath }));
                });
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await Task.CompletedTask;
        }));

        if (failure != null)
            throw failure;
    }

    [Test]
    public void UnchangedLegacySetIsMigratedWithoutReplacingBeatmapIdentity()
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}");
        Exception? failure = null;

        host.Run(new MigrationTestGame(async () =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var sourceDirectory = storage.GetFullPath("external-library");
                Directory.CreateDirectory(sourceDirectory);
                var sourcePath = Path.Combine(sourceDirectory, "chart.ojn");
                File.WriteAllBytes(sourcePath, OjnTestData.CreateChart());
                var plan = new O2JamImportPlanner().Create(sourcePath);
                var originalBeatmapId = Guid.NewGuid();
                var originalScoreId = Guid.NewGuid();

                realm.Write(database =>
                {
                    var ruleset = new O2LazerRuleset().RulesetInfo;
                    database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID)
                    {
                        Available = true,
                    });

                    using var stream = File.OpenRead(sourcePath);
                    var sourceFile = new RealmFileStore(realm, storage).Add(stream, database, preferHardLinks: false);
                    var set = new BeatmapSetInfo { Hash = "legacy-set-hash" };
                    set.Files.Add(new RealmNamedFileUsage(sourceFile, plan.FileName));
                    var beatmap = new BeatmapInfo
                    {
                        ID = originalBeatmapId,
                        Ruleset = database.Find<RulesetInfo>(O2LazerIdentity.ShortName)!,
                        Hash = sourceFile.Hash,
                        MD5Hash = plan.Charts[0].Md5Hash,
                        DifficultyName = "EX Lv.5",
                        Metadata = new BeatmapMetadata
                        {
                            Source = sourceDirectory,
                            Title = "Legacy title",
                        },
                        BeatmapSet = set,
                    };
                    set.Beatmaps.Add(beatmap);
                    database.Add(set);
                    database.Add(new ScoreInfo(beatmap, beatmap.Ruleset, new RealmUser { Username = "Migration test" })
                    {
                        ID = originalScoreId,
                        BeatmapHash = sourceFile.Hash,
                    });
                });

                var result = new O2JamLibraryWriter(realm, storage).Write(plan);
                var migrated = realm.Run(database =>
                {
                    var sets = database.All<BeatmapSetInfo>().Where(set => !set.DeletePending).ToArray();
                    var beatmap = sets.Single().Beatmaps.Single();
                    var score = database.Find<ScoreInfo>(originalScoreId)!;
                    return new
                    {
                        SetCount = sets.Length,
                        sets.Single().Hash,
                        beatmap.ID,
                        beatmap.Metadata.AudioFile,
                        beatmap.Metadata.Tags,
                        beatmap.StarRating,
                        BeatmapHash = beatmap.Hash,
                        ScoreBeatmapHash = score.BeatmapHash,
                        ScoreBeatmapId = score.BeatmapInfo?.ID,
                    };
                });

                Assert.Multiple(() =>
                {
                    Assert.That(result, Is.EqualTo(O2JamLibraryWriteResult.Updated));
                    Assert.That(migrated.SetCount, Is.EqualTo(1));
                    Assert.That(migrated.Hash, Is.EqualTo(plan.SetHash));
                    Assert.That(migrated.ID, Is.EqualTo(originalBeatmapId));
                    Assert.That(migrated.AudioFile, Is.EqualTo(plan.FileName));
                    Assert.That(migrated.Tags, Does.Contain(O2JamLibraryWriter.MetadataMarker));
                    Assert.That(migrated.StarRating, Is.EqualTo(plan.Charts[0].ManiaStarRating));
                    Assert.That(O2JamStarRatingMetadata.ReadO2Jam(migrated.Tags), Is.Null);
                    Assert.That(migrated.BeatmapHash,
                        Is.EqualTo(O2JamBeatmapIdentity.FromSource(plan.SourceHash, plan.Charts.Single().Difficulty)));
                    Assert.That(migrated.ScoreBeatmapHash, Is.EqualTo(migrated.BeatmapHash));
                    Assert.That(migrated.ScoreBeatmapId, Is.EqualTo(originalBeatmapId));
                });
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await Task.CompletedTask;
        }));

        if (failure != null)
            throw failure;
    }

    [Test]
    public void ChangedSourceRetainsHistoricalScoreWithoutAttachingItToNewChart()
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}");
        Exception? failure = null;

        host.Run(new MigrationTestGame(async () =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var sourceDirectory = storage.GetFullPath("changed-library");
                Directory.CreateDirectory(sourceDirectory);
                var sourcePath = Path.Combine(sourceDirectory, "chart.ojn");
                var originalBytes = OjnTestData.CreateChart();
                File.WriteAllBytes(sourcePath, originalBytes);

                realm.Write(database =>
                {
                    var ruleset = new O2LazerRuleset().RulesetInfo;
                    database.Add(new RulesetInfo(ruleset.ShortName, ruleset.Name, ruleset.InstantiationInfo, ruleset.OnlineID)
                    {
                        Available = true,
                    });
                });

                var writer = new O2JamLibraryWriter(realm, storage);
                var planner = new O2JamImportPlanner();
                Assert.That(writer.Write(planner.Create(sourcePath)), Is.EqualTo(O2JamLibraryWriteResult.Imported));
                var cachedDocument = OjnDocumentCache.Shared.Get(sourcePath, O2JamDifficulty.EX);

                var scoreId = Guid.NewGuid();
                var original = realm.Write(database =>
                {
                    var set = database.All<BeatmapSetInfo>().Single();
                    var beatmap = set.Beatmaps.Single();
                    database.Add(new ScoreInfo(beatmap, beatmap.Ruleset, new RealmUser { Username = "Historical score" })
                    {
                        ID = scoreId,
                        MaxCombo = 123,
                    });
                    return (SetId: set.ID, BeatmapId: beatmap.ID, BeatmapHash: beatmap.Hash);
                });

                var importedSources = writer.GetImportedSources();
                var originalTimestamp = O2JamSourceTimestamp.Read(sourcePath);
                var originalFileTime = File.GetLastWriteTimeUtc(sourcePath);
                Assert.That(originalBytes[320], Is.EqualTo(1));
                var changedBytes = originalBytes.ToArray();
                changedBytes[320] = 2;
                File.WriteAllBytes(sourcePath, changedBytes);
                File.SetLastWriteTimeUtc(sourcePath, originalFileTime);
                Assert.That(O2JamSourceTimestamp.Read(sourcePath), Is.EqualTo(originalTimestamp));
                Assert.That(changedBytes.Length, Is.EqualTo(originalBytes.Length));
                Assert.That(OjnDocumentCache.Shared.Get(sourcePath, O2JamDifficulty.EX), Is.SameAs(cachedDocument),
                    "The unchanged file stamp should reproduce the stale-cache condition before refresh.");

                var refresh = new O2JamImportService(planner, writer).Refresh([sourcePath], importedSources);
                Assert.That(OjnDocumentCache.Shared.Get(sourcePath, O2JamDifficulty.EX), Is.Not.SameAs(cachedDocument),
                    "A content-aware refresh must remove the old playback document.");
                Assert.Multiple(() =>
                {
                    Assert.That(refresh.Imported, Is.EqualTo(1));
                    Assert.That(refresh.Failed, Is.Zero);
                });

                var after = realm.Run(database =>
                {
                    var oldSet = database.Find<BeatmapSetInfo>(original.SetId)!;
                    var currentSet = database.All<BeatmapSetInfo>().Single(set => !set.DeletePending);
                    var currentBeatmap = currentSet.Beatmaps.Single();
                    var score = database.Find<ScoreInfo>(scoreId)!;
                    return new
                    {
                        oldSet.DeletePending,
                        CurrentSetId = currentSet.ID,
                        CurrentBeatmapId = currentBeatmap.ID,
                        CurrentBeatmapHash = currentBeatmap.Hash,
                        HistoricalBeatmapId = score.BeatmapInfo?.ID,
                        score.BeatmapHash,
                        score.MaxCombo,
                        CurrentScoreCount = currentBeatmap.Scores.Count(),
                    };
                });

                Assert.Multiple(() =>
                {
                    Assert.That(after.DeletePending, Is.True);
                    Assert.That(after.CurrentSetId, Is.Not.EqualTo(original.SetId));
                    Assert.That(after.CurrentBeatmapId, Is.Not.EqualTo(original.BeatmapId));
                    Assert.That(after.CurrentBeatmapHash, Is.Not.EqualTo(original.BeatmapHash));
                    Assert.That(after.HistoricalBeatmapId, Is.EqualTo(original.BeatmapId));
                    Assert.That(after.BeatmapHash, Is.EqualTo(original.BeatmapHash));
                    Assert.That(after.MaxCombo, Is.EqualTo(123));
                    Assert.That(after.CurrentScoreCount, Is.Zero);
                });

                using var restartedRealm = new RealmAccess(storage, "client.realm");
                var afterCleanup = restartedRealm.Run(database =>
                {
                    var score = database.Find<ScoreInfo>(scoreId)!;
                    return new
                    {
                        OldSetRemoved = database.Find<BeatmapSetInfo>(original.SetId) == null,
                        CurrentSetCount = database.All<BeatmapSetInfo>().Count(),
                        score.BeatmapHash,
                        BeatmapId = score.BeatmapInfo?.ID,
                        score.MaxCombo,
                    };
                });
                Assert.Multiple(() =>
                {
                    Assert.That(afterCleanup.OldSetRemoved, Is.True);
                    Assert.That(afterCleanup.CurrentSetCount, Is.EqualTo(1));
                    Assert.That(afterCleanup.BeatmapHash, Is.EqualTo(original.BeatmapHash));
                    Assert.That(afterCleanup.BeatmapId, Is.Null);
                    Assert.That(afterCleanup.MaxCombo, Is.EqualTo(123));
                });
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await Task.CompletedTask;
        }));

        if (failure != null)
            throw failure;
    }

    [Test]
    public void SourceFolderCollectionsSynchroniseAndDeleteWithoutTouchingUserCollections()
    {
        using var host = new TestRunHeadlessGameHost($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}");
        Exception? failure = null;

        host.Run(new MigrationTestGame(async () =>
        {
            try
            {
                using var storage = new TemporaryNativeStorage($"{nameof(O2JamLegacyLibraryMigrationTest)}-{Guid.NewGuid():N}", host);
                using var realm = new RealmAccess(storage, "client.realm");
                var root = storage.GetFullPath("library");
                var firstSource = Path.Combine(root, "Pack A", "Song");
                var secondSource = Path.Combine(root, "Pack B", "Song");

                realm.Write(database =>
                {
                    var sourceRuleset = new O2LazerRuleset().RulesetInfo;
                    var ruleset = database.Add(new RulesetInfo(
                        sourceRuleset.ShortName,
                        sourceRuleset.Name,
                        sourceRuleset.InstantiationInfo,
                        sourceRuleset.OnlineID)
                    {
                        Available = true,
                    });

                    addSet(database, ruleset, firstSource, "ex", "nx");
                    addSet(database, ruleset, secondSource, "hx");
                    database.Add(new BeatmapCollection("User collection", ["user"]));
                });

                var service = new O2JamSourceFolderCollectionService(realm);
                service.Synchronise(root);
                var collections = realm.Run(database => database.All<BeatmapCollection>()
                                                                 .AsEnumerable()
                                                                 .ToDictionary(
                                                                     collection => collection.Name,
                                                                     collection => collection.BeatmapMD5Hashes.ToArray()));

                Assert.Multiple(() =>
                {
                    Assert.That(collections[O2LazerStrings.SourceFolderCollectionName("Pack A/Song").ToString()],
                        Is.EquivalentTo(new[] { "ex", "nx" }));
                    Assert.That(collections[O2LazerStrings.SourceFolderCollectionName("Pack B/Song").ToString()],
                        Is.EquivalentTo(new[] { "hx" }));
                    Assert.That(collections["User collection"], Is.EquivalentTo(new[] { "user" }));
                });

                realm.Write(database => database.All<BeatmapSetInfo>()
                                                   .AsEnumerable()
                                                   .Single(set => set.Beatmaps.Any(beatmap => beatmap.Metadata.Source == secondSource))
                                                   .DeletePending = true);
                service.Synchronise(root);
                service.DeleteFeatureCollections();

                var remaining = realm.Run(database => database.All<BeatmapCollection>()
                                                               .AsEnumerable()
                                                               .Select(collection => collection.Name)
                                                               .ToArray());
                Assert.That(remaining, Is.EqualTo(new[] { "User collection" }));
            }
            catch (Exception exception)
            {
                failure = exception;
            }

            await Task.CompletedTask;
        }));

        if (failure != null)
            throw failure;
    }

    private static void addSet(Realms.Realm database, RulesetInfo ruleset, string source, params string[] hashes)
    {
        var set = new BeatmapSetInfo();

        foreach (var hash in hashes)
        {
            var beatmap = new BeatmapInfo
            {
                Ruleset = ruleset,
                MD5Hash = hash,
                Metadata = new BeatmapMetadata { Source = source },
                BeatmapSet = set,
            };
            set.Beatmaps.Add(beatmap);
        }

        database.Add(set);
    }

    private partial class MigrationTestGame(Func<Task> work) : Framework.Game
    {
        protected override void LoadComplete()
        {
            base.LoadComplete();
            Scheduler.Add(async () =>
            {
                await work().ConfigureAwait(true);
                Exit();
            });
        }
    }
}
