using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects;

public sealed partial class DrawableO2LazerNote<TCol> : DrawableO2LazerHitObject<TCol>
    where TCol : struct, IColumnProvider
{
    protected override O2LazerSkinComponents SkinComponent => O2LazerSkinComponents.Note;

    // The framework already performs passive result checks; normal notes have no O2LAZER-specific frame state.
    internal override bool RequiresColumnFrameUpdate => false;

    private double passivePoorOffset;

    protected override void OnApply()
    {
        base.OnApply();

        var table = O2LazerJudgementProfileProvider.GetTable(HitObject.Beatmap.LayoutVariant, HitObject.Column, HitObject.EffectiveJudgementRate, tail: false);
        passivePoorOffset = table.SlowWindowFor(HitResult.Ok);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject == null)
            return;

        if (timeOffset > passivePoorOffset)
            ApplyResult(LayoutVariant == O2LazerLayoutVariant.O2Jam7K ? HitResult.Miss : HitResult.Meh);
    }

    protected override void UpdateInitialTransforms() => Alpha = 1;

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state != ArmedState.Hit)
        {
            base.UpdateHitStateTransforms(state);
            return;
        }

        Alpha = 0;
        LifetimeEnd = Time.Current;
    }
}
