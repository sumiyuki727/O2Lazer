using osu.Framework.Graphics;
using osu.Framework.Graphics.Pooling;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

namespace osu.Game.Rulesets.O2Lazer.UI.Components;

public sealed partial class O2LazerHitExplosion : PoolableDrawable
{
    private readonly O2LazerCachedSkinnableDrawable skinnableExplosion;

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

    public void ApplyPositionOffset(float positionOffset) => Y = -positionOffset;

    protected override void PrepareForUse()
    {
        base.PrepareForUse();

        ClearTransforms();
        skinnableExplosion.ResetAnimation();
        LifetimeStart = Time.Current;
        this.FadeInFromZero(80).Then().FadeOut(120).Expire();
    }

    protected override void FreeAfterUse()
    {
        ClearTransforms();
        base.FreeAfterUse();
    }
}
