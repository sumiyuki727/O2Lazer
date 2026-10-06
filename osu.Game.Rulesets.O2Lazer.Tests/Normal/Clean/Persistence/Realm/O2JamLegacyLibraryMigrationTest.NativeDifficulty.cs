using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using osu.Framework.Audio.Track;
using osu.Framework.Graphics.Textures;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [Test]
    public void NativeBaselineUsesCommittedOjnWithoutGameplayOrAudioAndPublishesFullCache() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("native.ojn");
        var bytes = createThreeSlotSource();
        File.WriteAllBytes(path, bytes);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        File.Delete(path);
        var document = new O2Jam.Formats.Ojn.OjnReader().Read(bytes);
        var infos = realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => beatmap.Detach()).ToArray());
        Assert.That(O2JamNativeDifficultyPersistencePatch.IsInstalled, Is.True);
        foreach (var info in infos)
        {
            O2JamImportMetadata.Read(info.Metadata.Tags, out var projection);
            var expected = O2JamManiaStarRating.CalculateAttributes(new OjnBeatmapFactory().Create(document, projection!.Difficulty), [], false);
            Assert.That(expected.MaxCombo, Is.GreaterThan(info.TotalObjectCount + info.EndTimeObjectCount - 1), "Native LN ticks must not be replaced by the O2Jam combo formula.");
            var native = new StoredDifficultyWorkingBeatmap(info, storage.GetStorageForDirectory("files"));
            var working = new O2JamWorkingBeatmap(native, null!, path);
            var attributes = new O2LazerRuleset().CreateDifficultyCalculator(working).Calculate();
            Assert.That(native.Reads, Is.EqualTo(1));
            Assert.That(attributes.StarRating, Is.EqualTo(expected.StarRating));
            Assert.That(attributes.MaxCombo, Is.EqualTo(expected.MaxCombo));
            realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = attributes.StarRating);
        }
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Has.Count.EqualTo(3));
        Assert.That(((IO2JamLibraryDifficultyStore)writer).GetPendingDifficultySources(storage.GetFullPath(string.Empty), CancellationToken.None), Is.Empty);
    });

    [Test]
    public void NativeBaselineCacheRollsBackWithTheNativeStarTransaction() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("rollback.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        var info = realm.Run(database => database.All<BeatmapInfo>().First().Detach());
        var working = new StoredDifficultyWorkingBeatmap(info, storage.GetStorageForDirectory("files"));
        var calculator = new O2LazerRuleset().CreateDifficultyCalculator(working);
        var attributes = calculator.Calculate();
        Assert.Throws<InvalidOperationException>(() => realm.Write(database =>
        {
            var live = database.Find<BeatmapInfo>(info.ID)!;
            live.StarRating = attributes.StarRating;
            Assert.That(O2JamStarRatingMetadata.ReadManiaMaxCombo(live.Metadata.Tags), Is.EqualTo(attributes.MaxCombo));
            throw new InvalidOperationException("Rollback the native transaction.");
        }));
        realm.Run(database =>
        {
            var live = database.Find<BeatmapInfo>(info.ID)!;
            Assert.That(live.StarRating, Is.EqualTo(-1));
            Assert.That(O2JamStarRatingMetadata.ReadManiaMaxCombo(live.Metadata.Tags), Is.Null);
        });
        attributes = calculator.Calculate();
        realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = attributes.StarRating);
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Has.Count.EqualTo(1));
    });

    [TestCase("ruleset")]
    [TestCase("hash")]
    [TestCase("md5")]
    [TestCase("source")]
    [TestCase("projection")]
    [TestCase("retired")]
    [TestCase("value")]
    public void NativeAttributeHandoffRejectsChangedOrForeignProjection(string change) => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("guard.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        new O2JamLibraryWriter(realm, storage).Write(new O2JamImportPlanner().Create(path));
        var info = realm.Run(database => database.All<BeatmapInfo>().First().Detach());
        var working = new StoredDifficultyWorkingBeatmap(info, storage.GetStorageForDirectory("files"));
        var attributes = new O2LazerRuleset().CreateDifficultyCalculator(working).Calculate();
        realm.Write(database =>
        {
            var live = database.Find<BeatmapInfo>(info.ID)!;
            switch (change)
            {
                case "ruleset": live.Ruleset = database.Add(new ManiaRuleset().RulesetInfo); break;
                case "hash": live.Hash = new string('a', 64); break;
                case "md5": live.MD5Hash = new string('a', 32); break;
                case "source": live.BeatmapSet!.GetFile(live.Metadata.AudioFile)!.File = database.Add(new RealmFile { Hash = new string('a', 64) }); break;
                case "projection": live.Metadata.Tags = live.Metadata.Tags.Replace($"{O2JamImportMetadata.ProjectionPrefix}1:{O2JamImportMetadata.ProjectionVersion}", $"{O2JamImportMetadata.ProjectionPrefix}1:{O2JamImportMetadata.ProjectionVersion + 1}"); break;
                case "retired": live.BeatmapSet!.DeletePending = true; break;
            }
            var value = change == "value" ? attributes.StarRating + 1 : attributes.StarRating;
            live.StarRating = value;
            Assert.That(live.StarRating, Is.EqualTo(value), "The adapter must not intercept the native star write.");
            Assert.That(O2JamStarRatingMetadata.ReadManiaMaxCombo(live.Metadata.Tags), Is.Null);
            Assert.That(O2JamStarRatingMetadata.HasCurrentManiaVersion(live.Metadata.Tags), Is.False);
        });
    });

    [Test]
    public void BaselineHandoffCannotCrossThreadsOrMetadataOnlyModCalculations() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("thread.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        var info = realm.Run(database => database.All<BeatmapInfo>().First().Detach());
        var working = new StoredDifficultyWorkingBeatmap(info, storage.GetStorageForDirectory("files"));
        var calculator = new O2LazerRuleset().CreateDifficultyCalculator(working);
        var attributes = calculator.Calculate();
        Task.Run(() => realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = attributes.StarRating)).GetAwaiter().GetResult();
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Is.Empty);
        attributes = calculator.Calculate([new O2JamModManiaScore()]);
        realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = attributes.StarRating);
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Is.Empty);
        attributes = calculator.Calculate();
        realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = attributes.StarRating);
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Has.Count.EqualTo(1));
        Assert.That(working.Reads, Is.EqualTo(1));
    });

    [Test]
    public void CorruptCommittedNativeBaselineCannotPublishAttributes() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("corrupt-native.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        var plan = new O2JamImportPlanner().Create(path);
        writer.Write(plan);
        File.WriteAllBytes(storage.GetStorageForDirectory("files").GetFullPath(new RealmFile { Hash = plan.SourceHash }.GetStoragePath()), [1, 2, 3]);
        var info = realm.Run(database => database.All<BeatmapInfo>().First().Detach());
        var working = new StoredDifficultyWorkingBeatmap(info, storage.GetStorageForDirectory("files"));
        Assert.Throws<InvalidDataException>(() => new O2LazerRuleset().CreateDifficultyCalculator(working).Calculate());
        realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = 0);
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Is.Empty);
    });

    [Test]
    public void DeferredSnapshotSkipsSourcesCompletedByNativeWorker() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("snapshot.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        var store = new CompletedSnapshotDifficultyStore(writer, () =>
        {
            var infos = realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().Select(beatmap => beatmap.Detach()).ToArray());
            foreach (var info in infos)
            {
                var working = new StoredDifficultyWorkingBeatmap(info, storage.GetStorageForDirectory("files"));
                var attributes = new O2LazerRuleset().CreateDifficultyCalculator(working).Calculate();
                realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = attributes.StarRating);
            }
        });
        Assert.That(new O2JamLibraryDifficultyProcessor(store).Process(storage.GetFullPath(string.Empty), null, CancellationToken.None), Is.Zero);
        Assert.That(writer.GetImportedSources()[path].ManiaCache, Has.Count.EqualTo(3));
    });

    [Test]
    public void DeferredWriteKeepsCacheCompletedAfterItsRead() => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath("late.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        var store = (IO2JamLibraryDifficultyStore)writer;
        var pending = store.GetPendingDifficultySources(storage.GetFullPath(string.Empty), CancellationToken.None).Single();
        var info = realm.Run(database => database.All<BeatmapInfo>().First().Detach());
        var working = new StoredDifficultyWorkingBeatmap(info, storage.GetStorageForDirectory("files"));
        var attributes = new O2LazerRuleset().CreateDifficultyCalculator(working).Calculate();
        realm.Write(database => database.Find<BeatmapInfo>(info.ID)!.StarRating = attributes.StarRating);
        O2JamImportMetadata.Read(info.Metadata.Tags, out var projection);
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        store.WriteDifficulties(pending, [new O2JamImportDifficultyCache(projection!.Difficulty, 99, 999, O2JamManiaStarRating.CacheVersion)], CancellationToken.None);
        Assert.That(notices, Is.Zero);
        Assert.That(writer.GetImportedSources()[path].ManiaCache!.Single().MaxCombo, Is.EqualTo(attributes.MaxCombo));
        Assert.That(writer.GetImportedSources()[path].ManiaCache!.Single().StarRating, Is.EqualTo(attributes.StarRating));
    });

    private sealed class StoredDifficultyWorkingBeatmap(BeatmapInfo info, Storage storage) : WorkingBeatmap(info, null!)
    {
        public int Reads { get; private set; }
        protected override IBeatmap GetBeatmap() => throw new AssertionException("Baseline calculation must not decode gameplay or OJM.");
        public override Texture GetBackground() => throw new AssertionException("Baseline calculation loaded a background.");
        protected override Track GetBeatmapTrack() => throw new AssertionException("Baseline calculation loaded audio.");
        protected override ISkin GetSkin() => throw new AssertionException("Baseline calculation loaded samples.");
        public override Stream GetStream(string storagePath)
        {
            Reads++;
            return storage.GetStream(storagePath);
        }
    }

    private sealed class CompletedSnapshotDifficultyStore(IO2JamLibraryDifficultyStore inner, Action completeNative) : IO2JamLibraryDifficultyStore
    {
        public IReadOnlyList<O2JamLibraryDifficultySource> GetPendingDifficultySources(string path, CancellationToken token)
        {
            var snapshot = inner.GetPendingDifficultySources(path, token);
            completeNative();
            return snapshot;
        }
        public O2JamLibraryDifficultySource RefreshDifficultySource(O2JamLibraryDifficultySource source, CancellationToken token) => inner.RefreshDifficultySource(source, token);
        public byte[] ReadDifficultySource(O2JamLibraryDifficultySource source) => throw new AssertionException("A completed source must not be reread.");
        public void WriteDifficulties(O2JamLibraryDifficultySource source, IReadOnlyList<O2JamImportDifficultyCache> caches, CancellationToken token) => throw new AssertionException("A completed source must not be rewritten.");
    }
}
