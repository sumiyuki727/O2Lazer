using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.UI.Icons;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModManiaScore : Mod
{
    public override string Name => O2LazerStrings.ModManiaScoreName.ToString();

    public override string Acronym => O2LazerStrings.ModManiaScoreAcronym.ToString();

    public override LocalisableString Description => O2LazerStrings.ModManiaScoreDescription;

    public override IconUsage? Icon => O2JamModIcons.ManiaScore;

    public override ModType Type => ModType.Conversion;

    public override bool Ranked => true;

    // Keep the placeholder registered for stored scores, but let native selection hide it
    // until mania scoring is implemented.
    public override bool HasImplementation => false;
}
