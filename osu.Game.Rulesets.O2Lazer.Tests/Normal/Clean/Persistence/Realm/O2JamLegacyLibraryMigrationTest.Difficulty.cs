using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Scoring;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void PlayableProjectionCanSkipRefreshBeforeStarsAreCalculated() => runProjectionTest((realm, storage) =>
    {
        var directory = storage.GetFullPath("deferred");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "three.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        using var backend = new O2JamRealmLibraryBackend(realm, storage, null, null);
        Assert.That(backend.Refresh(directory, null, CancellationToken.None).Imported, Is.EqualTo(1));
        var writer = new O2JamLibraryWriter(realm, storage);
        var source = writer.GetImportedSources()[path];
        Assert.That(source.HasCurrentMetadata, Is.True);
        Assert.That(source.ManiaCache, Is.Empty);
        realm.Run(database =>
        {
            foreach (var beatmap in database.All<BeatmapInfo>())
            {
                Assert.That(beatmap.StarRating, Is.EqualTo(-1));
                Assert.That(O2JamStarRatingMetadata.ReadMania(beatmap), Is.Null);
                Assert.That(O2JamStarRatingMetadata.HasCurrentManiaVersion(beatmap.Metadata.Tags), Is.False);
                Assert.That(beatmap.TotalObjectCount, Is.GreaterThan(0));
            }
        });
        Assert.That(backend.Refresh(directory, null, CancellationToken.None).AlreadyPresent, Is.EqualTo(1));
        Assert.That(backend.CalculateDifficulties(directory, null, CancellationToken.None).Failed, Is.Zero);
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Has.Count.EqualTo(3));
        Assert.That(((IO2JamLibraryDifficultyStore)writer).GetPendingDifficultySources(directory, CancellationToken.None), Is.Empty);
    });

    [Test]
    public void DeferredStarsUseCommittedBytesAndPreserveScoresAndIdentity() => runProjectionTest((realm, storage) =>
    {
        var directory = storage.GetFullPath("deferred");
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "three.ojn");
        var bytes = createThreeSlotSource();
        File.WriteAllBytes(path, bytes);
        var writer = new O2JamLibraryWriter(realm, storage);
        var plan = new O2JamImportPlanner().Create(path);
        writer.Write(plan);
        var scoreId = realm.Write(database =>
        {
            var beatmap = database.All<BeatmapInfo>().First();
            var score = database.Add(new ScoreInfo(beatmap, beatmap.Ruleset) { BeatmapHash = beatmap.Hash, TotalScore = 321, MaxCombo = 12 });
            return score.ID;
        });
        var original = realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => (beatmap.ID, beatmap.Hash, beatmap.MD5Hash)).ToArray());
        File.WriteAllBytes(path, [1, 2, 3]);
        var notices = new List<Guid>();
        writer.BeatmapUpdated += beatmap => notices.Add(beatmap.ID);
        Assert.That(new O2JamLibraryDifficultyProcessor(writer).Process(directory, null, CancellationToken.None), Is.Zero);
        Assert.That(notices, Is.EquivalentTo(original.Select(beatmap => beatmap.ID)));
        realm.Run(database =>
        {
            var document = new O2Jam.Formats.Ojn.OjnReader().Read(bytes);
            foreach (var old in original)
            {
                var beatmap = database.Find<BeatmapInfo>(old.ID)!;
                Assert.That(beatmap.Hash, Is.EqualTo(old.Hash));
                Assert.That(beatmap.MD5Hash, Is.EqualTo(old.MD5Hash));
                var projection = O2JamImportMetadata.Read(beatmap.Metadata.Tags, out var metadata);
                Assert.That(projection, Is.EqualTo(O2JamImportMetadataStatus.Valid));
                var projected = new OjnBeatmapFactory().Create(document, metadata!.Difficulty);
                var attributes = O2JamManiaStarRating.CalculateAttributes(projected, [], false);
                Assert.That(beatmap.StarRating, Is.EqualTo(attributes.StarRating));
                Assert.That(O2JamStarRatingMetadata.ReadManiaMaxCombo(beatmap.Metadata.Tags), Is.EqualTo(attributes.MaxCombo));
            }
            var score = database.Find<ScoreInfo>(scoreId)!;
            Assert.That(score.TotalScore, Is.EqualTo(321));
            Assert.That(score.MaxCombo, Is.EqualTo(12));
            Assert.That(score.BeatmapHash, Is.EqualTo(score.BeatmapInfo!.Hash));
        });
    });

    [Test]
    public void CancellingDeferredStarsResumesOnlyMissingSources() => runProjectionTest((realm, storage) =>
    {
        var directory = storage.GetFullPath("deferred");
        Directory.CreateDirectory(directory);
        var writer = new O2JamLibraryWriter(realm, storage);
        foreach (var id in Enumerable.Range(301, O2JamLibraryDifficultyProcessor.BatchSize + 1))
        {
            var path = Path.Combine(directory, $"{id}.ojn");
            var bytes = createThreeSlotSource();
            BitConverter.GetBytes(id).CopyTo(bytes, 0);
            File.WriteAllBytes(path, bytes);
            writer.Write(new O2JamImportPlanner().Create(path));
        }
        using var cancellation = new CancellationTokenSource();
        Action<BeatmapInfo> cancel = _ => cancellation.Cancel();
        writer.BeatmapUpdated += cancel;
        Assert.Throws<OperationCanceledException>(() => new O2JamLibraryDifficultyProcessor(writer).Process(directory, null, cancellation.Token));
        writer.BeatmapUpdated -= cancel;
        var pending = ((IO2JamLibraryDifficultyStore)writer).GetPendingDifficultySources(directory, CancellationToken.None);
        Assert.That(pending, Has.Count.EqualTo(1));
        var noticeCount = 0;
        writer.BeatmapUpdated += _ => noticeCount++;
        Assert.That(new O2JamLibraryDifficultyProcessor(writer).Process(directory, null, CancellationToken.None), Is.Zero);
        Assert.That(noticeCount, Is.EqualTo(3));
        Assert.That(((IO2JamLibraryDifficultyStore)writer).GetPendingDifficultySources(directory, CancellationToken.None), Is.Empty);
    });

    [Test]
    public void CorruptCommittedOjnCannotPublishDifficultyCache() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("corrupt.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var filePath = storage.GetStorageForDirectory("files").GetFullPath(new RealmFile { Hash = plan.SourceHash.ToLowerInvariant() }.GetStoragePath());
        File.WriteAllBytes(filePath, [1, 2, 3]);
        Assert.That(new O2JamLibraryDifficultyProcessor(writer).Process(storage.GetFullPath(string.Empty), null, CancellationToken.None), Is.EqualTo(1));
        Assert.That(realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().All(beatmap => beatmap.StarRating == -1)), Is.True);
    });

    [Test]
    public void RetiredSourceCannotReceiveDeferredDifficultyResults() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("retired.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        var inner = (IO2JamLibraryDifficultyStore)writer;
        var store = new RetiringDifficultyStore(inner, realm);
        Assert.That(new O2JamLibraryDifficultyProcessor(store).Process(storage.GetFullPath(string.Empty), null, CancellationToken.None), Is.EqualTo(1));
        Assert.That(realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().All(beatmap => beatmap.StarRating == -1)), Is.True);
    });

    [Test]
    public void DeferredStarsKeepAlreadyValidSlots() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("partial.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var plan = new O2JamImportPlanner().Create(path);
        plan = plan with { Charts = plan.Charts.Select((chart, index) => index == 0 ? chart with { ManiaStarRating = 7.125, ManiaMaxCombo = 42 } : chart).ToArray() };
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var sources = writer.GetImportedSources();
        Assert.That(sources[path].ManiaCache, Has.Count.EqualTo(1));
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        Assert.That(new O2JamLibraryDifficultyProcessor(writer).Process(storage.GetFullPath(string.Empty), null, CancellationToken.None), Is.Zero);
        Assert.That(notices, Is.EqualTo(2));
        var complete = writer.GetImportedSources()[path].ManiaCache!;
        Assert.That(complete, Has.Count.EqualTo(3));
        Assert.That(complete.Single(cache => cache.Difficulty == plan.Charts[0].Difficulty).StarRating, Is.EqualTo(7.125));
    });
    [Test]
    public void UppercaseNativeDifficultyHashStillAcceptsDeferredCache() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("uppercase.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        realm.Write(database =>
        {
            foreach (var beatmap in database.All<BeatmapInfo>())
                beatmap.Hash = beatmap.Hash.ToUpperInvariant();
        });
        Assert.That(writer.GetImportedSources()[path].HasCurrentMetadata, Is.True);
        Assert.That(new O2JamLibraryDifficultyProcessor(writer).Process(storage.GetFullPath(string.Empty), null, CancellationToken.None), Is.Zero);
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Has.Count.EqualTo(3));
    });
    private sealed class RetiringDifficultyStore(IO2JamLibraryDifficultyStore inner, RealmAccess realm) : IO2JamLibraryDifficultyStore
    {
        public IReadOnlyList<O2JamLibraryDifficultySource> GetPendingDifficultySources(string path, CancellationToken token) => inner.GetPendingDifficultySources(path, token);
        public O2JamLibraryDifficultySource RefreshDifficultySource(O2JamLibraryDifficultySource source, CancellationToken token) => inner.RefreshDifficultySource(source, token);
        public byte[] ReadDifficultySource(O2JamLibraryDifficultySource source)
        {
            var data = inner.ReadDifficultySource(source);
            realm.Write(database => database.Find<BeatmapSetInfo>(source.Source.SetId)!.DeletePending = true);
            return data;
        }
        public void WriteDifficulties(O2JamLibraryDifficultySource source, IReadOnlyList<O2JamImportDifficultyCache> caches, CancellationToken token) => inner.WriteDifficulties(source, caches, token);
    }
}
