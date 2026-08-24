using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Drawables;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.UI.Objects.LnHelper;

/// <summary>
/// Hosts the same skin-selected single body piece that osu!mania stretches across a hold note.
/// </summary>
public sealed partial class O2LazerManiaLongNoteBody : CompositeDrawable
{
    public Color4 BodyColour
    {
        get => fallback.Colour;
        set => fallback.Colour = value;
    }

    internal int AnimationFrameCount => (bodyPiece?.Drawable as IO2LazerManiaHoldNoteVisualPiece)?.AnimationFrameCount ?? 0;
    internal int CurrentAnimationFrame => (bodyPiece?.Drawable as IO2LazerManiaHoldNoteVisualPiece)?.CurrentAnimationFrame ?? 0;

    private readonly Box fallback;
    private O2LazerCachedSkinnableDrawable? bodyPiece;
    private int? column;
    private O2LazerLayoutVariant layoutVariant;

    public O2LazerManiaLongNoteBody()
    {
        Masking = true;
        RelativeSizeAxes = Axes.X;

        InternalChild = fallback = new Box
        {
            RelativeSizeAxes = Axes.Both,
        };
    }

    public void SetSkinLookup(O2LazerLayoutVariant newLayoutVariant, int newColumn)
    {
        if (column == newColumn && layoutVariant == newLayoutVariant)
            return;

        column = newColumn;
        layoutVariant = newLayoutVariant;

        if (bodyPiece != null)
            RemoveInternal(bodyPiece, true);

        bodyPiece = new O2LazerCachedSkinnableDrawable(
            new O2LazerSkinComponentLookup(O2LazerSkinComponents.HoldNoteBody, layoutVariant, newColumn),
            lookup => lookup.LayoutVariant == O2LazerLayoutVariant.O2Jam7K
                ? new O2LazerManiaDefaultHoldBodyPiece()
                : new O2LazerDeferredResolvedHoldNoteBodyPiece(lookup))
        {
            RelativeSizeAxes = Axes.Both,
            ComponentAnchor = null,
        };

        AddInternal(bodyPiece);
    }

    protected override void Update()
    {
        base.Update();

        if (bodyPiece?.Drawable != null)
            fallback.Alpha = 0;
    }

    public void ResetBody()
    {
        Colour = Color4.White;
        if (bodyPiece?.Drawable is IO2LazerManiaHoldNoteVisualPiece piece)
        {
            piece.SetHolding(false);
            piece.Recycle();
        }
    }

    public void UpdateAnimation(bool isHolding)
    {
        if (bodyPiece?.Drawable is IO2LazerManiaHoldNoteVisualPiece piece)
            piece.SetHolding(isHolding);
    }

    public void UpdateBody(float bodyHeight, bool tailAtTop, bool isHolding)
    {
        UpdateAnimation(isHolding);

        if (bodyPiece?.Drawable is IO2LazerManiaHoldNoteBodyPiece body)
            body.SetTailAtTop(tailAtTop, bodyHeight);
    }

}
