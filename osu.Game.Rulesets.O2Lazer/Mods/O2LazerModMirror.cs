using osu.Framework.Graphics.Sprites;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Graphics;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.Mods;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public class O2LazerModMirror : Mod, IApplicableAfterBeatmapConversion
{
    public override string Name => "Mirror";

    public override string Acronym => "MR";

    public override IconUsage? Icon => OsuIcon.ModMirror;

    public override LocalisableString Description => "Notes are flipped horizontally.";

    public override ModType Type => ModType.Conversion;

    public void ApplyToBeatmap(IBeatmap beatmap)
    {
        if (beatmap is O2LazerBeatmap o2lazerBeatmap)
        {
            foreach (var hitObject in o2lazerBeatmap.HitObjects)
                hitObject.Column = 6 - hitObject.Column;
        }
    }
}
