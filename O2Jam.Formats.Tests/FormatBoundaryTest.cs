using System;
using System.IO;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class FormatBoundaryTest
{
    [Test]
    public void DecoderAssemblyDoesNotReferenceHostOrGameplay()
    {
        var assembly = typeof(OjnReader).Assembly;
        Assert.That(assembly.GetName().Name, Is.EqualTo("O2Jam.Formats"));
        Assert.That(assembly.GetReferencedAssemblies().Select(reference => reference.Name),
            Has.All.Matches<string>(name => name == "netstandard" || name.StartsWith("System", StringComparison.Ordinal)));
    }

    [TestCase(OjnDifficulty.EX)]
    [TestCase(OjnDifficulty.NX)]
    [TestCase(OjnDifficulty.HX)]
    public void SelectedDifficultyPreservesDecodedDataAndCallerStream(OjnDifficulty difficulty)
    {
        var bytes = OjnTestData.CreateChart();
        var reader = new OjnReader();
        var expected = reader.Read(bytes).Charts.Single(chart => chart.Difficulty == difficulty);
        using var stream = new MemoryStream(bytes);
        var actual = reader.ReadChart(stream, difficulty).Charts.Single();

        Assert.Multiple(() =>
        {
            Assert.That(actual.Difficulty, Is.EqualTo(expected.Difficulty));
            Assert.That(actual.Level, Is.EqualTo(expected.Level));
            Assert.That(actual.Notes, Is.EqualTo(expected.Notes));
            Assert.That(actual.BpmEvents, Is.EqualTo(expected.BpmEvents));
            Assert.That(actual.MeasureFractions, Is.EqualTo(expected.MeasureFractions));
            Assert.That(stream.CanRead, Is.True);
        });
    }
}
