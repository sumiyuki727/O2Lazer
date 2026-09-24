using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Mods;

// O2Jam judges in chart-position space; mania's concrete adapter assumes ManiaHitWindows.
public sealed class O2JamModDoubleTime : ModDoubleTime
{
    public override LocalisableString Description => O2LazerStrings.ModDoubleTimeDescription;
}
