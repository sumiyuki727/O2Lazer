using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Animations;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal partial class O2LazerCachedSkinnableDrawable : SkinReloadableDrawable
{
    public Anchor? ComponentAnchor { get; set; } = Anchor.Centre;

    public bool AutoSizeHeight
    {
        init
        {
            if (!value)
                return;

            RelativeSizeAxes = Axes.X;
            AutoSizeAxes = Axes.Y;
        }
    }

    public Drawable Drawable { get; private set; } = null!;

    private readonly O2LazerSkinComponentLookup componentLookup;
    private readonly Func<O2LazerSkinComponentLookup, Drawable>? createDefault;

    [Resolved(CanBeNull = true)]
    private O2LazerGameplaySkinCache? gameplaySkinCache { get; set; }

    public O2LazerCachedSkinnableDrawable(O2LazerSkinComponentLookup lookup, Func<O2LazerSkinComponentLookup, Drawable>? defaultImplementation = null)
    {
        componentLookup = lookup;
        createDefault = defaultImplementation;

        RelativeSizeAxes = Axes.Both;
    }

    public void ResetAnimation() => (Drawable as IFramedAnimation)?.GotoFrame(0);

    internal void SetComponentAnchor(Anchor? anchor)
    {
        ComponentAnchor = anchor;

        if (Drawable != null && ComponentAnchor.HasValue)
        {
            Drawable.Origin = ComponentAnchor.Value;
            Drawable.Anchor = ComponentAnchor.Value;
        }
    }

    protected override void SkinChanged(ISkinSource skin)
    {
        var retrieved = gameplaySkinCache != null
            ? gameplaySkinCache.GetDrawableFactory(componentLookup)?.Create()
            : skin.GetDrawableComponent(componentLookup);

        if (retrieved == null)
        {
            Drawable = createDefault?.Invoke(componentLookup) ?? Empty();
        }
        else
        {
            Drawable = retrieved;
        }

        if (ComponentAnchor.HasValue)
        {
            Drawable.Origin = ComponentAnchor.Value;
            Drawable.Anchor = ComponentAnchor.Value;
        }

        InternalChild = Drawable;
    }
}
