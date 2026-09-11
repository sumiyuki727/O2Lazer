using System.Collections.Generic;
using System.Linq;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Mods;

namespace osu.Game.Rulesets.O2Lazer.Audio;

internal sealed class O2JamHitSoundRateAdjustments
{
    private readonly AudioAdjustments adjustments = new();
    private readonly BindableDouble speed = new(1);
    private readonly BindableBool adjustPitch = new();
    private IBindable<bool>? playbackDisabled;
    private Bindable<double>? boundSpeed;
    private Bindable<bool>? boundAdjustPitch;
    private double fixedFrequency = 1;
    private bool optionalPitchAdjustment;

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

    internal void Bind(IAdjustableAudioComponent hitSound) => hitSound.BindAdjustments(adjustments);

    internal void BindPlaybackDisabled(IBindable<bool> disabled)
    {
        playbackDisabled?.UnbindAll();
        playbackDisabled = disabled.GetBoundCopy();
        playbackDisabled.BindValueChanged(_ => update(), true);
    }

    internal void UnbindAll()
    {
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
    }
}
