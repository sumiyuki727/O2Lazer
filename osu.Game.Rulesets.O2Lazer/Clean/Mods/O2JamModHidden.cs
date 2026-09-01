using osu.Framework.Localisation;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModHidden : ManiaModHidden
{
    public override LocalisableString Description => O2LazerStrings.ModHiddenDescription;

    public override void ApplyToDrawableRuleset(DrawableRuleset<ManiaHitObject> drawableRuleset) =>
        O2JamModPlayfieldCover.Apply(drawableRuleset, Coverage, ExpandDirection, CreateCover);
}
