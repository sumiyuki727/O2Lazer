using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.Beatmaps.Objects;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Objects;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Drawables;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.O2Lazer.UI.Components;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI.Scrolling;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects;

public abstract partial class DrawableO2LazerHitObject : DrawableHitObject<O2LazerHitObject>
{
    protected abstract O2LazerSkinComponents SkinComponent { get; }

    protected virtual bool SkipFurtherUpdates => false;

    protected virtual bool RequiresResultBeforeKindPostState => false;

    internal virtual bool RequiresColumnFrameUpdate => true;

    protected O2LazerLayoutVariant LayoutVariant => ParentColumn?.LayoutVariant ?? HitObject?.Beatmap.LayoutVariant ?? O2LazerLayoutVariant.O2Jam7K;

    protected float HitTargetPosition => ParentColumn?.HitTargetPosition ?? O2LazerStage.HIT_TARGET_POSITION;

    protected double ScrollSpeedMultiplier => ParentColumn?.ScrollSpeedMultiplier ?? 1;

    protected Container NoteContainer = null!;

    protected ScrollingDirection CurrentDirection { get; private set; } = ScrollingDirection.Down;

    private IBindable<ScrollingDirection> direction = null!;

    [Resolved]
    private IScrollingInfo scrollingInfo { get; set; } = null!;

    [Resolved(CanBeNull = true)]
    protected O2LazerColumn? ParentColumn { get; private set; }

    protected DrawableO2LazerHitObject()
        : base(null!)
    {
        Anchor = Anchor.BottomLeft;
        Origin = Anchor.BottomLeft;
        RelativeSizeAxes = Axes.X;
    }

    [BackgroundDependencyLoader]
    private void load(IScrollingInfo scrollingInfo)
    {
        this.scrollingInfo = scrollingInfo;
        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(change => ApplyScrollDirection(change.NewValue), true);
    }

    protected override void Update()
    {
        base.Update();

        if (scrollingInfo != null && scrollingInfo.Direction.Value != CurrentDirection)
            ApplyScrollDirection(scrollingInfo.Direction.Value);
    }

    protected virtual void ApplyScrollDirection(ScrollingDirection newDirection)
    {
        CurrentDirection = newDirection;
        Anchor = Origin = newDirection == ScrollingDirection.Up ? Anchor.TopLeft : Anchor.BottomLeft;
    }

    public virtual bool TryHit(HitResult result)
    {
        if (Judged || result == HitResult.None)
            return false;

        ApplyResult(result);
        return true;
    }

    public override void PlaySamples()
    {
    }

    internal void UpdateColumnFrame()
    {
        if (HitObject == null) return;

        if (!Judged && !SkipFurtherUpdates)
        {
            if (!UpdateKindState() && RequiresResultBeforeKindPostState)
                UpdateResult(false);
        }

        UpdateKindPostResultState();
    }

    protected virtual void ResetKindState()
    {
    }

    protected virtual bool UpdateKindState() => false;

    protected virtual void UpdateKindPostResultState()
    {
    }

    protected virtual void AddKindDrawablesBeforeNote()
    {
    }

    protected virtual void AddNoteContainer(Container noteContainer) => AddInternal(noteContainer);

    protected override void OnApply()
    {
        base.OnApply();
        AutoSizeAxes = LayoutVariant == O2LazerLayoutVariant.O2Jam7K && this is not ILongNoteHolder ? Axes.Y : Axes.None;
        Alpha = 1;
        ApplyScrollDirection(CurrentDirection);
        ResetKindState();
    }

    protected override void UpdateHitStateTransforms(ArmedState state)
    {
        base.UpdateHitStateTransforms(state);
        switch (state)
        {
            case ArmedState.Hit:
                this.FadeOut();
                LifetimeEnd = Time.Current;
                break;

            case ArmedState.Miss:
                this.FadeColour(Color4.Red, 80).FadeOut(220).Expire();
                LifetimeEnd = Time.Current + 300;
                break;
        }
    }

    protected override JudgementResult CreateResult(Judgement judgement) => new(HitObject, judgement);

    protected override void LoadSamples()
    {
    }
}

public abstract partial class DrawableO2LazerHitObject<TCol> : DrawableO2LazerHitObject
    where TCol : struct, IColumnProvider
{

    protected int Column { get; } = default(TCol).Value;

    private O2LazerCachedSkinnableDrawable? cachedSkinnableDrawable;

    [BackgroundDependencyLoader]
    private void load()
    {
        AddKindDrawablesBeforeNote();

        NoteContainer = new Container
        {
            Anchor = noteAnchor(),
            Origin = noteAnchor(),
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = LayoutVariant == O2LazerLayoutVariant.O2Jam7K ? Axes.Y : Axes.None,
        };

        cachedSkinnableDrawable = new O2LazerCachedSkinnableDrawable(
            new O2LazerSkinComponentLookup(SkinComponent, LayoutVariant, Column),
            lookup => lookup.LayoutVariant == O2LazerLayoutVariant.O2Jam7K
                      && lookup.Component is O2LazerSkinComponents.Note or O2LazerSkinComponents.HoldNoteHead or O2LazerSkinComponents.HoldNoteTail
                ? new O2LazerManiaDefaultNotePiece()
                : Empty())
        {
            AutoSizeHeight = LayoutVariant == O2LazerLayoutVariant.O2Jam7K,
            Anchor = noteAnchor(),
            Origin = noteAnchor(),
            ComponentAnchor = CurrentDirection == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre,
        };

        NoteContainer.Add(cachedSkinnableDrawable);
        AddNoteContainer(NoteContainer);
    }

    protected override void ApplyScrollDirection(ScrollingDirection newDirection)
    {
        base.ApplyScrollDirection(newDirection);

        if (NoteContainer == null)
            return;

        NoteContainer.Anchor = NoteContainer.Origin = noteAnchor();
        if (cachedSkinnableDrawable != null)
        {
            cachedSkinnableDrawable.Anchor = cachedSkinnableDrawable.Origin = noteAnchor();
            cachedSkinnableDrawable.SetComponentAnchor(newDirection == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre);
        }
    }

    private Anchor noteAnchor() => CurrentDirection == ScrollingDirection.Up ? Anchor.TopLeft : Anchor.BottomLeft;

    protected float NoteVisualHeight => cachedSkinnableDrawable?.Drawable.DrawHeight ?? 0;
}
