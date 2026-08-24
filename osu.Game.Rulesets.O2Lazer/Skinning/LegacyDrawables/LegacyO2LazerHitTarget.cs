using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Shapes;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;
using osuTK;
using osuTK.Graphics;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

/// <summary>
/// Keeps the legacy target texture continuous while its stage-level position places it below column lights and notes.
/// </summary>
internal sealed partial class LegacyO2LazerHitTarget : CompositeDrawable
{
    internal Drawable Target { get; private set; } = null!;

    private readonly string targetImage;
    private readonly bool showJudgementLine;
    private readonly Color4 lineColour;
    private Container directionContainer = null!;

    private IBindable<ScrollingDirection> direction = null!;

    public LegacyO2LazerHitTarget(O2LazerLegacySkinTransformer transformer)
    {
        RelativeSizeAxes = Axes.X;
        AutoSizeAxes = Axes.Y;

        targetImage = transformer.GetHitTargetImageName();
        showJudgementLine = transformer.GetManiaConfig<bool>(LegacyManiaSkinConfigurationLookups.ShowJudgementLine)?.Value ?? true;
        lineColour = transformer.GetManiaConfig<Color4>(LegacyManiaSkinConfigurationLookups.JudgementLineColour)?.Value ?? Color4.White;
    }

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        InternalChild = directionContainer = new Container
        {
            Origin = Anchor.CentreLeft,
            RelativeSizeAxes = Axes.X,
            AutoSizeAxes = Axes.Y,
            Children =
            [
                Target = skin.GetAnimation(targetImage, true, true)?.With(d =>
                {
                    d.RelativeSizeAxes = Axes.X;
                    d.Width = 1;
                    d.Scale = new Vector2(1, 1.44225f);
                }) ?? Empty(),
                new Box
                {
                    Anchor = Anchor.CentreLeft,
                    RelativeSizeAxes = Axes.X,
                    Height = 1,
                    Colour = LegacyColourCompatibility.DisallowZeroAlpha(lineColour),
                    Alpha = showJudgementLine ? 0.9f : 0,
                },
            ],
        };

        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(change =>
        {
            if (change.NewValue == ScrollingDirection.Up)
            {
                directionContainer.Anchor = Anchor.TopLeft;
                directionContainer.Scale = new Vector2(1, -1);
            }
            else
            {
                directionContainer.Anchor = Anchor.BottomLeft;
                directionContainer.Scale = Vector2.One;
            }
        }, true);
    }
}
