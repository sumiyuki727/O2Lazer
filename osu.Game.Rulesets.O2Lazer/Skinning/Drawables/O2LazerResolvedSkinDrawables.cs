using System;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.NoteTextures;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Drawables;

internal sealed partial class O2LazerResolvedNotePiece : CompositeDrawable
{
    private readonly float? widthForNoteHeightScale;
    private readonly O2LazerSkinComponentLookup lookup;

    private Container directionContainer = null!;
    private Drawable? noteAnimation;

    public O2LazerResolvedNotePiece(O2LazerSkinComponentLookup lookup, Texture[] textures, float? widthForNoteHeightScale)
    {
        this.lookup = lookup;
        this.widthForNoteHeightScale = widthForNoteHeightScale;

        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        if (textures.Length > 0)
            setTextures(textures);
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        if (noteAnimation == null)
            setTextures(O2LazerLegacyTextureResolver.ResolveNoteTextures(skin, lookup));
    }

    private void setTextures(Texture[] textures)
    {
        if (lookup.LayoutVariant != O2LazerLayoutVariant.O2Jam7K)
        {
            InternalChild = noteAnimation = createTextureDrawable(textures, true).With(drawable =>
            {
                drawable.Anchor = Anchor.TopLeft;
                drawable.Origin = Anchor.TopLeft;
            });
            return;
        }

        InternalChild = directionContainer = new Container
        {
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Child = noteAnimation = createTextureDrawable(textures, true),
        };

        // osu!mania inverts only the tail piece in down-scroll. O2Jam currently shares that
        // direction, so this reproduces LegacyHoldNoteTailPiece without introducing a second
        // scrolling implementation into the O2LAZER playfield.
        if (lookup.Component == O2LazerSkinComponents.HoldNoteTail)
        {
            directionContainer.Anchor = Anchor.TopCentre;
            directionContainer.Scale = new Vector2(1, -1);
        }
        else
        {
            directionContainer.Anchor = Anchor.BottomCentre;
            directionContainer.Scale = Vector2.One;
        }
    }

    protected override void Update()
    {
        base.Update();

        var texture = noteAnimation switch
        {
            Sprite sprite => sprite.Texture,
            TextureAnimation animation when animation.FrameCount > 0 => animation.CurrentFrame,
            _ => null,
        };

        if (texture == null)
            return;

        var noteWidth = widthForNoteHeightScale ?? DrawWidth;
        noteAnimation?.Scale = Vector2.Divide(new Vector2(DrawWidth, noteWidth), Math.Max(1, texture.DisplayWidth));
    }

    private static Drawable createTextureDrawable(Texture[] textures, bool looping)
    {
        if (textures.Length == 0)
            return Empty();

        if (textures.Length == 1)
            return new Sprite { Texture = textures[0] };

        var animation = new TextureAnimation
        {
            DefaultFrameLength = 1000 / 60d,
            Loop = looping,
        };

        foreach (var texture in textures)
            animation.AddFrame(texture);

        return animation;
    }
}

/// <summary>
/// osu!mania's original single-drawable stretch/repeat LN body, used for legacy skin textures.
/// </summary>
internal sealed partial class O2LazerLegacyStretchedHoldNoteBodyPiece : CompositeDrawable, IO2LazerManiaHoldNoteBodyPiece
{
    private readonly Drawable? bodySprite;
    private readonly LegacyManiaSkinConfiguration.LegacyNoteBodyStyle? bodyStyle;

    public int AnimationFrameCount => (bodySprite as TextureAnimation)?.FrameCount ?? (bodySprite == null ? 0 : 1);

    public int CurrentAnimationFrame => (bodySprite as TextureAnimation)?.CurrentFrameIndex ?? 0;

    public O2LazerLegacyStretchedHoldNoteBodyPiece(ISkin skin, O2LazerSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        bodyStyle = skin.GetConfig<O2LazerSkinConfigurationLookup, LegacyManiaSkinConfiguration.LegacyNoteBodyStyle>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.NoteBodyStyle, lookup))?.Value;
        var wrapMode = bodyStyle == LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.Stretch ? WrapMode.ClampToEdge : WrapMode.Repeat;

        foreach (var imageName in O2LazerLegacyTextureResolver.HoldBodyImageCandidates(skin, lookup).Where(name => !string.IsNullOrWhiteSpace(name)).Distinct())
        {
            bodySprite = skin.GetAnimation(imageName!, wrapMode, wrapMode, true, true, frameLength: 30);

            if (bodySprite != null)
                break;
        }

        if (bodySprite == null)
            return;

        bodySprite.Anchor = Anchor.TopCentre;
        bodySprite.Origin = Anchor.TopCentre;
        bodySprite.RelativeSizeAxes = Axes.Both;
        bodySprite.Size = Vector2.One;

        if (bodySprite is TextureAnimation animation)
            animation.IsPlaying = false;

        InternalChild = bodySprite;
    }

    public void SetHolding(bool holding)
    {
        if (bodySprite is not TextureAnimation animation)
            return;

        animation.IsPlaying = holding;

        if (!holding)
            animation.GotoFrame(0);
    }

    public void SetTailAtTop(bool tailAtTop, float bodyHeight)
    {
        if (bodySprite == null)
            return;

        var scaleDirection = tailAtTop ? 1 : -1;

        if (bodyStyle == LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.Stretch)
        {
            bodySprite.Scale = new Vector2(1, scaleDirection);
            return;
        }

        bodySprite.FillMode = FillMode.Stretch;
        if (bodyHeight > 0)
            bodySprite.Scale = new Vector2(1, scaleDirection * MathF.Max(1, 32800 / bodyHeight));
    }

    public void Recycle()
    {
        SetHolding(false);
        Colour = Color4.White;
    }
}

/// <summary>
/// Keeps the existing O2LAZER body lookup path lazy while O2Jam uses transformer-provided mania pieces.
/// </summary>
internal sealed partial class O2LazerDeferredResolvedHoldNoteBodyPiece(O2LazerSkinComponentLookup lookup)
    : CompositeDrawable, IO2LazerManiaHoldNoteBodyPiece
{
    private Drawable? resolved;
    private bool holding;
    private bool tailAtTop;
    private float bodyHeight;

    public int AnimationFrameCount => (resolved as IO2LazerManiaHoldNoteBodyPiece)?.AnimationFrameCount ?? 0;

    public int CurrentAnimationFrame => (resolved as IO2LazerManiaHoldNoteBodyPiece)?.CurrentAnimationFrame ?? 0;

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
    {
        InternalChild = resolved = new O2LazerLegacyStretchedHoldNoteBodyPiece(skin, lookup);
        var piece = (IO2LazerManiaHoldNoteBodyPiece)resolved;
        piece.SetTailAtTop(tailAtTop, bodyHeight);
        piece.SetHolding(holding);
    }

    public void SetHolding(bool newHolding)
    {
        holding = newHolding;
        (resolved as IO2LazerManiaHoldNoteBodyPiece)?.SetHolding(newHolding);
    }

    public void SetTailAtTop(bool newTailAtTop, float newBodyHeight)
    {
        tailAtTop = newTailAtTop;
        bodyHeight = newBodyHeight;
        (resolved as IO2LazerManiaHoldNoteBodyPiece)?.SetTailAtTop(newTailAtTop, newBodyHeight);
    }

    public void Recycle()
    {
        holding = false;
        (resolved as IO2LazerManiaHoldNoteBodyPiece)?.Recycle();
    }
}
