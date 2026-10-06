using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase(1)]
    [TestCase(2)]
    [TestCase(9)]
    [TestCase(10)]
    public void NumericPathCandidatesKeepLiteralSuffixesAndExcludeWildcardNeighbours(int count) => runProjectionTest((realm, storage) =>
    {
        var planner = new O2JamImportPlanner();
        var writer = new O2JamLibraryWriter(realm, storage);
        var plans = Enumerable.Range(120, 10).Select(number =>
        {
            var path = storage.GetFullPath($"o2ma{number}.ojn");
            File.WriteAllBytes(path, recoverySource((uint)number));
            return planner.Create(path);
        }).ToArray();
        writer.WriteBatch(plans.Select(plan => new O2JamLibraryWriteRequest(plan)).ToArray());
        var neighbour = storage.GetFullPath("o2ma12x.ojn");
        File.WriteAllBytes(neighbour, recoverySource(200));
        writer.Write(planner.Create(neighbour));
        var expected = writer.GetImportedSources();
        realm.Write(database => database.Find<BeatmapSetInfo>(expected[plans[0].SourcePath].SetId)!.Beatmaps.Single().Metadata.AudioFile = "O2MA120.OJN");
        realm.Run(database =>
        {
            var lookup = new O2JamLibraryLookup(database, plans.Take(count).Select(plan => new O2JamLibraryWriteRequest(plan)).ToArray());
            foreach (var plan in plans.Take(count))
                Assert.That(lookup.FindPath(plan.SourcePath)?.ID, Is.EqualTo(expected[plan.SourcePath].SetId));
            Assert.That(lookup.FindPath(neighbour), Is.Null, "A broad native candidate must not become an unrelated path owner.");
            if (count < 10)
                Assert.That(lookup.FindPath(plans[9].SourcePath), Is.Null);
            Assert.That(lookup.PathWildcardGroupCount, Is.EqualTo(count == 10 ? 1 : 0));
        });
    });

    [Test]
    public void NumericGroupCannotHideAPathOwnerAddedAfterTheHint() => runProjectionTest((realm, storage) =>
    {
        var planner = new O2JamImportPlanner();
        var writer = new O2JamLibraryWriter(realm, storage);
        var plans = Enumerable.Range(120, 10).Select(number =>
        {
            var path = storage.GetFullPath($"o2ma{number}.ojn");
            File.WriteAllBytes(path, recoverySource((uint)number));
            return planner.Create(path);
        }).ToArray();
        writer.WriteBatch(plans.Select(plan => new O2JamLibraryWriteRequest(plan)).ToArray());
        var sources = writer.GetImportedSources();
        var other = storage.GetFullPath("new-owner.ojn");
        File.WriteAllBytes(other, recoverySource(300));
        writer.Write(planner.Create(other));
        var owner = writer.GetImportedSources()[other].SetId;
        realm.Write(database => database.Find<BeatmapSetInfo>(owner)!.Beatmaps.Single().Metadata.AudioFile = plans[0].FileName);
        var requests = plans.Select(plan => new O2JamLibraryWriteRequest(plan, sources[plan.SourcePath].SetId)).ToArray();
        Assert.Throws<InvalidDataException>(() => writer.WriteBatch(requests));
        realm.Run(database =>
        {
            Assert.That(database.Find<BeatmapSetInfo>(owner)!.DeletePending, Is.False);
            foreach (var source in sources.Values)
                Assert.That(database.Find<BeatmapSetInfo>(source.SetId)!.DeletePending, Is.False);
        });
    });
}