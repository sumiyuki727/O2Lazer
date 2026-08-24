using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Framework.Graphics.Containers;
using osu.Framework.Utils;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.Scoring;
using osu.Game.Rulesets.UI.Scrolling;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.LegacyDrawables;

/// <summary>
/// Legacy O2LAZER judgement image wrapper.
/// </summary>
/// <remarks>
/// O2LAZER judgement names are mapped to osu!mania legacy image names by
/// O2LazerLegacySkinTransformer. This drawable only owns the playback transform:
/// PGREAT/GREAT/GOOD use the shrinking stable-style pop, while POOR/E-POOR use a simpler pulse.
/// </remarks>
internal sealed partial class LegacyO2LazerJudgementPiece : CompositeDrawable, IAnimatableJudgement
{
    private readonly HitResult result;
    private readonly Drawable animation;

    private IBindable<ScrollingDirection> direction = null!;

    public LegacyO2LazerJudgementPiece(HitResult result, Drawable animation)
    {
        this.result = result;
        this.animation = animation;

        Origin = Anchor.Centre;
        AutoSizeAxes = Axes.Both;
    }

    public void PlayAnimation()
    {
        (animation as IFramedAnimation)?.GotoFrame(0);

        this.FadeInFromZero(20, Easing.Out)
            .Then().Delay(160)
            .FadeOutFromOne(40, Easing.In);

        if (result is HitResult.Meh or HitResult.Miss)
        {
            animation.ScaleTo(1.2f).Then().ScaleTo(1, 100, Easing.Out);

            if (result == HitResult.Miss)
            {
                animation.RotateTo(0);
                animation.RotateTo(RNG.NextSingle(-5.73f, 5.73f), 100, Easing.Out);
            }

            return;
        }

        animation.ScaleTo(0.8f)
            .Then().ScaleTo(1, 40)
            .Then().ScaleTo(0.85f)
            .Then().ScaleTo(0.7f, 40)
            .Then().Delay(100)
            .Then().ScaleTo(0.4f, 40, Easing.In);
    }

    public Drawable? GetAboveHitObjectsProxiedContent() => null;

    [BackgroundDependencyLoader]
    private void load(ISkinSource skin, IScrollingInfo scrollingInfo)
    {
        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(_ => updatePosition(skin), true);
    }

    private void updatePosition(ISkinSource skin)
    {
        float hitPosition = skin.GetConfig<O2LazerSkinConfigurationLookup, float>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.HitPosition)
        )?.Value ?? 0;
        float scorePosition = skin.GetConfig<O2LazerSkinConfigurationLookup, float>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.ScorePosition)
        )?.Value ?? 0;

        float hitPositionFromTop = 480f * LegacyManiaSkinConfiguration.POSITION_SCALE_FACTOR - hitPosition;

        if (direction.Value == ScrollingDirection.Up)
        {
            Anchor = scorePosition > hitPositionFromTop / 2f ? Anchor.TopCentre : Anchor.BottomCentre;
            Y = scorePosition > hitPositionFromTop / 2f ? hitPositionFromTop - scorePosition : -scorePosition;
        }
        else if (scorePosition > hitPositionFromTop / 2f)
        {
            Anchor = Anchor.BottomCentre;
            Y = scorePosition - hitPositionFromTop;
        }
        else
        {
            Anchor = Anchor.TopCentre;
            Y = scorePosition;
        }
    }

    protected override void LoadComplete()
    {
        base.LoadComplete();

        InternalChild = animation.With(d =>
        {
            d.Anchor = Anchor.Centre;
            d.Origin = Anchor.Centre;
        });
    }
}
