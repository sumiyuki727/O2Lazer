using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Core;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.Objects;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Screens.Play;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects;

public partial class O2JamDrawableHoldNote : DrawableHoldNote, IKeyBindingHandler<ManiaAction>
{
    public new O2JamHoldNote HitObject => (O2JamHoldNote)base.HitObject;

    private O2JamDrawableHoldHead o2Head => (O2JamDrawableHoldHead)Head;
    private O2JamDrawableHoldTail o2Tail => (O2JamDrawableHoldTail)Tail;
    private O2JamDrawableHoldBody o2Body => (O2JamDrawableHoldBody)Body;

    public O2JamDrawableHoldNote()
    {
    }

    public O2JamDrawableHoldNote(O2JamHoldNote hitObject)
        : base(hitObject)
    {
    }

    protected override DrawableHitObject CreateNestedHitObject(HitObject hitObject) => hitObject switch
    {
        O2JamHoldHead head => new O2JamDrawableHoldHead(head),
        O2JamHoldTail tail => new O2JamDrawableHoldTail(tail),
        O2JamHoldBody body => new O2JamDrawableHoldBody(body),
        _ => base.CreateNestedHitObject(hitObject),
    };

    protected override void OnApply()
    {
        base.OnApply();
        Colour = Colour4.White;
    }

    protected override void Update()
    {
        base.Update();

        // The parent tint works for every mania skin implementation. Legacy skin pieces undo
        // their own native tint separately so this remains the single visual policy switch.
        Colour = MissingStartTime.Value != null && !O2JamRuntimeOptions.UseO2JamLongNoteMissVisual
            ? Colour4.DarkGray
            : Colour4.White;

        if (O2JamRuntimeOptions.UseO2JamLongNoteMissVisual)
            updateO2JamClipping();
    }

    private void updateO2JamClipping()
    {
        if (Head.Parent?.Parent is not Container sizingContainer)
            return;

        // A rejected BAD head is still an IsHit to mania, although O2Jam never began the hold.
        // Undo native clipping for that case so the unheld LN falls past the line intact.
        if (resolvedHeadOutcome() == O2JamHoldHeadOutcome.EndWithMiss)
        {
            sizingContainer.Height = 1;
            return;
        }

        // IsHit also includes unrescued BAD (framework Ok). Only the final COOL/GOOD result
        // continues clipping; BAD/MISS keep mania's frozen bounds and scroll past the line.
        if (Tail.Result.Type is not (HitResult.Perfect or HitResult.Good))
            return;

        if (Time.Current >= HitObject.StartTime && DrawHeight > 0)
        {
            var yOffset = Direction.Value == ScrollingDirection.Up ? -Y : Y;
            sizingContainer.Height = 1 - yOffset / DrawHeight;
        }

        // The pinned mania head must not remain on the line after the entire body has passed it.
        // The tail can still finish scrolling through the mask using its normal skin dimensions.
        Head.Alpha = Time.Current < HitObject.EndTime ? 1 : 0;
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state != ArmedState.Miss && !(state == ArmedState.Hit && O2JamRuntimeOptions.UseO2JamLongNoteMissVisual))
        {
            base.UpdateHitStateTransforms(state);
            return;
        }

        // Logical resolution does not end the O2Jam visual. Both early tail hits and rejected
        // heads must reach the charted tail instead of inheriting mania's whole-note fade.
        LifetimeEnd = HitObject.EndTime + 150;
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (Head.Judged && !Tail.Judged && resolvedHeadOutcome() == O2JamHoldHeadOutcome.EndWithMiss)
            endAfterRejectedHead();

        if (Tail.AllJudged)
            finish(Tail.IsHit);
    }

    bool IKeyBindingHandler<ManiaAction>.OnPressed(KeyBindingPressEvent<ManiaAction> e)
    {
        if (AllJudged || e.Action != Action.Value)
            return false;
        if ((Clock as IGameplayClock)?.IsRewinding == true)
            return false;
        if (CheckHittable?.Invoke(this, Time.Current) == false)
            return false;

        if (!Head.Judged)
            o2Head.UpdateResult();

        if (Head.Result is not Scoring.O2JamJudgementResult headResult || !headResult.ResolutionApplied)
            return false;

        switch (O2JamHoldRules.ResolveHead(headResult.Resolution.ResolvedAccuracy))
        {
            case O2JamHoldHeadOutcome.BeginHold:
                Result.ReportHoldState(Time.Current, true);
                return true;

            case O2JamHoldHeadOutcome.EndWithMiss:
                endAfterRejectedHead();
                return true;

            default:
                return false;
        }
    }

    void IKeyBindingHandler<ManiaAction>.OnReleased(KeyBindingReleaseEvent<ManiaAction> e)
    {
        if (AllJudged || e.Action != Action.Value)
            return;
        if ((Clock as IGameplayClock)?.IsRewinding == true)
            return;
        if (!Result.IsHolding(Time.Current))
            return;

        o2Tail.UpdateResult();
        o2Body.Resolve(Tail.IsHit);
        Result.ReportHoldState(Time.Current, false);

        // Parent resolution must remain in CheckForResult(), after the drawable tree has completed
        // this frame. Resolving here lets pooling remove Head/Tail before mania's Update() reads them.
    }

    private void finish(bool tailHit)
    {
        if (!Judged)
        {
            if (tailHit)
                ApplyMaxResult();
            else
                MissForcefully();
        }

        o2Body.Resolve(tailHit);
        Result.ReportHoldState(Time.Current, false);
    }

    private void endAfterRejectedHead()
    {
        o2Tail.ResolveForcedMiss();
        o2Body.Resolve(false);
    }

    private O2JamHoldHeadOutcome resolvedHeadOutcome() =>
        Head.Result is Scoring.O2JamJudgementResult { ResolutionApplied: true } headResult
            ? O2JamHoldRules.ResolveHead(headResult.Resolution.ResolvedAccuracy)
            : O2JamHoldHeadOutcome.Ignore;
}
