using System;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModHardRock : ManiaModHardRock, IO2JamManiaScoreDependentMod
{
    public override LocalisableString Description => O2LazerStrings.ModHardRockDescription;

    // Difficulty Adjust is part of MS, so the usual native DA exclusion cannot apply here.
    public override Type[] IncompatibleMods => [typeof(O2JamModEasy)];
}
