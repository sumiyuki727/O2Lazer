using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Database;
using osu.Game.Beatmaps;
using osu.Game.Scoring;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void ClearReportsCommittedBatchesAndCancellationPreservesScores(bool cancel) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var foreignId = realm.Write(database =>
        {
            var rule = database.Find<RulesetInfo>(O2LazerIdentity.ShortName)!;
            for (var index = 0; index < 128; index++)
            {
                var set = new BeatmapSetInfo { Hash = $"clear-{index}" };
                set.Beatmaps.Add(new osu.Game.Beatmaps.BeatmapInfo(rule) { BeatmapSet = set });
                database.Add(set);
            }
            var foreign = database.Add(new RulesetInfo("foreign", "Foreign", "test", -1));
            var foreignSet = new BeatmapSetInfo { Hash = "foreign-clear" };
            foreignSet.Beatmaps.Add(new osu.Game.Beatmaps.BeatmapInfo(foreign) { BeatmapSet = foreignSet });
            database.Add(foreignSet);
            return foreignSet.ID;
        });
        using var cancellation = new CancellationTokenSource();
        var counts = new List<int>();
        var writer = new O2JamLibraryWriter(realm, storage);
        Action<O2JamLibraryProgress> progress = value =>
        {
            counts.Add(value.Processed);
            Assert.That(value.Stage, Is.EqualTo(O2JamLibraryStage.ClearingCharts));
            Assert.That(realm.Run(database => database.All<BeatmapSetInfo>().Count(set => set.DeletePending)), Is.EqualTo(value.Processed));
            if (cancel && value.Processed == 128)
                cancellation.Cancel();
        };
        if (cancel)
        {
            Assert.Throws<OperationCanceledException>(() => writer.DeleteAll(progress, cancellation.Token));
            Assert.That(counts, Is.EqualTo(new[] { 0, 128 }));
            Assert.That(writer.DeleteAll(), Is.EqualTo(1));
        }
        else
        {
            Assert.That(writer.DeleteAll(progress, cancellation.Token), Is.EqualTo(129));
            Assert.That(counts, Is.EqualTo(new[] { 0, 128, 129 }));
        }
        Assert.That(realm.Run(database => database.Find<BeatmapSetInfo>(foreignId)!.DeletePending), Is.False);
        Assert.That(realm.Run(database => database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapHash), Is.EqualTo(saved.BeatmapHash));
        Assert.That(realm.Run(database => database.Find<ScoreInfo>(saved.ScoreId)!.Files.Single().File.Hash), Is.EqualTo(saved.ReplayHash));
        Assert.That(writer.DeleteAll(), Is.Zero);
    });

    [Test]
    public void CommittedSourceStarsCanBeWrittenFromIndependentWorker() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var importer = new O2JamLibraryWriter(realm, storage);
        var calculator = new O2JamLibraryWriter(realm, storage);
        using var written = new ManualResetEventSlim();
        calculator.BeatmapUpdated += _ => written.Set();
        using var work = new O2JamLibraryDifficultyPipeline(calculator, null, CancellationToken.None);
        var sources = importer.GetCommittedDifficultySources([new O2JamLibraryWriteRequest(saved.Plan)]);
        Assert.That(sources, Has.Count.EqualTo(1));
        work.Offer(sources[0]);
        Assert.That(written.Wait(TimeSpan.FromSeconds(5)), Is.True);
        work.Complete();
        Assert.That(work.FailedSources, Is.Empty);
        // This synchronous fixture blocks the update frame while the worker commits.
        // Refresh its native read snapshot as the next client frame normally would.
        realm.Run(database => database.Refresh());
        Assert.That(((IO2JamLibraryDifficultyStore)calculator).GetPendingDifficultySources(storage.GetFullPath(""), CancellationToken.None), Is.Empty);
        assertSavedLibrary(realm, storage, saved);
    });
}