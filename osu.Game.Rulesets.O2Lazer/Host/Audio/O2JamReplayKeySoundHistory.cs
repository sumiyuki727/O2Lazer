using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Audio;

internal sealed class O2JamReplayKeySoundHistory
{
    private readonly Dictionary<JudgementResult, O2JamPreviewEvent[]> hits = [];

    internal void Apply(JudgementResult result)
    {
        if (!result.IsHit)
            return;
        var samples = result.HitObject.Samples.OfType<O2JamHitSampleInfo>()
                            .Select(sample => new O2JamPreviewEvent(result.TimeAbsolute, sample.SampleId,
                                                                   sample.Volume, sample.Pan, true, false)).ToArray();
        if (samples.Length > 0)
            hits[result] = samples;
    }

    internal void Revert(JudgementResult result) => hits.Remove(result);

    internal O2JamPreviewEvent[] At(double time) => hits.Values.SelectMany(events => events)
                                                      .Where(evt => evt.Time <= time)
                                                      // Gameplay replaces the preceding voice of the same OJM sample.
                                                      .GroupBy(evt => evt.SampleId)
                                                      .Select(group => group.MaxBy(evt => evt.Time)).ToArray();

    internal void Clear() => hits.Clear();
}
