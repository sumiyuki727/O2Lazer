using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.O2Lazer.Integration.Beatmaps.Transforms;

/// <summary>
/// Original seven-key layout transforms, independent of the host clock, hit objects and settings UI.
/// </summary>
public static class O2JamColumnRandomizer
{
    public const int ColumnCount = 7;

    public static int[] Shuffle(int seed)
    {
        var random = new OriginalRandom(seed);
        return random.Shuffle();
    }

    public static int[] Panic(IReadOnlyList<O2JamRandomObject> objects, IReadOnlyList<int> measureTickLengths, int seed)
    {
        var measureCount = measureTickLengths.Count;
        foreach (var hitObject in objects)
        {
            if (hitObject.Column is < 0 or >= ColumnCount || hitObject.Head.Measure < 0 || hitObject.Head.Tick < 0
                || hitObject.Tail is { } tail && (tail.Measure < hitObject.Head.Measure || tail.Tick < 0))
                throw new ArgumentOutOfRangeException(nameof(objects));

            measureCount = Math.Max(measureCount, (hitObject.Tail ?? hitObject.Head).Measure + 1);
        }

        var protectedMeasures = new bool[measureCount];
        var activeHolds = new int[measureCount + 1];
        foreach (var hitObject in objects)
        {
            protectNearBoundary(hitObject.Head);
            if (hitObject.Tail is not { } tail)
                continue;

            protectNearBoundary(tail);
            activeHolds[hitObject.Head.Measure]++;
            activeHolds[tail.Measure]--;
        }

        var held = 0;
        for (var measure = 0; measure < measureCount; measure++)
        {
            held += activeHolds[measure];
            protectedMeasures[measure] |= held > 0;
        }

        // Extend each protected run by one measure, not recursively through the rest of the chart.
        for (var measure = 1; measure < measureCount; measure++)
        {
            if (protectedMeasures[measure - 1] && !protectedMeasures[measure])
                protectedMeasures[measure++] = true;
        }

        var random = new OriginalRandom(seed);
        var initialMapping = random.Shuffle();
        var indices = Enumerable.Range(0, objects.Count).OrderBy(index => objects[index].Head.Measure).ToArray();
        var columns = new int[objects.Count];
        var cursor = 0;
        var mapping = new int[ColumnCount];

        for (var measure = 0; measure < measureCount; measure++)
        {
            // Empty measures still consume a shuffle. Skipping them changes every later seed result.
            if (measure == 0 || !protectedMeasures[measure] || !protectedMeasures[measure - 1])
                random.Shuffle(mapping);

            while (cursor < indices.Length && objects[indices[cursor]].Head.Measure == measure)
            {
                var index = indices[cursor++];
                columns[index] = mapping[initialMapping[objects[index].Column]];
            }
        }

        return columns;

        void protectNearBoundary(O2JamSourcePosition position)
        {
            var length = position.Measure < measureTickLengths.Count ? measureTickLengths[position.Measure] : 192;
            var distance = length - position.Tick;
            if (distance is > 0 and < 6)
                protectedMeasures[position.Measure] = true;
        }
    }

    private struct OriginalRandom(int seed)
    {
        private uint state = unchecked((uint)seed);

        public int[] Shuffle()
        {
            var mapping = new int[ColumnCount];
            Shuffle(mapping);
            return mapping;
        }

        public void Shuffle(int[] mapping)
        {
            for (var column = 0; column < ColumnCount; column++)
                mapping[column] = column;

            for (var column = 0; column < ColumnCount; column++)
            {
                // Both clients use MSVC rand() and seven full-range swaps, not Fisher-Yates.
                state = unchecked(state * 214013 + 2531011);
                var target = (int)((state >> 16) & 0x7fff) % ColumnCount;
                (mapping[column], mapping[target]) = (mapping[target], mapping[column]);
            }
        }
    }
}

public readonly record struct O2JamRandomObject(int Column, O2JamSourcePosition Head, O2JamSourcePosition? Tail = null);
