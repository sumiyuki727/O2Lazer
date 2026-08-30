using System.Collections.Generic;
using System.Linq;
using osu.Game.Audio;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Formats.Ojn;

public sealed class OjnBeatmapFactory
{
    public O2JamBeatmap Create(OjnDocument document, O2JamDifficulty difficulty)
    {
        var chart = document.Charts.Single(candidate => candidate.Difficulty == difficulty);
        var timingMap = new O2JamTimingMap(document.Metadata.InitialBpm, chart.BpmEvents);
        var beatmap = new O2JamBeatmap(difficulty, timingMap)
        {
            Level = chart.Level,
        };

        beatmap.Difficulty.OverallDifficulty = System.Math.Clamp((int)chart.Level, 0, 10);
        addTimingPoints(beatmap, document.Metadata.InitialBpm, chart.BpmEvents);
        addMeasureLines(beatmap, chart);

        foreach (var note in chart.Notes)
        {
            if (!note.IsPlayable)
            {
                var kind = note.SampleKind == OjnSampleKind.Background
                    ? O2JamAudioEventKind.Background
                    : O2JamAudioEventKind.KeySound;
                beatmap.AutomaticAudioEvents.Add(new O2JamAudioEvent(timingMap.TimeAt(note.Position), note.SampleId, note.Volume, note.Pan, kind));
                continue;
            }

            var column = note.Channel - 2;
            if (note.Type == OjnNoteType.Hold && note.EndPosition is { } endPosition)
            {
                IList<HitSampleInfo> headSamples = [new O2JamHitSampleInfo(note.SampleId, note.Volume, note.Pan)];
                var startTime = timingMap.TimeAt(note.Position);
                var endTime = timingMap.TimeAt(endPosition);

                beatmap.HitObjects.Add(new O2JamHoldNote
                {
                    StartTime = startTime,
                    Duration = endTime - startTime,
                    Column = column,
                    HeadChartPosition = note.Position,
                    TailChartPosition = endPosition,
                    TimingMap = timingMap,
                    Samples = headSamples,
                    // Release records carry sample references, but O2Jam only sounds the hold head.
                    NodeSamples = [headSamples, []],
                });
            }
            else
            {
                beatmap.HitObjects.Add(new O2JamNote
                {
                    StartTime = timingMap.TimeAt(note.Position),
                    Column = column,
                    ChartPosition = note.Position,
                    TimingMap = timingMap,
                    Samples = [new O2JamHitSampleInfo(note.SampleId, note.Volume, note.Pan)],
                });
            }
        }

        beatmap.HitObjects = beatmap.HitObjects.OrderBy(hitObject => hitObject.StartTime).ToList();
        beatmap.AutomaticAudioEvents.Sort((left, right) => left.Time.CompareTo(right.Time));
        return beatmap;
    }

    private static void addTimingPoints(O2JamBeatmap beatmap, float initialBpm, IReadOnlyList<O2JamBpmEvent> events)
    {
        beatmap.ControlPointInfo.Add(0, new TimingControlPoint { BeatLength = 60_000 / initialBpm });

        var timingMap = beatmap.TimingMap;
        foreach (var bpmEvent in events)
        {
            beatmap.ControlPointInfo.Add(timingMap.TimeAt(bpmEvent.Position), new TimingControlPoint
            {
                BeatLength = 60_000 / bpmEvent.Bpm,
            });
        }
    }

    private static void addMeasureLines(O2JamBeatmap beatmap, OjnChart chart)
    {
        var accumulatedReduction = 0d;
        var fractions = chart.MeasureFractions.ToDictionary(fraction => fraction.Measure, fraction => fraction.Fraction);

        for (var rawMeasure = 0u; rawMeasure <= chart.MeasureCount; rawMeasure++)
        {
            if (rawMeasure > 0 && fractions.TryGetValue(checked((int)rawMeasure), out var fraction))
                accumulatedReduction += 1 - fraction;

            var position = rawMeasure - accumulatedReduction;
            beatmap.MeasureLineTimes.Add(beatmap.TimingMap.TimeAt(position));
        }
    }
}
