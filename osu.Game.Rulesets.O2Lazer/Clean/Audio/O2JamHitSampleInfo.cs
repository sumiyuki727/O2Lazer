using System.Collections.Generic;
using osu.Game.Audio;

namespace osu.Game.Rulesets.O2Lazer.Audio;

public sealed class O2JamHitSampleInfo : HitSampleInfo
{
    public int SampleId { get; }

    public float Pan { get; }

    public O2JamHitSampleInfo(int sampleId, int volume, float pan)
        : base($"o2jam-{sampleId}", volume: volume, editorAutoBank: false, useBeatmapSamples: true)
    {
        SampleId = sampleId;
        Pan = pan;
    }

    public override IEnumerable<string> LookupNames
    {
        get
        {
            yield return $"o2jam/{SampleId}";
        }
    }
}
