using System;
using System.Linq;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Integration.Beatmaps.Transforms;

public static class O2JamRandomBeatmapTransform
{
    public static void Shuffle(O2JamBeatmap beatmap, int seed) => applyMapping(beatmap, O2JamColumnRandomizer.Shuffle(seed));

    public static void Rotate(O2JamBeatmap beatmap, int seed) => applyMapping(beatmap, ColumnRandomizer.Rotate(7, seed));

    private static void applyMapping(O2JamBeatmap beatmap, int[] mapping)
    {
        foreach (var hitObject in beatmap.HitObjects)
            hitObject.Column = mapping[hitObject.Column];
    }

    public static void ShuffleNotes(O2JamBeatmap beatmap, int seed)
    {
        var objects = beatmap.HitObjects.Select(hitObject => new ColumnRandomObject(hitObject.Column,
            hitObject.StartTime, hitObject is HoldNote hold ? hold.EndTime : null)).ToArray();
        var columns = ColumnRandomizer.ShuffleNotes(objects, 7, seed);
        for (var index = 0; index < columns.Length; index++)
            beatmap.HitObjects[index].Column = columns[index];
    }

    public static void Panic(O2JamBeatmap beatmap, int seed)
    {
        var objects = beatmap.HitObjects.Select(hitObject =>
        {
            var source = beatmap.SourcePositions.TryGetValue(hitObject, out var position)
                ? position
                : generatedPosition(beatmap, hitObject);
            // Normalisation can pair malformed overlapping packages in a different raw-measure order.
            // Preserve that playable hold rather than sending a backwards interval to the transform.
            if (source.Tail is { } tail && tail.Measure < source.Head.Measure)
                source = generatedPosition(beatmap, hitObject);

            return new O2JamRandomObject(hitObject.Column, source.Head, source.Tail);
        }).ToArray();
        var columns = O2JamColumnRandomizer.Panic(objects, beatmap.SourceMeasureTickLengths, seed);

        for (var index = 0; index < columns.Length; index++)
            beatmap.HitObjects[index].Column = columns[index];
    }

    private static O2JamObjectSourcePosition generatedPosition(O2JamBeatmap beatmap, ManiaHitObject hitObject) =>
        new(positionAt(beatmap, hitObject.StartTime), hitObject is HoldNote hold ? positionAt(beatmap, hold.EndTime) : null);

    private static O2JamSourcePosition positionAt(O2JamBeatmap beatmap, double time)
    {
        // Host-generated objects (e.g. Invert) have no authored OJN slot. Only these use musical time.
        var position = beatmap.TimingMap.PositionAt(time);
        if (beatmap.MeasureLineTimes.Count == 0)
        {
            var measure = Math.Max(0, (int)Math.Floor(position));
            return new O2JamSourcePosition(measure, Math.Max(0, (int)Math.Floor((position - measure) * 192)));
        }

        var rawMeasure = beatmap.MeasureLineTimes.BinarySearch(time);
        if (rawMeasure < 0)
            rawMeasure = Math.Max(0, ~rawMeasure - 1);
        var offset = position - beatmap.TimingMap.PositionAt(beatmap.MeasureLineTimes[rawMeasure]);
        if (rawMeasure == beatmap.MeasureLineTimes.Count - 1 && offset >= 1)
        {
            var extra = (int)Math.Floor(offset);
            rawMeasure += extra;
            offset -= extra;
        }

        return new O2JamSourcePosition(rawMeasure, Math.Max(0, (int)Math.Floor(offset * 192 + 1e-7)));
    }
}
