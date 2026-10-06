using osu.Framework.Localisation;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModHalfTime : ManiaModHalfTime, IApplicableToHitObject
{
    private O2JamManiaRateAdjustment? maniaRateAdjustment;

    public override LocalisableString Description => O2LazerStrings.ModHalfTimeDescription;

    void IApplicableToHitObject.ApplyToHitObject(HitObject hitObject) =>
        (maniaRateAdjustment ??= new O2JamManiaRateAdjustment(SpeedChange)).Apply(hitObject);
}
