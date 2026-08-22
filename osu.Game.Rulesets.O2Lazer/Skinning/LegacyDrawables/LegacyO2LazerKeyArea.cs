using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Textures;
using osu.Framework.Input.Bindings;
using osu.Framework.Input.Events;
using osu.Game.Rulesets.O2Lazer.Configuration;
using osu.Game.Rulesets.O2Lazer.IO.Input;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

internal sealed partial class LegacyO2LazerKeyArea : CompositeDrawable, IKeyBindingHandler<O2LazerAction>
{
    private readonly O2LazerSkinComponentLookup lookup;
    private readonly string upImage;
    private readonly string downImage;

    private Drawable? upSprite;
    private Drawable? downSprite;

    public LegacyO2LazerKeyArea(O2LazerLegacySkinTransformer transformer, O2LazerSkinComponentLookup lookup)
    {
        this.lookup = lookup;
        upImage = transformer.GetKeyImageName(lookup, false);
        downImage = transformer.GetKeyImageName(lookup, true);

        RelativeSizeAxes = Axes.Both;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin)
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

        InternalChild = new Container
        {
            Anchor = Anchor.BottomCentre,
            Origin = Anchor.BottomCentre,
            RelativeSizeAxes = Axes.Both,
            Children =
            [
                upSprite ?? Empty(),
                downSprite ?? Empty(),
            ],
        };
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
