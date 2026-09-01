using System;
using System.Collections.Generic;
using System.Threading;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public sealed class O2JamBeatmapConverter : BeatmapConverter<ManiaHitObject>
{
    private readonly O2JamBeatmap? source;

    public O2JamBeatmapConverter(IBeatmap beatmap, Ruleset ruleset)
        : base(beatmap, ruleset)
    {
        source = beatmap as O2JamBeatmap;
    }

    public override bool CanConvert() => Beatmap is O2JamBeatmap;

    protected override Beatmap<ManiaHitObject> ConvertBeatmap(IBeatmap original, CancellationToken cancellationToken)
    {
        var beatmap = base.ConvertBeatmap(original, cancellationToken);
        var objects = new List<ManiaHitObject>(beatmap.HitObjects.Count);

        // The framework converter only clones the beatmap container. Native column mods mutate
        // hit objects, so each play needs fresh objects rather than the cached source's bindables.
        foreach (var hitObject in beatmap.HitObjects)
        {
            cancellationToken.ThrowIfCancellationRequested();
            ManiaHitObject copy = hitObject switch
            {
                O2JamNote note => new O2JamNote
                {
                    ChartPosition = note.ChartPosition,
                    TimingMap = note.TimingMap,
                },
                O2JamHoldNote hold => new O2JamHoldNote
                {
                    Duration = hold.Duration,
                    ReleaseTimingDisabled = hold.ReleaseTimingDisabled,
                    HeadChartPosition = hold.HeadChartPosition,
                    TailChartPosition = hold.TailChartPosition,
                    TimingMap = hold.TimingMap,
                    NodeSamples = [[.. hold.GetNodeSamples(0)], []],
                    PlaySlidingSamples = hold.PlaySlidingSamples,
                },
                _ => throw new InvalidOperationException($"Unsupported O2Jam hit object: {hitObject.GetType().FullName}"),
            };

            copy.StartTime = hitObject.StartTime;
            copy.Column = hitObject.Column;
            copy.Samples = [.. hitObject.Samples];
            objects.Add(copy);
        }

        beatmap.HitObjects = objects;
        return beatmap;
    }

    protected override Beatmap<ManiaHitObject> CreateBeatmap()
    {
        // osu! validates custom rulesets by converting an empty generic beatmap during startup.
        // A harmless fallback keeps that compatibility probe separate from real OJN decoding.
        var beatmap = source == null
            ? new O2JamBeatmap(Core.O2JamDifficulty.EX, new Core.O2JamTimingMap(120))
            : new O2JamBeatmap(source.O2JamDifficulty,
                new Core.O2JamTimingMap(source.TimingMap.InitialBpm, source.TimingMap.Events))
        {
            Level = source?.Level ?? 0,
        };

        if (source != null)
        {
            beatmap.AutomaticAudioEvents.AddRange(source.AutomaticAudioEvents);
            beatmap.MeasureLineTimes.AddRange(source.MeasureLineTimes);
        }

        return beatmap;
    }

    protected override IEnumerable<ManiaHitObject> ConvertHitObject(HitObject original, IBeatmap beatmap, CancellationToken cancellationToken)
    {
        if (original is ManiaHitObject maniaObject && original is IO2JamJudgedObject)
            yield return maniaObject;
        else if (original is O2JamHoldNote holdNote)
            yield return holdNote;
    }
}
