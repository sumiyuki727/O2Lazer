using System.Collections.Generic;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Beatmaps.ControlPoints;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

internal static class O2LazerBeatmapDecoder
{
    internal static O2LazerHitObject CreateHitObject(O2LazerParsedHitObject parsedObject, IO2LazerBeatmap beatmap)
    {
        var hitObject = O2LazerHitObject.CreateForKind(parsedObject.IsLongNote, false);
        hitObject.Beatmap = beatmap;

        hitObject.StartTime = parsedObject.StartTime;
        hitObject.Column = parsedObject.Column;
        hitObject.SourceChannel = parsedObject.SourceChannel;
        hitObject.SampleKey = parsedObject.SampleKey;
        hitObject.SampleVolume = parsedObject.SampleVolume;
        hitObject.JudgementRate = parsedObject.JudgementRate;

        if (hitObject is O2LazerLongNote ln)
        {
            ln.Duration = parsedObject.Duration;
        }

        return hitObject;
    }

    internal static void PopulateTiming(Beatmap output, IEnumerable<O2LazerBpmEvent> timingEvents)
    {
        output.ControlPointInfo.Clear();

        foreach (var timingEvent in timingEvents.GroupBy(e => e.Tick).Select(g => g.Last()))
        {
            output.ControlPointInfo.Add(timingEvent.Time, new TimingControlPoint
            {
                BeatLength = 60000 / timingEvent.Bpm,
            });
        }
    }
}
