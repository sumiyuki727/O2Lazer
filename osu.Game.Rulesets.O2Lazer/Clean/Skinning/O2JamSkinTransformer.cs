using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.Mania;
using osu.Game.Rulesets.Mania.Objects.Drawables;
using osu.Game.Rulesets.Mania.Skinning.Legacy;
using osu.Game.Rulesets.Objects.Drawables;
using osu.Game.Skinning;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning;

/// <summary>
/// Adds O2Jam-specific skin behaviour on top of the native mania skin pipeline.
/// </summary>
internal sealed partial class O2JamSkinTransformer : SkinTransformer
{
    private O2JamSkinTransformer(ISkin skin)
        : base(skin)
    {
    }

    internal static ISkin WrapIfNeeded(ISkin transformedSkin) => transformedSkin is ManiaLegacySkinTransformer
        ? new O2JamSkinTransformer(transformedSkin)
        : transformedSkin;

    public override Drawable? GetDrawableComponent(ISkinComponentLookup lookup)
    {
        var drawable = base.GetDrawableComponent(lookup);

        if (drawable == null || lookup is not ManiaSkinComponentLookup maniaLookup)
            return drawable;

        return maniaLookup.Component switch
        {
            ManiaSkinComponents.HoldNoteBody => new O2JamLegacyHoldBodyPiece(drawable),
            ManiaSkinComponents.HoldNoteHead or ManiaSkinComponents.HoldNoteTail => new O2JamLegacyHoldPiece(drawable),
            _ => drawable,
        };
    }

    private sealed partial class O2JamLegacyHoldPiece : CompositeDrawable
    {
        private readonly Drawable piece;
        private IBindable<double?>? missingStartTime;

        public O2JamLegacyHoldPiece(Drawable piece)
        {
            this.piece = piece;
            RelativeSizeAxes = piece.RelativeSizeAxes;
            AutoSizeAxes = (piece as CompositeDrawable)?.AutoSizeAxes ?? Axes.None;
            InternalChild = piece;
        }

        [BackgroundDependencyLoader]
        private void load(DrawableHitObject drawableObject)
        {
            missingStartTime = drawableObject switch
            {
                DrawableHoldNote hold => hold.MissingStartTime,
                DrawableHoldNoteHead head => head.MissingStartTime,
                DrawableHoldNoteTail tail => tail.MissingStartTime,
                _ => null,
            };
        }

        protected override void UpdateAfterChildren()
        {
            base.UpdateAfterChildren();

            // The hold-note parent owns the O2Jam visual toggle for all skin systems. Resetting
            // this legacy-only native tint avoids multiplying two grey tints together.
            if (missingStartTime?.Value != null)
                piece.Colour = Colour4.White;
        }
    }
}
