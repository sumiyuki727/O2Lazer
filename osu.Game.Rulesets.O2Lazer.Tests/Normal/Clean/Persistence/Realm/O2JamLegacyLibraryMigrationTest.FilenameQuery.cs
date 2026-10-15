using System.IO;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

public partial class O2JamLegacyLibraryMigrationTest
{
    [TestCase("chart'quoted.ojn")]
    [TestCase("chart{value}$0.ojn")]
    [TestCase("\u6d77\u306e\u97f3 \u2460.ojn")]
    [TestCase("O2MA120.OJN")]
    public void PathLookupPreservesParameterizedFilenames(string filename) => runProjectionTest((realm, storage) =>
    {
        var path = storage.GetFullPath(filename);
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        var writer = new O2JamLibraryWriter(realm, storage);
        writer.Write(plan);
        var expected = writer.GetImportedSources()[path].SetId;
        realm.Run(database =>
        {
            // A content match would conceal an omission in the filename branch.
            var lookup = new O2JamLibraryLookup(database, [new O2JamLibraryWriteRequest(plan with
            {
                SourceHash = "unrelated-source-hash",
                SetHash = "unrelated-set-hash",
            })]);
            Assert.That(lookup.FindPath(path)?.ID, Is.EqualTo(expected));
            Assert.That(lookup.FindContent("unrelated-source-hash"), Is.Null);
        });
    });
}
