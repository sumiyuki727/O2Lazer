using osu.Framework.Localisation;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Mods;

// The common generic base retains the beat overlay without applying mania-only hit windows.
public sealed class O2JamModNightcore : ModNightcore<ManiaHitObject>
{
    public override LocalisableString Description => O2LazerStrings.ModNightcoreDescription;
}
