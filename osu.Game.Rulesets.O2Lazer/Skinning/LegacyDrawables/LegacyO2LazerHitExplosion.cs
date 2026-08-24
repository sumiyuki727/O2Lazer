using System;
using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.O2Lazer.UI.Components;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

/// <summary>
/// Additive legacy hit-light animation for a single column.
/// </summary>
/// <remarks>
/// O2LAZER uses normal hit lights for notes and LN hit lights for held notes. Frame length is derived
/// from the animation frame count so short animations still finish near the expected stable timing.
/// </remarks>
internal sealed partial class LegacyO2LazerHitExplosion : CompositeDrawable
{
    public float ResolvedScale { get; }

    private readonly string imageName;
    private readonly Color4 colour;
    private readonly float hitPosition;

    private Drawable? hitExplosion;
    private IBindable<ScrollingDirection> direction = null!;

    public LegacyO2LazerHitExplosion(O2LazerLegacySkinTransformer transformer, O2LazerSkinComponentLookup lookup)
    {
        RelativeSizeAxes = Axes.Both;

        imageName = transformer.GetHitExplosionImageName(lookup);
        var scale = transformer
                        .GetManiaConfig<float>(
                            lookup.IsLongNote ? LegacyManiaSkinConfigurationLookups.HoldNoteLightScale : LegacyManiaSkinConfigurationLookups.ExplosionScale, lookup)
                        ?.Value
                    ?? 1;
        colour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.ColumnLightColour, lookup)?.Value ?? Color4.White;
        hitPosition = transformer.GetManiaConfig<float>(LegacyManiaSkinConfigurationLookups.HitPosition)?.Value ?? O2LazerStage.HIT_TARGET_POSITION;
        ResolvedScale = scale;

        setAnimation(frameLength => transformer.GetAnimation(imageName, true, false, frameLength: frameLength));
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        if (hitExplosion == null)
        {
            setAnimation(frameLength => skin.GetAnimation(imageName, true, false, frameLength: frameLength));
            if (hitExplosion == null)
                InternalChild = Empty();
        }

        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(_ => updatePosition(), true);
    }

    private void updatePosition()
    {
        if (hitExplosion == null)
            return;

        hitExplosion.Anchor = direction.Value == ScrollingDirection.Up ? Anchor.TopCentre : Anchor.BottomCentre;
        hitExplosion.Y = direction.Value == ScrollingDirection.Up ? hitPosition : -hitPosition;
    }

    private void setAnimation(Func<double, Drawable?> getAnimation)
    {
        var tmp = getAnimation(0);
        double frameLength = 0;

        if (tmp is IFramedAnimation tmpAnimation && tmpAnimation.FrameCount > 0)
            frameLength = Math.Max(1000 / 60.0, 170.0 / tmpAnimation.FrameCount);

        hitExplosion = getAnimation(frameLength)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.Centre;
            d.Y = -hitPosition;
            d.Blending = BlendingParameters.Additive;
            d.Colour = LegacyColourCompatibility.DisallowZeroAlpha(colour);
            d.Scale = new Vector2(ResolvedScale);
        });

        if (hitExplosion != null)
            InternalChild = hitExplosion;
    }
}
