using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Audio;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModInvert : ManiaModInvert, IApplicableAfterBeatmapConversion
{
    public override LocalisableString Description => O2LazerStrings.ModInvertDescription;

    void IApplicableAfterBeatmapConversion.ApplyToBeatmap(IBeatmap beatmap)
    {
        var o2JamBeatmap = (O2JamBeatmap)beatmap;
        var usesManiaObjects = o2JamBeatmap.HitObjects.All(hitObject => hitObject is not IO2JamJudgedObject && hitObject is not O2JamHoldNote);
        if (usesManiaObjects)
        {
            // MS has already replaced the chart objects, so native inversion preserves all
            // required metadata. Only the O2Jam route needs to add musical judgement positions.
            base.ApplyToBeatmap(beatmap);
            o2JamBeatmap.SourcePositions.Clear();
            return;
        }

        var newObjects = new List<ManiaHitObject>();

        foreach (var column in o2JamBeatmap.HitObjects.GroupBy(hitObject => hitObject.Column))
        {
            var locations = column.OfType<Note>().Select(note => (StartTime: note.StartTime, Samples: note.Samples))
                                  .Concat(column.OfType<HoldNote>().Select(hold => (StartTime: hold.StartTime, Samples: hold.GetNodeSamples(0))))
                                  .OrderBy(location => location.StartTime)
                                  .ToList();

            for (var i = 0; i < locations.Count - 1; i++)
            {
                var duration = locations[i + 1].StartTime - locations[i].StartTime;
                var beatLength = beatmap.ControlPointInfo.TimingPointAt(locations[i + 1].StartTime).BeatLength;
                duration = Math.Max(duration / 2, duration - beatLength / 4);
                var endTime = locations[i].StartTime + duration;

                newObjects.Add(new O2JamHoldNote
                {
                    Column = column.Key,
                    StartTime = locations[i].StartTime,
                    Duration = duration,
                    HeadChartPosition = o2JamBeatmap.TimingMap.PositionAt(locations[i].StartTime),
                    TailChartPosition = o2JamBeatmap.TimingMap.PositionAt(endTime),
                    TimingMap = o2JamBeatmap.TimingMap,
                    NodeSamples = [locations[i].Samples, Array.Empty<HitSampleInfo>()],
                });
            }
        }

        o2JamBeatmap.HitObjects = [.. newObjects.OrderBy(hitObject => hitObject.StartTime)];
        o2JamBeatmap.SourcePositions.Clear();
        o2JamBeatmap.Breaks.Clear();
    }
}
