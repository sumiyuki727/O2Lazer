using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

internal sealed partial class LegacyO2LazerKeyArea : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private readonly string upImage;
    private readonly string downImage;

    private Drawable? upSprite;
    private Drawable? downSprite;
    private Container keyAreaContainer = null!;
    private IBindable<ScrollingDirection> direction = null!;

    public LegacyO2LazerKeyArea(O2LazerLegacySkinTransformer transformer, O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        upImage = transformer.GetKeyImageName(lookup, false);
        downImage = transformer.GetKeyImageName(lookup, true);

        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        upSprite = skin.GetAnimation(upImage, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.RelativeSizeAxes = Axes.X;
            d.Width = 1;
        });

        downSprite = skin.GetAnimation(downImage, WrapMode.ClampToEdge, WrapMode.ClampToEdge, true, true)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.RelativeSizeAxes = Axes.X;
            d.Width = 1;
            d.Alpha = 0;
        });

        InternalChild = keyAreaContainer = new Container
        {
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children =
            [
                upSprite ?? Empty(),
                downSprite ?? Empty(),
            ],
        };

        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(change =>
        {
            if (change.NewValue == ScrollingDirection.Up)
            {
                keyAreaContainer.Anchor = keyAreaContainer.Origin = Anchor.TopCentre;

                if (upSprite != null)
                {
                    upSprite.Anchor = Anchor.TopCentre;
                    upSprite.Scale = new Vector2(1, -1);
                }

                if (downSprite != null)
                {
                    downSprite.Anchor = Anchor.TopCentre;
                    downSprite.Scale = new Vector2(1, -1);
                }
            }
            else
            {
                keyAreaContainer.Anchor = keyAreaContainer.Origin = Anchor.BottomCentre;

                if (upSprite != null)
                {
                    upSprite.Anchor = Anchor.BottomCentre;
                    upSprite.Scale = Vector2.One;
                }

                if (downSprite != null)
                {
                    downSprite.Anchor = Anchor.BottomCentre;
                    downSprite.Scale = Vector2.One;
                }
            }
        }, true);
    }

    public bool OnPressed(KeyBindingPressEvent<O2LazerAction> e)
    {
        if (lookup.ColumnIndex == null || O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return false;

        if (downSprite == null)
            return false;

        upSprite?.FadeTo(0);
        downSprite.FadeTo(1);
        return false;
    }

    public void OnReleased(KeyBindingReleaseEvent<O2LazerAction> e)
    {
        if (lookup.ColumnIndex == null || O2LazerKeyBindingConfiguration.ActionToColumn(e.Action, lookup.LayoutVariant) != lookup.ColumnIndex)
            return;

        upSprite?.Delay(O2LazerLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION).FadeTo(1);
        downSprite?.Delay(O2LazerLegacySkinTransformer.HIT_EXPLOSION_FADE_IN_DURATION).FadeTo(0);
    }
}
