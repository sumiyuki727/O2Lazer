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

    internal void UnbindAll()
    {
        speed.UnbindAll();
        adjustPitch.UnbindAll();
        boundSpeed = null;
        boundAdjustPitch = null;
    }

    private void update()
    {
        if (optionalPitchAdjustment)
        {
            adjustments.Frequency.Value = adjustPitch.Value ? speed.Value : 1;
            adjustments.Tempo.Value = adjustPitch.Value ? 1 : speed.Value;
            return;
        }

        adjustments.Frequency.Value = fixedFrequency;
        adjustments.Tempo.Value = speed.Value / fixedFrequency;
    }
}
