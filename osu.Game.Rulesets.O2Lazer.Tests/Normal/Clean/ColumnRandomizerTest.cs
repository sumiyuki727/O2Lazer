using System;
using System.Linq;
using NUnit.Framework;
using osu.Game.Rulesets.O2Lazer.Integration.Beatmaps.Transforms;

namespace osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean;

[TestFixture]
public class ColumnRandomizerTest
{
    // Java 21 vectors; S fixtures execute the pinned, unmodified beatoraja Randomizer.
    [TestCase(0, new[] { 2, 3, 4, 5, 6, 0, 1 })]
    [TestCase(1, new[] { 2, 3, 4, 5, 6, 0, 1 })]
    [TestCase(12345, new[] { 4, 3, 2, 1, 0, 6, 5 })]
    [TestCase(-1, new[] { 5, 4, 3, 2, 1, 0, 6 })]
    [TestCase(int.MaxValue, new[] { 5, 6, 0, 1, 2, 3, 4 })]
    [TestCase(4096, new[] { 3, 2, 1, 0, 6, 5, 4 })]
    public void RotationMatchesReferenceSeedVectors(int seed, int[] expected) =>
        Assert.That(ColumnRandomizer.Rotate(7, seed), Is.EqualTo(expected));

    [Test]
    public void RotationProducesTwelvePatternsAndPreservesCyclicNeighbours()
    {
        var patterns = Enumerable.Range(0, 128).Concat(Enumerable.Range(4096, 128))
            .Select(seed => ColumnRandomizer.Rotate(7, seed)).ToArray();
        Assert.That(patterns.Select(columns => string.Join(",", columns)).Distinct().Count(), Is.EqualTo(12));
        foreach (var columns in patterns)
        {
            Assert.That(columns, Is.EquivalentTo(Enumerable.Range(0, 7)));
            Assert.That(columns, Is.Not.EqualTo(Enumerable.Range(0, 7)));
            Assert.That(columns, Is.Not.EqualTo(Enumerable.Range(0, 7).Reverse()));
            var direction = (columns[1] - columns[0] + 7) % 7;
            Assert.That(direction, Is.AnyOf(1, 6));
            for (var index = 0; index < 7; index++)
                Assert.That((columns[(index + 1) % 7] - columns[index] + 7) % 7, Is.EqualTo(direction));
        }
    }

    [TestCase(0, new[] { 5, 3, 0, 2, 5, 3, 6 })]
    [TestCase(1, new[] { 4, 5, 2, 6, 0, 3, 1 })]
    [TestCase(12345, new[] { 5, 0, 3, 1, 4, 6, 3 })]
    public void NoteShuffleMatchesReferenceSeedVectors(int seed, int[] expected)
    {
        ColumnRandomObject[] objects = [new(0, 0), new(1, 10), new(2, 20), new(3, 40), new(4, 41), new(0, 100), new(1, 110)];
        Assert.That(ColumnRandomizer.ShuffleNotes(objects, 7, seed), Is.EqualTo(expected));
    }

    [TestCase(0, new[] { 5, 4, 1, 4, 0, 2 })]
    [TestCase(1, new[] { 4, 5, 1, 3, 6, 2 })]
    [TestCase(12345, new[] { 5, 4, 2, 4, 0, 1 })]
    public void HoldAndTailTimelineConsumeReferenceSeedSequence(int seed, int[] expected)
    {
        ColumnRandomObject[] objects = [new(0, 0, 100), new(1, 0), new(1, 20), new(2, 80), new(1, 100), new(2, 200)];
        Assert.That(ColumnRandomizer.ShuffleNotes(objects, 7, seed), Is.EqualTo(expected));
    }

    [TestCase(0, new[] { 5, 4, 6, 2, 3, 0, 1, 1, 4, 6, 2, 5, 0, 3, 4, 2, 6, 0, 5, 1, 3 })]
    [TestCase(1, new[] { 4, 5, 2, 1, 6, 0, 3, 1, 5, 4, 0, 3, 6, 2, 2, 5, 3, 6, 0, 1, 4 })]
    [TestCase(12345, new[] { 5, 4, 1, 6, 2, 0, 3, 4, 1, 6, 2, 0, 3, 5, 0, 5, 3, 6, 4, 1, 2 })]
    public void UnavoidableDenseChordsPreserveAllNotesAndReferenceFallback(int seed, int[] expected)
    {
        var objects = new[] { 0, 20, 41 }.SelectMany(time => Enumerable.Range(0, 7)
            .Select(column => new ColumnRandomObject(column, time))).ToArray();
        var columns = ColumnRandomizer.ShuffleNotes(objects, 7, seed);
        Assert.That(columns, Is.EqualTo(expected));
        foreach (var chord in columns.Chunk(7))
            Assert.That(chord, Is.EquivalentTo(Enumerable.Range(0, 7)));
    }

    [TestCase(39.999, false)]
    [TestCase(40, false)]
    [TestCase(40.001, true)]
    public void RepeatProtectionUsesContinuousFortyMillisecondBoundary(double nextTime, bool mayRepeat)
    {
        ColumnRandomObject[] objects = [new(0, 0), new(1, 0), new(0, nextTime)];
        var layouts = Enumerable.Range(0, 32).Select(seed => ColumnRandomizer.ShuffleNotes(objects, 3, seed));
        Assert.That(layouts.Any(columns => columns.Take(2).Contains(columns[2])), Is.EqualTo(mayRepeat));
    }

    [Test]
    public void HoldLaneStaysReservedAtTailAndRecoversBeforeReuse()
    {
        ColumnRandomObject[] objects = [new(0, 0, 100), new(1, 100), new(1, 101)];
        foreach (var seed in Enumerable.Range(0, 32))
        {
            var columns = ColumnRandomizer.ShuffleNotes(objects, 3, seed);
            Assert.That(columns[1], Is.Not.EqualTo(columns[0]));
            Assert.That(columns[2], Is.Not.EqualTo(columns[0]));
            Assert.That(columns[2], Is.Not.EqualTo(columns[1]));
        }
    }

    [Test]
    public void AuthoredNotesInsideOverlappingHoldsKeepTheirOccupiedLane()
    {
        ColumnRandomObject[] objects = [new(0, 0, 100), new(0, 10, 200), new(0, 50), new(1, 100), new(0, 100, 300), new(1, 250)];
        var columns = ColumnRandomizer.ShuffleNotes(objects, 3, 12345);
        Assert.That(new[] { columns[0], columns[1], columns[2], columns[4] }, Is.All.EqualTo(columns[0]));
        Assert.That(columns[3], Is.Not.EqualTo(columns[0]));
        Assert.That(columns[5], Is.Not.EqualTo(columns[0]));
    }

    [Test]
    public void FullyOccupiedAndZeroDurationHoldsKeepAllObjects()
    {
        var objects = Enumerable.Range(0, 7).Select(column => new ColumnRandomObject(column, 0, 100))
            .Concat(Enumerable.Range(0, 7).Select(column => new ColumnRandomObject(column, 50)))
            .Concat(Enumerable.Range(0, 7).Select(column => new ColumnRandomObject(column, 100, 100))).ToArray();
        var columns = ColumnRandomizer.ShuffleNotes(objects, 7, 1);
        Assert.That(columns.Length, Is.EqualTo(objects.Length));
        Assert.That(columns.Take(7), Is.EqualTo(columns.Skip(7).Take(7)));
        Assert.That(columns.Take(7), Is.EqualTo(columns.Skip(14)));
    }

    [Test]
    public void ShuffleIsIndependentOfInputOrderAndSupportsOtherKeyCounts()
    {
        ColumnRandomObject[] objects = [new(0, 100), new(1, 0, 40), new(2, 0), new(3, 40), new(4, 70)];
        var expected = ColumnRandomizer.ShuffleNotes(objects, 5, -12345);
        Assert.That(ColumnRandomizer.ShuffleNotes(objects.Reverse().ToArray(), 5, -12345).Reverse(), Is.EqualTo(expected));
        Assert.That(ColumnRandomizer.ShuffleNotes([], 7, 0), Is.Empty);
        Assert.That(ColumnRandomizer.ShuffleNotes([new(0, 0), new(0, 1)], 1, 0), Is.EqualTo(new[] { 0, 0 }));
    }

    [Test]
    public void InvalidIntervalsFailBeforeAssigningColumns()
    {
        Assert.Throws<ArgumentException>(() => ColumnRandomizer.ShuffleNotes([new(7, 0)], 7, 0));
        Assert.Throws<ArgumentException>(() => ColumnRandomizer.ShuffleNotes([new(0, double.NaN)], 7, 0));
        Assert.Throws<ArgumentException>(() => ColumnRandomizer.ShuffleNotes([new(0, 100, 50)], 7, 0));
    }
}
