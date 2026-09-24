using System.Linq;
using osu.Framework.Localisation;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Mania.Mods;
using osu.Game.Rulesets.Mania.Objects;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mods;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.UI;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI;

namespace osu.Game.Rulesets.O2Lazer.Mods;

public sealed partial class O2JamModNoRelease : ManiaModNoRelease, IApplicableAfterBeatmapConversion, IApplicableToDrawableRuleset<ManiaHitObject>
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

    void IApplicableToDrawableRuleset<ManiaHitObject>.ApplyToDrawableRuleset(DrawableRuleset<ManiaHitObject> drawableRuleset)
    {
        if (drawableRuleset is not O2JamDrawableRuleset o2JamRuleset)
            return;

        // O2Jam tails retain their exact-type pool. MS creates plain mania tails, for which this
        // mod installs the same automatic-release drawable used by native mania No Release.
        foreach (var stage in o2JamRuleset.Playfield.Stages)
        {
            foreach (var column in stage.Columns)
                column.RegisterPool<TailNote, ManiaScoreNoReleaseDrawableTail>(10, 50);
        }
    }

    private partial class ManiaScoreNoReleaseDrawableTail : DrawableHoldNoteTail
    {
        protected override void CheckForResult(bool userTriggered, double timeOffset)
        {
            if (HoldNote.IsHolding.Value && timeOffset >= 0)
                ApplyResult(GetCappedResult(HitResult.Perfect));
            else
                base.CheckForResult(userTriggered, timeOffset);
        }
    }
}
