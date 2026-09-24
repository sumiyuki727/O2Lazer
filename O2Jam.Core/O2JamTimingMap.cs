using System;
using System.Collections.Generic;
using System.Linq;

namespace osu.Game.Rulesets.O2Lazer.Core;

public readonly record struct O2JamBpmEvent(double Position, double Bpm);

/// <summary>
/// Converts between elapsed real time and integrated O2Jam measure position.
/// One position unit is one full measure (192 O2Jam ticks).
/// </summary>
public sealed class O2JamTimingMap
{
    public const int TicksPerMeasure = 192;

    private const double milliseconds_per_minute = 60_000;
    private const double beats_per_measure = 4;

    private readonly Segment[] segments;

    public double InitialBpm { get; }

    public IReadOnlyList<O2JamBpmEvent> Events { get; }

    public O2JamTimingMap(double initialBpm, IEnumerable<O2JamBpmEvent>? events = null)
    {
        validateBpm(initialBpm, nameof(initialBpm));
        InitialBpm = initialBpm;

        var normalisedEvents = (events ?? [])
                               .Select((e, index) => (Event: e, Index: index))
                               .OrderBy(e => e.Event.Position)
                               .ThenBy(e => e.Index)
                               .GroupBy(e => e.Event.Position)
                               .Select(group => group.Last().Event)
                               .ToArray();

        Events = normalisedEvents;

        if (normalisedEvents.Any(e => e.Position < 0))
            throw new ArgumentOutOfRangeException(nameof(events), "BPM event positions cannot be negative.");

        foreach (var bpmEvent in normalisedEvents)
            validateBpm(bpmEvent.Bpm, nameof(events));

        var result = new List<Segment>();
        var position = 0d;
        var time = 0d;
        var bpm = initialBpm;

        foreach (var bpmEvent in normalisedEvents)
        {
            if (bpmEvent.Position == 0)
            {
                bpm = bpmEvent.Bpm;
                continue;
            }

            result.Add(new Segment(position, time, bpm));
            time += durationForPosition(bpmEvent.Position - position, bpm, 1);
            position = bpmEvent.Position;
            bpm = bpmEvent.Bpm;
        }

        result.Add(new Segment(position, time, bpm));
        segments = [.. result];
    }

    public double PositionAt(double elapsedRealTime, double playbackRate = 1)
    {
        validateRate(playbackRate);

        var baseTime = elapsedRealTime * playbackRate;
        var segment = segmentAtBaseTime(baseTime);
        return segment.Position + positionForDuration(baseTime - segment.BaseTime, segment.Bpm, 1);
    }

    public double TimeAt(double position, double playbackRate = 1)
    {
        validateRate(playbackRate);

        var segment = segmentAtPosition(position);
        var baseTime = segment.BaseTime + durationForPosition(position - segment.Position, segment.Bpm, 1);
        return baseTime / playbackRate;
    }

    public double EffectiveBpmAtTime(double elapsedRealTime, double playbackRate = 1)
    {
        validateRate(playbackRate);
        return segmentAtBaseTime(elapsedRealTime * playbackRate).Bpm * playbackRate;
    }

    public double EffectiveBpmAtPosition(double position, double playbackRate = 1)
    {
        validateRate(playbackRate);
        return segmentAtPosition(position).Bpm * playbackRate;
    }

    public static double TicksToPosition(double ticks) => ticks / TicksPerMeasure;

    public static double PositionToTicks(double position) => position * TicksPerMeasure;

    private Segment segmentAtPosition(double position)
    {
        if (position < 0)
            return segments[0];

        var low = 0;
        var high = segments.Length - 1;

        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (segments[middle].Position <= position)
                low = middle;
            else
                high = middle - 1;
        }

        return segments[low];
    }

    private Segment segmentAtBaseTime(double baseTime)
    {
        if (baseTime < 0)
            return segments[0];

        var low = 0;
        var high = segments.Length - 1;

        while (low < high)
        {
            var middle = (low + high + 1) / 2;
            if (segments[middle].BaseTime <= baseTime)
                low = middle;
            else
                high = middle - 1;
        }

        return segments[low];
    }

    private static double durationForPosition(double position, double bpm, double rate) =>
        position * beats_per_measure * milliseconds_per_minute / (bpm * rate);

    private static double positionForDuration(double duration, double bpm, double rate) =>
        duration * bpm * rate / (beats_per_measure * milliseconds_per_minute);

    private static void validateBpm(double bpm, string parameter)
    {
        if (!double.IsFinite(bpm) || bpm <= 0)
            throw new ArgumentOutOfRangeException(parameter, "BPM must be finite and greater than zero.");
    }

    private static void validateRate(double rate)
    {
        if (!double.IsFinite(rate) || rate <= 0)
            throw new ArgumentOutOfRangeException(nameof(rate), "Playback rate must be finite and greater than zero.");
    }

    private readonly record struct Segment(double Position, double BaseTime, double Bpm);
}
