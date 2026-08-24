using osu.Framework.Allocation;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Rulesets.UI.Scrolling;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

internal interface IO2LazerHitExplosion
{
    void Animate(JudgementResult result);
}

public sealed partial class O2LazerHitExplosion : PoolableDrawable
{
    public const double DURATION = 200;

    private readonly O2LazerCachedSkinnableDrawable skinnableExplosion;
    private JudgementResult? result;
    private float positionOffset;
    private IBindable<ScrollingDirection> direction = null!;

    public O2LazerHitExplosion()
        : this(new O2LazerSkinComponentLookup(O2LazerSkinComponents.HitExplosion))
    {
    }

    public O2LazerHitExplosion(O2LazerSkinComponentLookup lookup, float positionOffset = 0)
    {
        RelativeSizeAxes = Axes.Both;
        ApplyPositionOffset(positionOffset);

        InternalChild = skinnableExplosion = new O2LazerCachedSkinnableDrawable(lookup)
        {
            RelativeSizeAxes = Axes.Both,
            ComponentAnchor = null,
        };
    }

    public void ApplyPositionOffset(float positionOffset)
    {
        this.positionOffset = positionOffset;
        applyPositionOffset();
    }

    [BackgroundDependencyLoader]
    private void load(IScrollingInfo scrollingInfo)
    {
        direction = scrollingInfo.Direction.GetBoundCopy();
        direction.BindValueChanged(_ => applyPositionOffset(), true);
    }

    private void applyPositionOffset()
    {
        if (direction == null)
            return;

        Y = direction.Value == ScrollingDirection.Up ? positionOffset : -positionOffset;
    }

    public void Apply(JudgementResult? result) => this.result = result;

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        ClearTransforms();
        skinnableExplosion.ResetAnimation();
        LifetimeStart = Time.Current;

        if (skinnableExplosion.Drawable is IO2LazerHitExplosion explosion && result != null)
        {
            explosion.Animate(result);
            this.Delay(DURATION).Then().Expire();
        }
        else
            this.FadeInFromZero(80).Then().FadeOut(120).Expire();
    }

    protected override void FreeAfterUse()
    {
        ClearTransforms();
        base.FreeAfterUse();
    }
}
