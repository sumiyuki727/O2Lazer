using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

/// <summary>
/// Legacy bottom stage overlay.
/// </summary>
/// <remarks>
/// This is a decorative foreground strip anchored to the bottom of the stage. It intentionally does
/// not participate in input or hit-object layout.
/// </remarks>
internal sealed partial class LegacyO2LazerStageForeground : CompositeDrawable
{
    private readonly string imageName;
    private Drawable? sprite;
    private IBindable<ScrollingDirection> direction = null!;

    public LegacyO2LazerStageForeground(O2LazerLegacySkinTransformer transformer)
    {
        RelativeSizeAxes = Axes.Both;
        imageName = transformer.GetStageForegroundImageName();
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        sprite = skin.GetAnimation(imageName, true, true)?.With(d =>
        {
            d.Anchor = Anchor.BottomCentre;
            d.Origin = Anchor.BottomCentre;
            d.Scale = new Vector2(1.6f);
        });

        InternalChild = sprite ?? Empty();

        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(change =>
        {
            if (sprite == null)
                return;

            sprite.Anchor = sprite.Origin = change.NewValue == ScrollingDirection.Up
                ? Anchor.TopCentre
                : Anchor.BottomCentre;
        }, true);
    }
}
