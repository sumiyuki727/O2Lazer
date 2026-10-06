using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void DifficultyBatchRollsBackMutatedSourcesBeforePublishing(bool cancel) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var path = storage.GetFullPath("cache-second.ojn");
        File.WriteAllBytes(path, recoverySource(200));
        new O2JamLibraryWriter(realm, storage).Write(new O2JamImportPlanner().Create(path));
        using var cancellation = new CancellationTokenSource();
        var inject = true;
        var notices = 0;
        var writer = new O2JamLibraryWriter(realm, storage, (stage, index) =>
        {
            if (!inject || stage != O2JamLibraryWriteStage.DifficultySourceWritten || index != 0) return;
            if (cancel) cancellation.Cancel();
            else throw new IOException("Injected cache transaction failure.");
        });
        writer.BeatmapUpdated += _ =>
        {
            notices++;
            Assert.That(realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().All(beatmap => beatmap.StarRating == 7.5)), Is.True,
                "Notifications must observe every accepted source after the native batch commits.");
        };
        var writes = cacheWrites(writer, storage.GetFullPath(""));
        if (cancel) Assert.Throws<OperationCanceledException>(() => ((IO2JamLibraryDifficultyBatchStore)writer).WriteDifficultyBatch(writes, cancellation.Token));
        else Assert.Throws<IOException>(() => ((IO2JamLibraryDifficultyBatchStore)writer).WriteDifficultyBatch(writes, cancellation.Token));
        Assert.That(realm.Run(database => database.All<BeatmapInfo>().AsEnumerable().All(beatmap => beatmap.StarRating == -1)), Is.True);
        Assert.That(notices, Is.Zero);
        assertSavedLibrary(realm, storage, saved, 2);
        inject = false;
        var results = ((IO2JamLibraryDifficultyBatchStore)writer).WriteDifficultyBatch(writes, CancellationToken.None);
        Assert.That(results.Select(result => result.Failure), Is.All.Null);
        Assert.That(results.Select(result => result.WrittenSlots), Is.All.EqualTo(1));
        Assert.That(notices, Is.EqualTo(2));
        assertSavedLibrary(realm, storage, saved, 2);
    });

    [Test]
    public void ChangedDifficultyRejectsItsWholeSourceAndKeepsOtherSources() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var path = storage.GetFullPath("cache-three.ojn");
        File.WriteAllBytes(path, createThreeSlotSource());
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(new O2JamImportPlanner().Create(path));
        var writes = cacheWrites(writer, storage.GetFullPath(""));
        var changed = writes.Single(write => write.Source.Path == path);
        realm.Write(database => database.Find<BeatmapSetInfo>(changed.Source.Source.SetId)!.Beatmaps[1].MD5Hash = "changed-after-calculation");
        var checks = ((IO2JamLibraryDifficultyBatchStore)writer).RefreshDifficultySources(writes.Select(write => write.Source).ToArray(), CancellationToken.None);
        Assert.That(checks.Single(check => check.Source.Path == path).Failure, Is.InstanceOf<InvalidDataException>());
        var notices = 0;
        writer.BeatmapUpdated += _ => notices++;
        var results = ((IO2JamLibraryDifficultyBatchStore)writer).WriteDifficultyBatch(writes, CancellationToken.None);
        Assert.That(results.Single(result => result.Source.Path == path).Failure, Is.InstanceOf<InvalidDataException>());
        Assert.That(results.Single(result => result.Source.Source.SetId == saved.SetId).WrittenSlots, Is.EqualTo(1));
        Assert.That(realm.Run(database => database.Find<BeatmapSetInfo>(changed.Source.Source.SetId)!.Beatmaps.All(beatmap => beatmap.StarRating == -1)), Is.True,
            "The valid first slot of a rejected source must not leak through the commit.");
        Assert.That(notices, Is.EqualTo(1));
        Assert.That(realm.Run(database => database.Find<BeatmapInfo>(saved.BeatmapId)!.Hash), Is.EqualTo(saved.BeatmapHash));
    });

    private static O2JamLibraryDifficultyCacheWrite[] cacheWrites(O2JamLibraryWriter writer, string path) =>
        ((IO2JamLibraryDifficultyStore)writer).GetPendingDifficultySources(path, CancellationToken.None)
            .Select(source => new O2JamLibraryDifficultyCacheWrite(source, source.Source.DifficultyIdentities!.Select(identity =>
                new O2JamImportDifficultyCache(identity.Difficulty, 7.5, 42, O2JamManiaStarRating.CacheVersion)).ToArray())).ToArray();
}