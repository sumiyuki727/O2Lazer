using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamImportPlannerTest
{
    private string directory = null!;

    [SetUp]
    public void SetUp()
    {
        directory = Path.Combine(Path.GetTempPath(), $"o2lazer-planner-{Guid.NewGuid():N}");
        Directory.CreateDirectory(directory);
    }

    [TearDown]
    public void TearDown() => Directory.Delete(directory, true);

    [Test]
    public void CreatesStablePlanWithoutDatabaseDependency()
    {
        var path = Path.Combine(directory, "chart.ojn");
        File.WriteAllBytes(path, OjnReaderTest.CreateChart());

        var planner = new O2JamImportPlanner();
        var first = planner.Create(path);
        var second = planner.Create(path);

        Assert.Multiple(() =>
        {
            Assert.That(first.SourcePath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(first.Title, Is.EqualTo("Clean O2Jam"));
            Assert.That(first.Charts, Has.Count.EqualTo(1));
            Assert.That(first.Charts[0].Difficulty, Is.EqualTo(O2JamDifficulty.EX));
            Assert.That(first.Charts[0].TotalObjectCount, Is.EqualTo(1));
            Assert.That(first.Charts[0].HoldObjectCount, Is.EqualTo(1));
            Assert.That(first.Charts[0].Length, Is.EqualTo(6000).Within(0.001));
            Assert.That(first.SetHash, Is.EqualTo(second.SetHash));
            Assert.That(first.SetHash, Has.Length.EqualTo(64));
            Assert.That(first.SetHash, Is.Not.EqualTo(string.Concat(first.Charts.Select(chart => chart.Md5Hash))));
            Assert.That(first.Charts[0].Md5Hash, Is.EqualTo(second.Charts[0].Md5Hash));
        });
    }
}
