using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Formats.Ojn;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase(false)]
    [TestCase(true)]
    public void OverBudgetPlansReserveNothingAndKeepExistingUserRecords(bool background) => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var requests = replacementRequests(storage, saved.Plan.SourcePath);
        var original = requests[0].Plan;
        var limits = new OjnReadLimits(original.SourceData.Length, 4);
        requests[0] = requests[0] with { Plan = background
            ? original with { Background = new byte[limits.SourceBytes + 1] }
            : original with { SourceData = new byte[limits.SourceBytes + 1] } };
        var filesBefore = realm.Run(database => database.All<RealmFile>().AsEnumerable().Select(file => file.Hash).ToArray());
        var stages = 0;
        var notices = 0;
        var writer = new O2JamLibraryWriter(realm, storage, (_, _) => stages++, sourceLimits: limits);
        writer.BeatmapUpdated += _ => notices++;
        Assert.That(Assert.Throws<InvalidDataException>(() => writer.WriteBatch(requests))!.Message, Does.Contain("source byte budget"));
        Assert.That(stages, Is.Zero);
        Assert.That(notices, Is.Zero);
        Assert.That(realm.Run(database => database.All<RealmFile>().AsEnumerable().Select(file => file.Hash).ToArray()), Is.EquivalentTo(filesBefore));
        assertSavedLibrary(realm, storage, saved);
    });

    [Test]
    public void FailedSourceScanPreservesScoresReplayAndCollections() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var bytes = File.ReadAllBytes(saved.Plan.SourcePath);
        using (var source = new FileStream(saved.Plan.SourcePath, FileMode.Append)) source.WriteByte(0);
        var limits = new OjnReadLimits(bytes.Length, 4);
        var writer = new O2JamLibraryWriter(realm, storage);
        var summary = new O2JamImportService(new O2JamImportPlanner(limits), writer, _ => { },
            path => O2JamSourceSnapshot.ReadHash(path, limits)).Refresh([saved.Plan.SourcePath], writer.GetImportedSources());
        Assert.That(summary.Failed, Is.EqualTo(1));
        Assert.That(summary.Imported + summary.Updated, Is.Zero);
        assertSavedLibrary(realm, storage, saved);
    });

    [Test]
    public void DifficultySourceReadRejectsExcessBeforeReturningPayload() => runProjectionTest((realm, storage) =>
    {
        var saved = seedRecoveryLibrary(realm, storage);
        var writer = new O2JamLibraryWriter(realm, storage, null,
            sourceLimits: new OjnReadLimits(saved.Plan.SourceData.Length - 1, 4));
        var imported = writer.GetImportedSources().Single();
        var source = new O2JamLibraryDifficultySource(imported.Key, imported.Value);
        Assert.That(Assert.Throws<InvalidDataException>(() => ((IO2JamLibraryDifficultyStore)writer).ReadDifficultySource(source))!.Message,
            Does.Contain("source byte budget"));
        assertSavedLibrary(realm, storage, saved);
    });
}
