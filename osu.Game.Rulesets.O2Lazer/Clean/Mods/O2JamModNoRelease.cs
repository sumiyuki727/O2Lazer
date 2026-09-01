using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed class O2JamModNoRelease : ManiaModNoRelease, IApplicableAfterBeatmapConversion, IApplicableToDrawableRuleset<ManiaHitObject>
{
    public override LocalisableString Description => O2LazerStrings.ModNoReleaseDescription;

    void IApplicableAfterBeatmapConversion.ApplyToBeatmap(IBeatmap beatmap)
    {
        foreach (var hold in beatmap.HitObjects.OfType<O2JamHoldNote>())
        {
            hold.ReleaseTimingDisabled = true;

            foreach (var tail in hold.NestedHitObjects.OfType<O2JamHoldTail>())
                tail.ReleaseTimingDisabled = true;
        }
    }

    // O2Jam's exact-type tail drawable owns the automatic release behaviour, so no replacement
    // pool is required and the native O2Jam endpoint remains intact.
    void IApplicableToDrawableRuleset<ManiaHitObject>.ApplyToDrawableRuleset(DrawableRuleset<ManiaHitObject> drawableRuleset)
    {
    }
}
