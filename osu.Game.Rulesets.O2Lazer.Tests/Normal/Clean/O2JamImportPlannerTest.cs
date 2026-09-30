using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using O2Jam.Core;
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
        File.WriteAllBytes(path, OjnTestData.CreateChart());

        var planner = new O2JamImportPlanner();
        var first = planner.Create(path);
        var second = planner.Create(path);

        Assert.Multiple(() =>
        {
            Assert.That(first.SourcePath, Is.EqualTo(Path.GetFullPath(path)));
            Assert.That(first.Title, Is.EqualTo("Clean O2Jam"));
            Assert.That(first.Charts, Has.Count.EqualTo(1));
            Assert.That(first.Slots, Has.Count.EqualTo(3));
            Assert.That(first.Slots.Where(slot => slot.IsPlayable).Select(slot => slot.Difficulty), Is.EqualTo(new[] { O2JamDifficulty.EX }));
            Assert.That(first.Charts[0].Difficulty, Is.EqualTo(O2JamDifficulty.EX));
            Assert.That(first.Charts[0].TotalObjectCount, Is.EqualTo(1));
            Assert.That(first.Charts[0].HoldObjectCount, Is.EqualTo(1));
            Assert.That(first.Charts[0].ManiaStarRating, Is.Zero);
            Assert.That(first.Charts[0].ManiaStarRating, Is.EqualTo(second.Charts[0].ManiaStarRating));
            Assert.That(first.Charts[0].ManiaMaxCombo, Is.GreaterThan(0));
            Assert.That(first.Charts[0].ManiaMaxCombo, Is.EqualTo(second.Charts[0].ManiaMaxCombo));
            Assert.That(first.Charts[0].Length, Is.EqualTo(6000).Within(0.001));
            Assert.That(first.SetHash, Is.EqualTo(second.SetHash));
            Assert.That(first.SetHash, Has.Length.EqualTo(64));
            Assert.That(first.SetHash, Is.Not.EqualTo(string.Concat(first.Charts.Select(chart => chart.Md5Hash))));
            Assert.That(first.Charts[0].Md5Hash, Is.EqualTo(second.Charts[0].Md5Hash));
        });
    }

    [TestCase(false)]
    [TestCase(true)]
    public void RefreshValidatesStoredDifficultyMd5BeforeSkipping(bool corrupted)
    {
        var path = Path.Combine(directory, "identity.ojn");
        File.WriteAllBytes(path, OjnTestData.CreateChart());
        var plan = new O2JamImportPlanner().Create(path);
        var source = new O2JamImportedSource(Guid.NewGuid(), plan.SourceTimestamp, plan.SourceData.Length, true, true, plan.SourceHash,
            DifficultyIdentities: [new O2JamStoredDifficultyIdentity(O2JamDifficulty.EX, corrupted ? "old-md5" : plan.Charts[0].Md5Hash)]);
        Assert.That(O2JamImportService.isUnchanged(path, source), Is.EqualTo(!corrupted));
    }
}
