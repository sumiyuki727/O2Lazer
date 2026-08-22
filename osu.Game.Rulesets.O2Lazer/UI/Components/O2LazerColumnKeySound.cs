using System.Collections.Generic;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics.Containers;
using osu.Game.Audio;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Playback;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;
using osu.Game.Rulesets.O2Lazer.UI.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

public sealed partial class O2LazerColumnKeySound(IReadOnlyList<O2LazerHitObject> hitObjects, HitObjectContainer hitObjectContainer)
    : CompositeDrawable
{
    public override bool IsPresent => false;

    private readonly O2LazerKeySoundCursor cursor = new(hitObjects);

    private readonly IBindable<bool> samplePlaybackDisabled = new Bindable<bool>();

    [Resolved]
    private O2LazerSamplePlayback samplePlayback { get; set; } = null!;

    /// <summary>Triggers a note-hit / LN-tail definition through shared sample playback.</summary>
    public void PlaySample(ushort? sampleKey, int volume = 100)
    {
        // Auto-play owns every keysound at chart time; suppress manual retriggers so they don't double.
        if (O2LazerRulesetRuntime.AutoPlayKeysounds)
            return;

        if (sampleKey is { } key)
            samplePlayback.QueueLivePlay(key, volume);
    }

    public void PlaySampleAtTime(ushort? sampleKey, int volume, double targetTime)
    {
        if (O2LazerRulesetRuntime.AutoPlayKeysounds)
            return;

        if (sampleKey is { } key)
            samplePlayback.SchedulePlayAt(key, volume, targetTime);
    }

    /// <summary>
    /// Plays the next pending note's key-sound on an empty press. Returns false (no sound) when
    /// sample playback is disabled (replay/autoplay) or no pending note has a sample.
    /// </summary>
    public void PlayKeySound()
    {
        if (samplePlaybackDisabled.Value)
            return;

        if (cursor.Next(Time.Current, hasNoteFinished) is not { } hitObject)
            return;

        PlaySample(hitObject.SampleKey, hitObject.SampleVolume);
    }

    /// <summary>Triggers the landmine explosion definition (#WAV00).</summary>
    public void PlayLandmineSound(int volume = 100) => PlaySample(0, volume);

    [BackgroundDependencyLoader(true)]
    private void load(ISamplePlaybackDisabler? samplePlaybackDisabler)
    {
        if (samplePlaybackDisabler == null)
            return;

        samplePlaybackDisabled.BindTo(samplePlaybackDisabler.SamplePlaybackDisabled);
    }

    /// <summary>
    /// A note is "finished" when its drawable has been judged, or when it has scrolled past
    /// without a drawable (expired from the pool). The past-BAD-window case is owned by the
    /// cursor's isPastBadWindow (its first loop skips those before calling this), so it isn't
    /// re-checked here. Scoped to this column's own HitObjectContainer.
    /// </summary>
    private bool hasNoteFinished(O2LazerHitObject hitObject)
    {
        var currentTime = Time.Current;

        DrawableO2LazerHitObject? drawable = null;

        foreach (var (entry, d) in hitObjectContainer.AliveEntries)
        {
            if (entry.HitObject == hitObject && d is DrawableO2LazerHitObject o2lazerD)
            {
                drawable = o2lazerD;
                break;
            }
        }

        if (drawable?.Judged == true)
            return true;

        if (currentTime > hitObject.StartTime && drawable == null)
            return true;

        return false;
    }
}
