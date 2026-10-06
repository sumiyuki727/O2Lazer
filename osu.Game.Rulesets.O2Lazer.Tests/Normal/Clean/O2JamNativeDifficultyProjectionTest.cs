using System;
using System.Linq;
using System.Threading;
using NUnit.Framework;
using O2Jam.Core;
using O2Jam.Formats.Ojn;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class O2JamNativeDifficultyProjectionTest
{
    [TestCase(2)]
    [TestCase(3)]
    [TestCase(4)]
    [TestCase(5)]
    [TestCase(6)]
    [TestCase(7)]
    [TestCase(8)]
    public void DirectNativeProjectionPreservesHoldTimingStarsAndTickCombo(int channel)
    {
        var bytes = OjnTestData.CreateChart();
        BitConverter.GetBytes((ushort)channel).CopyTo(bytes, 316);
        BitConverter.GetBytes((ushort)channel).CopyTo(bytes, 352);
        var document = new OjnReader().Read(bytes);
        var factory = new OjnBeatmapFactory();
        foreach (var difficulty in Enum.GetValues<O2JamDifficulty>())
        {
            var original = factory.Create(document, difficulty);
            var projection = factory.CreateDifficultyProjection(document, difficulty);
            Assert.That(projection.HitObjects.Select(note => (note.StartTime, note.Column, Duration: (note as HoldNote)?.Duration)),
                Is.EqualTo(original.HitObjects.Select(note => (note.StartTime, note.Column, Duration: (note as HoldNote)?.Duration))));
            Assert.That(projection.HitObjects.All(note => note.GetType() == typeof(Note) || note.GetType() == typeof(HoldNote)), Is.True);
            var expected = O2JamManiaStarRating.CalculateAttributes(original, [], false);
            var actual = O2JamManiaStarRating.CalculateBaselineAttributes(projection);
            Assert.Multiple(() =>
            {
                Assert.That(actual.StarRating, Is.EqualTo(expected.StarRating));
                Assert.That(actual.MaxCombo, Is.EqualTo(expected.MaxCombo));
            });
        }
    }

    [Test]
    public void NativeBaselineCalculationHonoursCancellation()
    {
        var projection = new OjnBeatmapFactory().CreateDifficultyProjection(new OjnReader().Read(OjnTestData.CreateChart()), O2JamDifficulty.EX);
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => O2JamManiaStarRating.CalculateBaselineAttributes(projection, cancellation.Token));
    }
}