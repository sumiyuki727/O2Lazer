using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.O2Lazer.Integration.Beatmaps.Transforms;

public static class ColumnRandomizer
{
    public const double MinimumRepeatInterval = 40;

    public static int[] Rotate(int columnCount, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columnCount, 2);
        var random = new JavaSeededRandom(seed);
        var forward = random.Next(2) == 1;
        var start = random.Next(columnCount - 1) + (forward ? 1 : 0);
        var mapping = new int[columnCount];
        // The reference describes destination-to-source order; hit objects require its inverse.
        for (var destination = 0; destination < columnCount; destination++)
        {
            var source = (start + (forward ? destination : columnCount - destination)) % columnCount;
            mapping[source] = destination;
        }
        return mapping;
    }

    public static int[] ShuffleNotes(IReadOnlyList<ColumnRandomObject> objects, int columnCount, int seed)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(columnCount, 1);
        var timelines = new SortedDictionary<double, Timeline>();
        var columns = new int[objects.Count];
        for (var index = 0; index < objects.Count; index++)
        {
            var hitObject = objects[index];
            if (hitObject.Column < 0 || hitObject.Column >= columnCount || !double.IsFinite(hitObject.StartTime)
                || hitObject.EndTime is { } end && (!double.IsFinite(end) || end < hitObject.StartTime))
                throw new ArgumentException("Invalid column or time interval.", nameof(objects));
            timelineAt(hitObject.StartTime).Heads.Add(index);
            if (hitObject.EndTime is { } tail)
                timelineAt(tail).Tails.Add(index);
        }

        var random = new JavaSeededRandom(seed);
        var freeSources = Enumerable.Range(0, columnCount).ToList();
        var freeDestinations = Enumerable.Range(0, columnCount).ToList();
        var heldDestinations = Enumerable.Repeat(-1, columnCount).ToArray();
        var heldUntil = new double[columnCount];
        var lastNoteTimes = Enumerable.Repeat(double.NegativeInfinity, columnCount).ToArray();

        foreach (var (time, timeline) in timelines)
        {
            var present = new bool[columnCount];
            foreach (var index in timeline.Heads)
                present[objects[index].Column] = true;
            var mapping = (int[])heldDestinations.Clone();
            var preferred = freeDestinations.Where(column => time - lastNoteTimes[column] > MinimumRepeatInterval).ToList();
            var recent = freeDestinations.Where(column => time - lastNoteTimes[column] <= MinimumRepeatInterval).ToList();

            foreach (var source in freeSources.Where(column => present[column]))
            {
                int destination;
                if (preferred.Count > 0)
                    destination = takeRandom(preferred);
                else
                {
                    // Dense chords can exhaust safe lanes; keep every note and maximise recovery time.
                    var oldest = recent.Min(column => lastNoteTimes[column]);
                    var candidates = recent.Where(column => lastNoteTimes[column] == oldest).ToList();
                    destination = takeRandom(candidates);
                    recent.Remove(destination);
                }
                mapping[source] = destination;
            }

            // Empty lanes also consume the seeded sequence, as in the reference timeline shuffle.
            preferred.AddRange(recent);
            foreach (var source in freeSources.Where(column => !present[column]))
                mapping[source] = takeRandom(preferred);

            foreach (var index in timeline.Heads)
            {
                var hitObject = objects[index];
                var source = hitObject.Column;
                var destination = columns[index] = mapping[source];
                lastNoteTimes[destination] = time;
                if (hitObject.EndTime is not { } end)
                    continue;
                if (heldDestinations[source] < 0)
                {
                    heldDestinations[source] = destination;
                    heldUntil[source] = end;
                    freeSources.Remove(source);
                    freeDestinations.Remove(destination);
                }
                else
                    heldUntil[source] = Math.Max(heldUntil[source], end);
            }

            // A tail is still occupied at this timestamp. Release only after assigning its chord.
            foreach (var index in timeline.Tails)
                lastNoteTimes[columns[index]] = time;
            for (var source = 0; source < columnCount; source++)
            {
                if (heldDestinations[source] < 0 || heldUntil[source] > time)
                    continue;
                freeSources.Add(source);
                freeDestinations.Add(heldDestinations[source]);
                heldDestinations[source] = -1;
            }
        }
        return columns;

        Timeline timelineAt(double time)
        {
            if (!timelines.TryGetValue(time, out var timeline))
                timelines.Add(time, timeline = new Timeline());
            return timeline;
        }

        int takeRandom(List<int> candidates)
        {
            var index = random.Next(candidates.Count);
            var result = candidates[index];
            candidates.RemoveAt(index);
            return result;
        }
    }

    private sealed class Timeline
    {
        public List<int> Heads { get; } = [];
        public List<int> Tails { get; } = [];
    }

    // Fix the reference PRNG explicitly so runtime upgrades cannot change seeded replay layouts.
    private sealed class JavaSeededRandom(int seed)
    {
        private const ulong multiplier = 0x5deece66d;
        private const ulong mask = (1UL << 48) - 1;
        private ulong state = (unchecked((ulong)(long)seed) ^ multiplier) & mask;

        public int Next(int bound)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(bound, 1);
            if ((bound & (bound - 1)) == 0)
                return (int)((bound * (long)nextBits()) >> 31);
            int bits, value;
            do
            {
                bits = nextBits();
                value = bits % bound;
            } while (unchecked(bits - value + (bound - 1)) < 0);
            return value;
        }

        private int nextBits()
        {
            state = unchecked(state * multiplier + 0xb) & mask;
            return (int)(state >> 17);
        }
    }
}

public readonly record struct ColumnRandomObject(int Column, double StartTime, double? EndTime = null);
