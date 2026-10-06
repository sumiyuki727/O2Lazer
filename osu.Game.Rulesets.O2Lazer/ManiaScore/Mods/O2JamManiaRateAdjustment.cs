using osu.Framework.Bindables;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Scoring;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;

namespace osu.Game.Rulesets.O2Lazer.Mods;

internal sealed class O2JamManiaRateAdjustment(BindableNumber<double> speedChange) : IManiaRateAdjustmentMod
{
    public BindableNumber<double> SpeedChange => speedChange;

    public void Apply(HitObject hitObject)
    {
        // Native mania assumes millisecond windows. Only converted MS objects may enter
        // that implementation; O2Jam endpoints retain their position-based judgement.
        if (hitObject is Note { HitWindows: ManiaHitWindows }
            or HoldNote { Head.HitWindows: ManiaHitWindows, Tail.HitWindows: ManiaHitWindows })
            ((IApplicableToHitObject)this).ApplyToHitObject(hitObject);
    }
}
