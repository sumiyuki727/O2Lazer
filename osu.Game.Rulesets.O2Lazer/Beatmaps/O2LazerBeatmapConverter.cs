using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public class O2LazerBeatmapConverter(IBeatmap beatmap, Ruleset ruleset) : BeatmapConverter<O2LazerHitObject>(beatmap, ruleset)
{
    public override bool CanConvert() => Beatmap is O2LazerBeatmap or O2LazerDecodedBeatmap;

    protected override Beatmap<O2LazerHitObject> CreateBeatmap() => new O2LazerBeatmap();

    protected override Beatmap<O2LazerHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var converted = new O2LazerBeatmap
        {
            BeatmapInfo = original.BeatmapInfo,
            ControlPointInfo = original.ControlPointInfo,
            Breaks = original.Breaks,
            AudioLeadIn = original.AudioLeadIn,
            StackLeniency = original.StackLeniency,
            SpecialStyle = original.SpecialStyle,
            LetterboxInBreaks = original.LetterboxInBreaks,
            WidescreenStoryboard = original.WidescreenStoryboard,
            EpilepsyWarning = original.EpilepsyWarning,
            SamplesMatchPlaybackRate = original.SamplesMatchPlaybackRate,
            DistanceSpacing = original.DistanceSpacing,
            GridSize = original.GridSize,
            TimelineZoom = original.TimelineZoom,
            Countdown = original.Countdown,
            CountdownOffset = original.CountdownOffset,
            Bookmarks = original.Bookmarks,
            BeatmapVersion = original.BeatmapVersion,
        };

        if (original is IO2LazerBeatmap o2lazerSource)
            converted.CopyO2LazerDataFrom(o2lazerSource);

        converted.HitObjects = original.HitObjects
            .OfType<O2LazerHitObject>()
            .Select(h => h.ToTypedHitObject())
            .ToList();

        converted.TimingMap ??= createFallbackTimingMap(converted);
        converted.TickResolution = converted.TimingMap.TickResolution;

        if (converted.TotalColumns <= 0)
            converted.TotalColumns = O2LazerLayout.O2JAM_KEY_COLUMNS;

        converted.LayoutVariant = O2LazerLayoutVariant.O2Jam7K;
        precomputeScrollPositions(converted);
        stampJudgementContextOnHitObjects(converted);
        return converted;
    }

    protected override IEnumerable<O2LazerHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
    {
        if (original is O2LazerHitObject o2lazerObject)
            yield return o2lazerObject.ToTypedHitObject();
    }

    private static O2LazerTimingMap createFallbackTimingMap(O2LazerBeatmap beatmap)
    {
        const double fallback_bpm = 130;

        var tickResolution = beatmap.TickResolution;
        var endTime = beatmap.HitObjects.Count == 0 ? 0 : beatmap.HitObjects.Max(h => h.GetEndTime());
        var endTick = (long)System.Math.Ceiling(System.Math.Max(0, endTime) * fallback_bpm * tickResolution / (60000 * 4));
        var measureCount = System.Math.Max(1, (int)(endTick / tickResolution) + 1);
        var measures = Enumerable.Range(0, measureCount + 1)
            .Select(i => new O2LazerMeasureInfo(i, (long)i * tickResolution, tickResolution, 1));

        return new O2LazerTimingMap(
            tickResolution,
            measures,
            [new O2LazerBpmEvent(0, fallback_bpm, 0)],
            [],
            [],
            []);
    }

    private static void precomputeScrollPositions(O2LazerBeatmap beatmap)
    {
        var timingMap = beatmap.TimingMap;
        if (timingMap == null)
            return;

        foreach (var hitObject in beatmap.HitObjects)
        {
            hitObject.ScrollPositionAtStartTime = timingMap.GetScrollPositionAtTime(hitObject.StartTime);
            if (hitObject is O2LazerLongNote ln)
                ln.ScrollPositionAtEndTime = timingMap.GetScrollPositionAtTime(hitObject.GetEndTime());
        }
    }

    private static void stampJudgementContextOnHitObjects(O2LazerBeatmap beatmap)
    {
        foreach (var hitObject in beatmap.HitObjects)
            hitObject.Beatmap = beatmap;
    }
}
