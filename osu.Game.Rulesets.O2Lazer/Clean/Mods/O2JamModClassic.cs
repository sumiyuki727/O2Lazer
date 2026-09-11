using osu.Framework.Localisation;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModClassic : ManiaModClassic, IO2JamManiaScoreDependentMod
{
    public override LocalisableString Description => O2LazerStrings.ModClassicDescription;
}
