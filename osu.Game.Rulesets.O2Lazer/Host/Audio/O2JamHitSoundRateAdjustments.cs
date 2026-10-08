using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Audio;
using osu.Framework.Lists;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.Audio;

internal sealed class O2JamHitSoundRateAdjustments
{
    private static readonly ConditionalWeakTable<Drawable, O2JamHitSoundRateAdjustments> owners = new();
    private readonly AudioAdjustments adjustments = new();
    private readonly BindableDouble channelPlaybackFrequency = new(1);
    private readonly WeakList<SampleChannel> channels = new();
    private readonly Dictionary<string, WeakReference<SampleChannel>> latestKeySounds = new(StringComparer.OrdinalIgnoreCase);
    private readonly WeakList<Drawable> soundContainers = new();
    private readonly BindableDouble speed = new(1);
    private readonly BindableBool adjustPitch = new();
    private IBindable<bool>? playbackDisabled;
    private Bindable<double>? boundSpeed;
    private Bindable<bool>? boundAdjustPitch;
    private GameplayClockContainer? gameplayClock;
    private double fixedFrequency = 1;
    private bool optionalPitchAdjustment;
    internal Action<int>? ReplaceRestoredSample { get; set; }

    public O2JamHitSoundRateAdjustments()
    {
        speed.BindValueChanged(_ => update());
        adjustPitch.BindValueChanged(_ => update());
    }

    internal void Configure(IReadOnlyList<Mod> mods)
    {
        if (boundSpeed != null)
            speed.UnbindFrom(boundSpeed);
        if (boundAdjustPitch != null)
            adjustPitch.UnbindFrom(boundAdjustPitch);
        boundSpeed = null;
        boundAdjustPitch = null;
        fixedFrequency = 1;
        optionalPitchAdjustment = false;

        switch (mods.FirstOrDefault(mod => mod is ModRateAdjust or ModTimeRamp or ModAdaptiveSpeed))
        {
            case O2JamModHalfTime halfTime:
                optionalPitchAdjustment = true;
                speed.BindTo(boundSpeed = halfTime.SpeedChange);
                adjustPitch.BindTo(boundAdjustPitch = halfTime.AdjustPitch);
                break;

            case O2JamModDoubleTime doubleTime:
                optionalPitchAdjustment = true;
                speed.BindTo(boundSpeed = doubleTime.SpeedChange);
                adjustPitch.BindTo(boundAdjustPitch = doubleTime.AdjustPitch);
                break;

            case O2JamModDaycore daycore:
                fixedFrequency = daycore.SpeedChange.Default;
                speed.BindTo(boundSpeed = daycore.SpeedChange);
                break;

            case O2JamModNightcore nightcore:
                fixedFrequency = nightcore.SpeedChange.Default;
                speed.BindTo(boundSpeed = nightcore.SpeedChange);
                break;

            case ModTimeRamp timeRamp:
                optionalPitchAdjustment = true;
                speed.BindTo(boundSpeed = timeRamp.SpeedChange);
                adjustPitch.BindTo(boundAdjustPitch = timeRamp.AdjustPitch);
                break;

            case ModAdaptiveSpeed adaptiveSpeed:
                optionalPitchAdjustment = true;
                speed.BindTo(boundSpeed = adaptiveSpeed.SpeedChange);
                adjustPitch.BindTo(boundAdjustPitch = adaptiveSpeed.AdjustPitch);
                break;

            default:
                speed.Value = 1;
                adjustPitch.Value = false;
                break;
        }

        update();
    }

    internal void Bind(IAdjustableAudioComponent hitSound)
    {
        hitSound.BindAdjustments(adjustments);
        if (hitSound is Drawable drawable)
            RegisterSoundContainer(drawable);
    }

    internal void RegisterSoundContainer(Drawable drawable)
    {
        if (owners.TryGetValue(drawable, out var owner) && ReferenceEquals(owner, this))
            return;

        owners.Remove(drawable);
        owners.Add(drawable, this);
        soundContainers.Add(drawable);
    }

    internal static void BindChannel(DrawableSample sample, SampleChannel channel)
    {
        // Only musical OJM samples inside this gameplay's registered sound containers
        // need voice replacement and tail suspension. Other audio retains native behaviour.
        if (!channel.Name.StartsWith("o2jam/", StringComparison.OrdinalIgnoreCase))
            return;

        for (Drawable? ancestor = sample; ancestor != null; ancestor = ancestor.Parent)
        {
            if (!owners.TryGetValue(ancestor, out var owner))
                continue;

            // The sound's drawable may leave the pool hierarchy before its long tail ends.
            // Keep pause control on the channel itself; do not duplicate its rate/pitch factors.
            channel.AddAdjustment(AdjustableProperty.Frequency, owner.channelPlaybackFrequency);
            owner.channels.Add(channel);

            // Native empty-hit and judgement sounds own different sample instances. Coordinate
            // their voices by OJM identity without changing eligibility, preview or background audio.
            if (owner.latestKeySounds.TryGetValue(channel.Name, out var previous)
                && previous.TryGetTarget(out var oldChannel) && !oldChannel.IsDisposed)
                oldChannel.Stop();
            owner.latestKeySounds[channel.Name] = new WeakReference<SampleChannel>(channel);
            if (int.TryParse(channel.Name.AsSpan("o2jam/".Length), out var sampleId))
                owner.ReplaceRestoredSample?.Invoke(sampleId);
            return;
        }
    }

    internal void BindPlaybackDisabled(IBindable<bool> disabled)
    {
        playbackDisabled?.UnbindAll();
        playbackDisabled = disabled.GetBoundCopy();
        playbackDisabled.BindValueChanged(_ => update(), true);
    }

    internal void BindSeek(GameplayClockContainer? clock)
    {
        if (ReferenceEquals(gameplayClock, clock))
            return;

        if (gameplayClock != null)
            gameplayClock.OnSeek -= StopActiveChannels;

        gameplayClock = clock;
        if (gameplayClock != null)
            gameplayClock.OnSeek += StopActiveChannels;
    }

    internal void StopActiveChannels()
    {
        // Replay seek invalidates long OJM tails even when their native drawable has left the
        // object pool. A normal pause instead preserves the original voice and playback offset.
        foreach (var channel in channels)
        {
            if (!channel.IsDisposed)
                channel.Stop();
        }

        channels.Clear();
        latestKeySounds.Clear();
    }

    internal void UnbindAll()
    {
        BindSeek(null);
        foreach (var container in soundContainers)
        {
            if (owners.TryGetValue(container, out var owner) && ReferenceEquals(owner, this))
                owners.Remove(container);
        }
        soundContainers.Clear();

        // Leaving gameplay is terminal, unlike pausing. Do not leave suspended tails
        // behind to resume in another play or after their owner has been disposed.
        StopActiveChannels();
        speed.UnbindAll();
        adjustPitch.UnbindAll();
        playbackDisabled?.UnbindAll();
        playbackDisabled = null;
        boundSpeed = null;
        boundAdjustPitch = null;
    }

    private void update()
    {
        double frequency;
        double tempo;

        if (optionalPitchAdjustment)
        {
            frequency = adjustPitch.Value ? speed.Value : 1;
            tempo = adjustPitch.Value ? 1 : speed.Value;
        }
        else
        {
            frequency = fixedFrequency;
            tempo = speed.Value / fixedFrequency;
        }

        // O2Jam keysounds can contain long music stems. A zero frequency pauses existing sample
        // channels at their current position while native gameplay suppresses new playback.
        adjustments.Frequency.Value = playbackDisabled?.Value == true ? 0 : frequency;
        adjustments.Tempo.Value = tempo;
        channelPlaybackFrequency.Value = playbackDisabled?.Value == true ? 0 : 1;
    }
}
