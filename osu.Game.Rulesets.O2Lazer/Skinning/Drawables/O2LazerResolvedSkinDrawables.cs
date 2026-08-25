using System;
using System.Collections.Generic;
using System.Linq;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Primitives;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Game.Rulesets.O2Lazer;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.NoteTextures;
using osu.Game.Rulesets.UI.Scrolling;
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
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        if (noteAnimation == null)
            setTextures(O2LazerLegacyTextureResolver.ResolveNoteTextures(skin, lookup));

        scrollingInfo.Direction.BindValueChanged(direction => updateDirection(direction.NewValue), true);
    }

    private void updateDirection(ScrollingDirection direction)
    {
        if (directionContainer == null)
            return;

        if (lookup.Component == O2LazerSkinComponents.HoldNoteTail)
        {
            directionContainer.Anchor = direction == ScrollingDirection.Up ? Anchor.BottomCentre : Anchor.TopCentre;
            directionContainer.Scale = new Vector2(1, direction == ScrollingDirection.Up ? 1 : -1);
        }
        else
        {
            directionContainer.Anchor = direction == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre;
            directionContainer.Scale = new Vector2(1, direction == ScrollingDirection.Up ? -1 : 1);
        }
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
    // Mania stretches a non-stretch body texture across this fixed span and masks the visible
    // window. Beyond it the legacy single-sprite path cannot cover the remainder, so the opt-in
    // repeat mode draws the full span once and tiles the plain lower half to extend it.
    private const float full_span = 32800;
    private const float half_span = full_span / 2;

    private readonly Drawable? bodySprite;
    private readonly LegacyManiaSkinConfiguration.LegacyNoteBodyStyle? bodyStyle;
    private IBindable<ScrollingDirection> direction = null!;

    private Container? tileContainer;
    private Sprite? firstSegmentSprite;
    private Texture? lowerHalfTexture;
    private readonly List<Sprite> repeatSegments = [];
    private bool tiledModeActive;

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

    [BackgroundDependencyLoader]
    private void load(IScrollingInfo scrollingInfo)
    {
        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(onDirectionChanged, true);
    }

    private void onDirectionChanged(ValueChangedEvent<ScrollingDirection> change)
    {
        if (bodySprite == null)
            return;

        // Mania flips the body sprite's anchor when scaling it for up-scroll, otherwise the
        // mirrored texture is offset by one texture height.
        if (change.NewValue == ScrollingDirection.Up)
        {
            bodySprite.Origin = Anchor.TopCentre;
            bodySprite.Anchor = Anchor.BottomCentre;
        }
        else
        {
            bodySprite.Origin = Anchor.TopCentre;
            bodySprite.Anchor = Anchor.TopCentre;
        }
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

        // The legacy path is identical to osu!mania up to the fixed stretch span. Entering the
        // tiled path for shorter bodies scales the full texture to one full span and lets the
        // mask expose only a middle window, so repeat mode is reserved for bodies past the span.
        if (bodySprite is Sprite sprite
            && bodyStyle != LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.Stretch
            && bodyHeight > full_span
            && (O2LazerRulesetRuntime.ConfigManager?.Get<bool>(O2LazerRulesetSetting.PercyLongNoteBodyRepeat) ?? false))
        {
            ensureTiledLayout(sprite);
            applyTiledLayout(tailAtTop, bodyHeight);
        }
        else
        {
            ensureLegacyLayout();
            applyLegacyLayout(tailAtTop, bodyHeight);
        }
    }

    private void ensureLegacyLayout()
    {
        if (!tiledModeActive)
            return;

        RemoveInternal(tileContainer, true);
        tileContainer = null;
        firstSegmentSprite = null;
        lowerHalfTexture = null;
        repeatSegments.Clear();
        InternalChild = bodySprite;
        tiledModeActive = false;
    }

    private void ensureTiledLayout(Sprite sprite)
    {
        if (tiledModeActive)
            return;

        RemoveInternal(bodySprite, false);

        tileContainer = new Container
        {
            RelativeSizeAxes = Axes.Both,
        };
        firstSegmentSprite = new Sprite
        {
            Texture = sprite.Texture,
            FillMode = FillMode.Stretch,
            RelativeSizeAxes = Axes.X,
        };
        lowerHalfTexture = sprite.Texture.Crop(new RectangleF(
            0,
            sprite.Texture.Height / 2f,
            sprite.Texture.Width,
            sprite.Texture.Height / 2f));

        tileContainer.Add(firstSegmentSprite);
        InternalChild = tileContainer;
        tiledModeActive = true;
    }

    private void applyLegacyLayout(bool tailAtTop, float bodyHeight)
    {
        var scaleDirection = tailAtTop ? 1 : -1;

        if (bodyStyle == LegacyManiaSkinConfiguration.LegacyNoteBodyStyle.Stretch)
        {
            bodySprite!.Scale = new Vector2(1, scaleDirection);
            return;
        }

        bodySprite!.FillMode = FillMode.Stretch;
        if (bodyHeight > 0)
            bodySprite.Scale = new Vector2(1, scaleDirection * MathF.Max(1, 32800 / bodyHeight));
    }

    private void applyTiledLayout(bool tailAtTop, float bodyHeight)
    {
        if (tileContainer == null || firstSegmentSprite == null || lowerHalfTexture == null)
            return;

        // Match the legacy single sprite's geometry for the first span: the sprite occupies the
        // full body and is stretched so its draw quad is one full span tall.
        firstSegmentSprite.Height = bodyHeight;
        firstSegmentSprite.Scale = new Vector2(1, (tailAtTop ? 1 : -1) * full_span / Math.Max(1, bodyHeight));
        firstSegmentSprite.Anchor = tailAtTop ? Anchor.TopCentre : Anchor.BottomCentre;
        firstSegmentSprite.Origin = Anchor.TopCentre;
        firstSegmentSprite.Y = 0;

        var remaining = Math.Max(0, bodyHeight - full_span);
        var needed = (int)Math.Ceiling(remaining / half_span);

        while (repeatSegments.Count < needed)
        {
            var repeat = new Sprite
            {
                Texture = lowerHalfTexture,
                FillMode = FillMode.Stretch,
                RelativeSizeAxes = Axes.X,
            };
            repeatSegments.Add(repeat);
            tileContainer.Add(repeat);
        }

        while (repeatSegments.Count > needed)
        {
            tileContainer.Remove(repeatSegments[^1], true);
            repeatSegments.RemoveAt(repeatSegments.Count - 1);
        }

        for (var i = 0; i < needed; i++)
        {
            var repeat = repeatSegments[i];
            var tileHeight = Math.Min(half_span, Math.Max(0, remaining - i * half_span));
            repeat.Height = tileHeight;
            repeat.Scale = new Vector2(1, (tailAtTop ? 1 : -1) * half_span / Math.Max(1, tileHeight));
            repeat.Anchor = tailAtTop ? Anchor.TopCentre : Anchor.BottomCentre;
            repeat.Origin = Anchor.TopCentre;
            repeat.Y = tailAtTop ? full_span + i * half_span : -(full_span + i * half_span);
        }
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
