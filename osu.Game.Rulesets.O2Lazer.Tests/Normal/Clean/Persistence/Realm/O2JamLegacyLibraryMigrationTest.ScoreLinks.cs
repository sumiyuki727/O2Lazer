using System;
using System.IO;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Database;
using osu.Game.Extensions;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Scoring;
using osu.Game.Rulesets;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void ClearReimportRestoresScoreLinksAndReplayAttachments(bool restart) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        Assert.That(new O2JamLibraryWriter(realm, storage).DeleteAll(), Is.EqualTo(1));
        RealmAccess? reopened = null;
        try
        {
            if (restart)
            {
                realm.Dispose();
                reopened = new RealmAccess(storage, "client.realm");
            }
            var current = reopened ?? realm;
            using var backend = new O2JamRealmLibraryBackend(current, storage, null, null);
            var summary = backend.Refresh(storage.GetFullPath(""), null, CancellationToken.None);
            Assert.That(summary.PendingNotifications, Is.Zero);
            Assert.That(summary.Imported, Is.EqualTo(1));
            current.Run(database =>
            {
                var active = database.All<BeatmapInfo>().AsEnumerable().Single(beatmap => !beatmap.BeatmapSet!.DeletePending);
                var score = database.Find<ScoreInfo>(saved.ScoreId)!;
                Assert.Multiple(() =>
                {
                    Assert.That(active.ID, Is.Not.EqualTo(saved.BeatmapId));
                    Assert.That(score.BeatmapInfo?.ID, Is.EqualTo(active.ID));
                    Assert.That(score.BeatmapHash, Is.EqualTo(saved.BeatmapHash));
                    Assert.That(score.TotalScore, Is.EqualTo(12345));
                    Assert.That(score.MaxCombo, Is.EqualTo(123));
                    Assert.That(score.DeletePending, Is.False);
                    Assert.That(score.Files.Single().File.Hash, Is.EqualTo(saved.ReplayHash));
                    Assert.That(active.Scores.Single().ID, Is.EqualTo(saved.ScoreId));
                });
            });
            using var stream = new RealmFileStore(current, storage).Store.GetStream(new RealmFile { Hash = saved.ReplayHash }.GetStoragePath());
            using var bytes = new MemoryStream();
            stream.CopyTo(bytes);
            Assert.That(bytes.ToArray(), Is.EqualTo(recovery_replay));
        }
        finally { reopened?.Dispose(); }
    });

    [TestCase(false)]
    [TestCase(true)]
    public void UnchangedRefreshRestoresAlreadyReimportedScoreLinks(bool detached) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.DeleteAll();
        writer.Write(saved.Plan);
        // Model the installed version's missed reattachment without requiring another clear.
        realm.Write(database => database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapInfo = detached ? null : database.Find<BeatmapInfo>(saved.BeatmapId));
        using var backend = new O2JamRealmLibraryBackend(realm, storage, null, null);
        var summary = backend.Refresh(storage.GetFullPath(""), null, CancellationToken.None);
        Assert.Multiple(() =>
        {
            Assert.That(summary.Imported, Is.Zero);
            Assert.That(summary.Updated, Is.Zero);
        });
        realm.Run(database =>
        {
            var active = database.All<BeatmapInfo>().AsEnumerable().Single(beatmap => !beatmap.BeatmapSet!.DeletePending);
            Assert.That(database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapInfo?.ID, Is.EqualTo(active.ID));
        });
    });

    [Test]
    public void ScoreRestorationPreservesForeignAndDeletedRecords() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.DeleteAll();
        writer.Write(saved.Plan);
        var ids = realm.Write(database =>
        {
            var old = database.Find<BeatmapInfo>(saved.BeatmapId)!;
            var foreignRule = database.Add(new RulesetInfo("foreign-score-links", "Foreign", "test", -1));
            var foreignSet = new BeatmapSetInfo { Hash = "foreign-score-links" };
            var foreignBeatmap = new BeatmapInfo(foreignRule) { BeatmapSet = foreignSet, Hash = saved.BeatmapHash };
            foreignSet.Beatmaps.Add(foreignBeatmap);
            database.Add(foreignSet);
            var foreign = database.Add(new ScoreInfo(foreignBeatmap, foreignRule) { TotalScore = 789 });
            var deleted = database.Add(new ScoreInfo(old, old.Ruleset) { DeletePending = true, TotalScore = 456 });
            return (ForeignScore: foreign.ID, ForeignBeatmap: foreignBeatmap.ID, DeletedScore: deleted.ID);
        });
        using var backend = new O2JamRealmLibraryBackend(realm, storage, null, null);
        var summary = backend.Refresh(storage.GetFullPath(""), null, CancellationToken.None);
        Assert.That(summary.PendingNotifications, Is.Zero);
        realm.Run(database =>
        {
            var active = database.All<BeatmapInfo>().AsEnumerable().Single(beatmap => beatmap.Ruleset.ShortName == "o2lazer" && !beatmap.BeatmapSet!.DeletePending);
            Assert.Multiple(() =>
            {
                Assert.That(database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapInfo!.ID, Is.EqualTo(active.ID));
                Assert.That(database.Find<ScoreInfo>(ids.ForeignScore)!.BeatmapInfo!.ID, Is.EqualTo(ids.ForeignBeatmap));
                Assert.That(database.Find<ScoreInfo>(ids.DeletedScore)!.BeatmapInfo!.ID, Is.EqualTo(saved.BeatmapId));
                Assert.That(database.Find<ScoreInfo>(ids.DeletedScore)!.DeletePending, Is.True);
                Assert.That(database.Find<ScoreInfo>(ids.ForeignScore)!.TotalScore, Is.EqualTo(789));
            });
        });
    });

    [Test]
    public void ScoreRestorationDoesNotAttachChangedSourceHistory() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        new O2JamLibraryWriter(realm, storage).DeleteAll();
        File.WriteAllBytes(saved.Plan.SourcePath, recoverySource(200));
        using var backend = new O2JamRealmLibraryBackend(realm, storage, null, null);
        var summary = backend.Refresh(storage.GetFullPath(""), null, CancellationToken.None);
        Assert.That(summary.PendingNotifications, Is.Zero);
        realm.Run(database =>
        {
            var active = database.All<BeatmapInfo>().AsEnumerable().Single(beatmap => !beatmap.BeatmapSet!.DeletePending);
            var score = database.Find<ScoreInfo>(saved.ScoreId)!;
            Assert.Multiple(() =>
            {
                Assert.That(active.Hash, Is.Not.EqualTo(saved.BeatmapHash));
                Assert.That(score.BeatmapHash, Is.EqualTo(saved.BeatmapHash));
                Assert.That(score.BeatmapInfo?.ID, Is.EqualTo(saved.BeatmapId));
                Assert.That(active.Scores, Is.Empty);
            });
        });
    });

    [Test]
    public void ScoreRestorationDoesNotChooseAmbiguousActiveTargets() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.DeleteAll();
        writer.Write(saved.Plan);
        realm.Write(database =>
        {
            var duplicate = new BeatmapSetInfo { Hash = "ambiguous-score-links" };
            duplicate.Beatmaps.Add(new BeatmapInfo(database.Find<RulesetInfo>("o2lazer")!)
            {
                BeatmapSet = duplicate,
                Hash = saved.BeatmapHash,
            });
            database.Add(duplicate);
        });
        Assert.That(new RulesetLocalScoreRestorer(realm, "o2lazer").Restore(CancellationToken.None), Is.Zero);
        Assert.That(realm.Run(database => database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapInfo!.ID), Is.EqualTo(saved.BeatmapId));
    });

    [Test]
    public void CancelledScoreRestorationKeepsHistoricalAssociation() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.DeleteAll();
        writer.Write(saved.Plan);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => new RulesetLocalScoreRestorer(realm, "o2lazer").Restore(cancellation.Token));
        Assert.That(realm.Run(database => database.Find<ScoreInfo>(saved.ScoreId)!.BeatmapInfo!.ID), Is.EqualTo(saved.BeatmapId));
    });
}
