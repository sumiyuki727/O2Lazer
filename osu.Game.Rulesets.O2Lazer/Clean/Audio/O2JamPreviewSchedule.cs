using System.Collections.Generic;
using System.Linq;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Objects;

namespace osu.Game.Rulesets.O2Lazer.Audio;

/// <summary>
/// Builds an audio event schedule without depending on an audio device or playback state.
/// </summary>
public sealed record O2JamPreviewSchedule(
    IReadOnlyList<O2JamPreviewEvent> BackgroundEvents,
    IReadOnlyList<O2JamPreviewEvent> PreviewEvents)
{
    public static O2JamPreviewSchedule Create(O2JamBeatmap beatmap, bool includeKeysounds)
    {
        var backgroundEvents = beatmap.AutomaticAudioEvents
                                      .Where(evt => evt.Kind == O2JamAudioEventKind.Background)
                                      .Select(evt => new O2JamPreviewEvent(evt.Time, evt.SampleId, evt.Volume, evt.Pan, false, true))
                                      .OrderBy(evt => evt.Time)
                                      .ToArray();

        var automaticKeySoundEvents = beatmap.AutomaticAudioEvents
                                              .Where(evt => evt.Kind == O2JamAudioEventKind.KeySound)
                                              .Select(evt => new O2JamPreviewEvent(evt.Time, evt.SampleId, evt.Volume, evt.Pan, true, true));

        var previewEvents = includeKeysounds
            ? backgroundEvents.Concat(automaticKeySoundEvents)
                              .Concat(createPlayableEvents(beatmap))
                              .OrderBy(evt => evt.Time)
                              .ThenBy(evt => evt.IsKeySound)
                              .ToArray()
            : backgroundEvents.Concat(automaticKeySoundEvents)
                              .OrderBy(evt => evt.Time)
                              .ThenBy(evt => evt.IsKeySound)
                              .ToArray();

        return new O2JamPreviewSchedule(backgroundEvents, previewEvents);
    }

    private static IEnumerable<O2JamPreviewEvent> createPlayableEvents(O2JamBeatmap beatmap)
    {
        foreach (var hitObject in beatmap.HitObjects)
        {
            if (hitObject is O2JamHoldNote hold)
            {
                foreach (var sample in hold.GetNodeSamples(0).OfType<O2JamHitSampleInfo>())
                    yield return new O2JamPreviewEvent(hold.StartTime, sample.SampleId, sample.Volume, sample.Pan, true, false);
            }
            else
            {
                foreach (var sample in hitObject.Samples.OfType<O2JamHitSampleInfo>())
                    yield return new O2JamPreviewEvent(hitObject.StartTime, sample.SampleId, sample.Volume, sample.Pan, true, false);
            }
        }
    }
}

public readonly record struct O2JamPreviewEvent(
    double Time,
    int SampleId,
    int Volume,
    float Pan,
    bool IsKeySound,
    bool IsAutomatic);
