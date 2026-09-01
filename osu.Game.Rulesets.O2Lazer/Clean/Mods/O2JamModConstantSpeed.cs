using osu.Framework.Localisation;
using osu.Game.Configuration;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.UI;
using osu.Game.Rulesets.UI.Scrolling;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModConstantSpeed : ManiaModConstantSpeed, IApplicableToDrawableRuleset<ManiaHitObject>
{
    public override LocalisableString Description => O2LazerStrings.ModConstantSpeedDescription;

    // Mania's adapter casts to its own drawable ruleset. The shared scrolling base provides
    // the same algorithm without coupling O2Jam's gameplay lifecycle to DrawableManiaRuleset.
    void IApplicableToDrawableRuleset<ManiaHitObject>.ApplyToDrawableRuleset(DrawableRuleset<ManiaHitObject> drawableRuleset) =>
        ((DrawableScrollingRuleset<ManiaHitObject>)drawableRuleset).VisualisationMethod = ScrollVisualisationMethod.Constant;
}
