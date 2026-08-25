using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Scoring.Judgements;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Drawables;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.O2Lazer.UI.Gameplay;
using osu.Game.Rulesets.O2Lazer.UI.Objects.LnHelper;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Screens.Play;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects;

public sealed partial class DrawableO2LazerLongNote<TCol> : DrawableO2LazerHitObject<TCol>, ILongNoteHolder, IO2LazerLongNoteHooks
    where TCol : struct, IColumnProvider
{
    public bool IsHoldingLongNote => controller.LongNoteStarted && !controller.TailJudged;

    public bool IsAutomaticallyHeld { get; set; }

    protected override O2LazerSkinComponents SkinComponent => O2LazerSkinComponents.HoldNoteHead;

    protected override bool RequiresResultBeforeKindPostState => true;

    private O2LazerLongNote ln => (O2LazerLongNote)HitObject;

    // Re-trigger the LN hit light this often while holding so the explosion pulses throughout the
    // hold instead of only firing at the head and tail endpoints.
    private const double hold_explosion_interval = O2LazerLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION;

    private readonly O2LazerLongNoteVisualState visualState = new();
    private readonly O2LazerLongNoteJudgementController controller = new();

    private double lastHoldExplosionTime;
    private bool bodyGeometryValid;
    private float lastHeadOffset;
    private float lastBodyTailOffset;
    private float lastTailOffset;
    private float lastBodyWidth;
    private float lastVisualNoteHeight;
    private float lastVisualTailHeight;
    private bool lastHoldingBody;
    private bool lastReleasedFast;
    private O2LazerManiaLongNoteBody longNoteBody = null!;
    private O2LazerCachedSkinnableDrawable longNoteTail = null!;
    private O2JamManiaHoldNoteVisual o2jamVisual = null!;

    private bool isO2Jam => LayoutVariant == O2LazerLayoutVariant.O2Jam7K;

    [Resolved]
    private IScrollingInfo scrollingInfo { get; set; } = null!;

    [Resolved(CanBeNull = true)]
    private IO2LazerLnScoring? scoring { get; set; }

    private double gameplayRate => (Clock as IGameplayClock)?.GetTrueGameplayRate() ?? Clock.Rate;

    public override bool TryHit(HitResult result)
    {
        if (Judged || HitObject == null)
            return false;

        return controller.TryHit(Time.Current, result, gameplayRate);
    }

    public bool TryRelease(double releaseOffset, O2LazerJudgementWindowTable tailTable)
        => HitObject != null && controller.TryRelease(Time.Current, releaseOffset, tailTable, gameplayRate);

    /// <summary>
    /// Called by O2LazerColumnHitObjectContainer every frame with pre-computed
    /// head and end Y positions. Updates the body/tail visual geometry.
    /// </summary>
    public void UpdateBodyGeometry(float headY, float endY)
    {
        var hitObject = HitObject;
        if (hitObject == null)
            return;

        var holdingBody = isHoldingBody();
        var visualOffset = ParentColumn?.VisualOffset ?? 0;

        visualState.UpdateHeadYAtStartTime(headY, Time.Current, HitObject.StartTime, visualOffset);

        if (isO2Jam)
        {
            var rawHeadY = headY;

            if (holdingBody)
            {
                // Like mania, a fast hit must not stretch the LN by fixing its head
                // before the chart time reaches it.
                var canPinHead = Time.Current >= HitObject.StartTime;
                headY = visualState.ResolveHeldHeadY(headY, endY, canPinHead, bodyDirectionBeforeTailPasses);
            }

            var o2jamMyY = Y;
            var o2jamHeadOffset = headY - o2jamMyY;
            var isUp = scrollingInfo.Direction.Value == ScrollingDirection.Up;
            var fullHeight = Math.Max(0, isUp ? endY - rawHeadY : rawHeadY - endY);
            var consumedHeight = Math.Max(0, isUp ? o2jamHeadOffset : -o2jamHeadOffset);
            var headHeight = NoteVisualHeight;
            var o2jamReleasedFast =
                controller.LongNoteStarted && HitObject != null && Time.Current < ln.EndTime && !holdingBody;

            o2jamVisual.UpdateGeometry(
                fullHeight,
                consumedHeight,
                headHeight,
                pinActive: holdingBody && Time.Current >= hitObject.StartTime,
                holding: holdingBody);

            if (o2jamReleasedFast)
                o2jamVisual.MarkDropped();

            return;
        }

        if (holdingBody)
        {
            // Like mania, a fast hit must not stretch the LN by fixing its head
            // before the chart time reaches it.
            var canPinHead = Time.Current >= HitObject.StartTime;
            headY = visualState.ResolveHeldHeadY(headY, endY, canPinHead, bodyDirectionBeforeTailPasses);
        }

        var myY = Y;
        var headOffset = headY - myY;
        var tailOffset = endY - myY;
        var bodyTailOffset = holdingBody
            ? visualState.VisibleBodyTailOffset(headOffset, tailOffset)
            : tailOffset;
        var visualNoteHeight = NoteVisualHeight;
        var visualTailHeight = longNoteTail.Drawable.DrawHeight;

        var releasedFast =
            controller.LongNoteStarted && HitObject != null && Time.Current < ln.EndTime && !holdingBody;
        var geometryChanged = !bodyGeometryValid
                              || Math.Abs(lastHeadOffset - headOffset) > 0.5f
                              || Math.Abs(lastBodyTailOffset - bodyTailOffset) > 0.5f
                              || Math.Abs(lastTailOffset - tailOffset) > 0.5f
                              || Math.Abs(lastBodyWidth - longNoteBody.DrawWidth) >= 1
                              || Math.Abs(lastVisualNoteHeight - visualNoteHeight) > 0.5f
                              || Math.Abs(lastVisualTailHeight - visualTailHeight) > 0.5f
                              || lastHoldingBody != holdingBody
                              || lastReleasedFast != releasedFast;

        if (!geometryChanged)
        {
            // Static LNs move with their parent. Their relative body geometry does not need to be
            // invalidated every frame, but animated skins must still be allowed to advance.
            longNoteBody.UpdateAnimation(holdingBody);
            return;
        }

        bodyGeometryValid = true;
        lastHeadOffset = headOffset;
        lastBodyTailOffset = bodyTailOffset;
        lastTailOffset = tailOffset;
        lastBodyWidth = longNoteBody.DrawWidth;
        lastVisualNoteHeight = visualNoteHeight;
        lastVisualTailHeight = visualTailHeight;
        lastHoldingBody = holdingBody;
        lastReleasedFast = releasedFast;

        if (Math.Abs(NoteContainer.Y - headOffset) > 0.5f)
            NoteContainer.Y = headOffset;

        // osu!mania starts and ends the body half-way under each independently-sized endpoint.
        var bodyHeadCentre = O2LazerLongNoteGeometry.BodyCentreOffset(headOffset, visualNoteHeight);
        var bodyTailCentre = O2LazerLongNoteGeometry.BodyCentreOffset(bodyTailOffset, visualTailHeight);
        var tailAtTop = bodyTailCentre < bodyHeadCentre;
        var bodyTop = Math.Min(bodyHeadCentre, bodyTailCentre);
        var bodyBottom = Math.Max(bodyHeadCentre, bodyTailCentre);

        var bodyHeight = Math.Max(0, bodyBottom - bodyTop);

        if (Math.Abs(longNoteBody.Y - bodyTop) > 0.5f)
            longNoteBody.Y = bodyTop;

        if (Math.Abs(longNoteBody.Height - bodyHeight) > 0.5f)
            longNoteBody.Height = Math.Max(0, bodyHeight);

        longNoteBody.UpdateBody(bodyHeight, tailAtTop, holdingBody);
        longNoteBody.Alpha = bodyHeight > 0 ? 1 : 0;
        longNoteBody.Colour = releasedFast ? Color4.DarkGray : Color4.White;

        if (longNoteTail.Drawable is IO2LazerManiaHoldNoteVisualPiece tailPiece)
            tailPiece.SetHolding(holdingBody);

        if (Math.Abs(longNoteTail.Y - tailOffset) > 0.5f)
            longNoteTail.Y = tailOffset;

        longNoteTail.Alpha = 1;
        longNoteTail.Colour = releasedFast ? Color4.DarkGray : Color4.White;
    }

    protected override void ResetKindState()
    {
        IsAutomaticallyHeld = false;
        controller.Reset();
        visualState.Reset();
        bodyGeometryValid = false;

        if (isO2Jam)
        {
            o2jamVisual.ResetVisual();
            return;
        }

        longNoteBody.ResetBody();
        longNoteBody.Alpha = 0;
        longNoteTail.Alpha = 0;
        longNoteTail.Colour = Color4.White;
        if (longNoteTail.Drawable is IO2LazerManiaHoldNoteVisualPiece tailPiece)
            tailPiece.Recycle();
    }

    protected override void OnApply()
    {
        base.OnApply();

        if (HitObject != null)
        {
            controller.Bind((O2LazerLongNote)HitObject, this);

            if (!isO2Jam)
                longNoteBody.SetSkinLookup(LayoutVariant, Column);
        }
    }

    protected override void AddKindDrawablesBeforeNote()
    {
        if (isO2Jam)
        {
            AddInternal(o2jamVisual = new O2JamManiaHoldNoteVisual(LayoutVariant, Column));
            return;
        }

        AddRangeInternal([
            longNoteBody = new O2LazerManiaLongNoteBody
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                BodyColour = Color4.Cyan,
                Alpha = 0,
            },
            longNoteTail = new O2LazerCachedSkinnableDrawable(
                new O2LazerSkinComponentLookup(O2LazerSkinComponents.HoldNoteTail,
                    LayoutVariant, Column),
                lookup => lookup.LayoutVariant == O2LazerLayoutVariant.O2Jam7K
                    ? new O2LazerManiaDefaultNotePiece()
                    : Empty())
            {
                Anchor = Anchor.TopLeft,
                Origin = Anchor.TopLeft,
                RelativeSizeAxes = Axes.X,
                Alpha = 0,
                AutoSizeHeight = LayoutVariant == O2LazerLayoutVariant.O2Jam7K,
                ComponentAnchor = scrollingInfo.Direction.Value == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre,
            },
        ]);
    }

    protected override void AddNoteContainer(Container noteContainer)
    {
        if (isO2Jam)
        {
            o2jamVisual.AttachHead(noteContainer);
            return;
        }

        base.AddNoteContainer(noteContainer);
    }

    protected override void ApplyScrollDirection(ScrollingDirection newDirection)
    {
        base.ApplyScrollDirection(newDirection);

        // A live flip changes the hit line's coordinate sign (negative for down, positive for up).
        // Rebase any already-pinned head so it keeps sitting on the new line instead of inheriting
        // the previous direction's stored coordinate.
        visualState.PrepareHeadPin();
        visualState.RebaseHeadPin(newDirection == ScrollingDirection.Up ? HitTargetPosition : -HitTargetPosition);

        // The non-O2Jam tail is parented directly to the drawable, so unlike the O2Jam visual it
        // does not receive direction changes through a wrapping hold-note hierarchy.
        longNoteTail?.SetComponentAnchor(newDirection == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre);
    }

    protected override void CheckForResult(bool userTriggered, double timeOffset)
    {
        if (userTriggered || HitObject == null)
            return;

        controller.CheckPassiveResult(Time.Current, gameplayRate);
    }

    // Keep CN/HCN visuals alive after head judgement
    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        if (state == ArmedState.Hit && HitObject != null && Time.Current < ln.EndTime)
        {
            Alpha = 1;
            LifetimeEnd = controller.IsChargeMode ? controller.ChargeTailLifetimeEnd() : ln.EndTime;
            return;
        }

        if (controller.IsChargeMode && state == ArmedState.Hit && !controller.TailJudged)
        {
            Alpha = 1;

            if (HitObject != null)
                LifetimeEnd = controller.ChargeTailLifetimeEnd();

            return;
        }

        // A missed O2Jam LN keeps scrolling past the judgement line greyed out, matching mania's
        // missing-start-time dim instead of the base class's red fade-out.
        if (state == ArmedState.Miss && isO2Jam)
        {
            Alpha = 1;
            o2jamVisual.MarkDropped();
            return;
        }

        base.UpdateHitStateTransforms(state);
    }

    protected override void UpdateKindPostResultState()
    {
        if (HitObject == null)
            return;

        // Hold-explosion pulse runs first, matching the original per-frame order (pulse, then
        // charge-tail passive miss, retire, HCN tick). It reads pre-mutation controller state.
        if (controller.LongNoteStarted && !controller.TailJudged
                                       && Time.Current >= HitObject.StartTime && Time.Current <= ln.EndTime
                                       && Time.Current - lastHoldExplosionTime >= hold_explosion_interval)
        {
            ParentColumn?.TriggerHitExplosion(true);
            lastHoldExplosionTime = Time.Current;
        }

        controller.UpdatePostResult(Time.Current, Time.Elapsed, isKeyHeld(), gameplayRate);
    }

    private bool isHoldingBody()
        => HitObject != null && controller.ShouldShowHeldVisual(isKeyHeld());

    private bool isKeyHeld() => ParentColumn?.IsPressed == true || IsAutomaticallyHeld;

    private int bodyDirectionBeforeTailPasses(float realHeadY, float realTailY) => HitObject == null
        ? Math.Sign(realTailY - realHeadY)
        : O2LazerLongNoteGeometry.BodyDirectionBeforeTailPasses(
            ((O2LazerLongNote)HitObject).ScrollPositionAtEndTime - HitObject.ScrollPositionAtStartTime,
            ln.Duration,
            ScrollSpeedMultiplier,
            realHeadY,
            realTailY);

    // --- IO2LazerLongNoteHooks: side-effects driven by the judgement controller. ---

    void IO2LazerLongNoteHooks.OnUserHeadJudged()
    {
        visualState.PrepareHeadPin();
        lastHoldExplosionTime = Time.Current - hold_explosion_interval;
    }

    void IO2LazerLongNoteHooks.OnHellChargeHeadPoor(double eventTime, double lifetimeEnd)
    {
        visualState.PrepareHeadPin();
        Alpha = 1;
        LifetimeEnd = lifetimeEnd;
    }

    void IO2LazerLongNoteHooks.ApplyJudgementResult(HitResult result, System.Collections.Generic.IReadOnlyList<O2LazerLongNoteEndpointResult> endpoints)
    {
        ((O2LazerLongNoteJudgementResult)Result).SetEndpointResults(endpoints);
        ApplyResult(result);
    }

    void IO2LazerLongNoteHooks.ApplySyntheticEndpoint(HitResult result, O2LazerLongNoteEndpointResult endpoint)
        => scoring?.ApplySyntheticLongNoteEndpoint(this, endpoint);

    bool IO2LazerLongNoteHooks.TryConsumePillForBad() => ScoreProcessor?.TryConsumePillForBad() == true;

    void IO2LazerLongNoteHooks.ClearVisualIfTailWasNotPoor(HitResult tailResult)
    {
        // Missed tails (and POOR) leave the greyed hold scrolling past like mania; only a
        // successful tail judgement retires the drawable immediately.
        if (!tailResult.IsHit())
        {
            // The drawable is already judged by the head, so the miss state transform is skipped;
            // grey the visual here instead of relying on it.
            if (isO2Jam)
                o2jamVisual.MarkDropped();
            else
                longNoteBody.SetDropped(true);
            return;
        }

        visualState.Reset();

        if (isO2Jam)
        {
            o2jamVisual.ResetVisual();
            this.FadeOut();
            LifetimeEnd = Time.Current;
            return;
        }

        longNoteBody.Alpha = 0;
        longNoteTail.Alpha = 0;
        this.FadeOut();
        LifetimeEnd = Time.Current;
    }

    void IO2LazerLongNoteHooks.ApplyHellChargeTick(bool holding, double scale)
        => scoring?.ApplyHellChargeTick(holding, scale);

    void IO2LazerLongNoteHooks.Retire()
    {
        this.FadeOut();
        LifetimeEnd = Time.Current;
    }

    protected override JudgementResult CreateResult(Judgement judgement)
        => new O2LazerLongNoteJudgementResult(HitObject, judgement);
}
