using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Containers;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Overlays;
using osu.Game.Rulesets.O2Lazer.Localisation;
using osu.Game.Overlays.Notifications;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.UI.Icons;

public sealed partial class O2LazerRulesetIcon : CompositeDrawable
{
    private const float design_size = 40;

    private readonly Container iconContent;

    private static readonly object texture_store_lock = new();
    private static TextureStore? shared_texture_store;
    private static IRenderer? shared_renderer;

    public O2LazerRulesetIcon()
    {
        Size = new Vector2(design_size);

        InternalChild = iconContent = new Container
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            Size = new Vector2(design_size),
        };
    }

    [BackgroundDependencyLoader]
    private void load(IRenderer renderer)
    {
        iconContent.Child = new Sprite
        {
            Anchor = Anchor.Centre,
            Origin = Anchor.Centre,
            RelativeSizeAxes = Axes.Both,
            Texture = getSharedTextureStore(renderer).Get("Textures/o2jamruleset"),
        };
    }

    private static TextureStore getSharedTextureStore(IRenderer renderer)
    {
        lock (texture_store_lock)
        {
            if (shared_texture_store == null || !ReferenceEquals(shared_renderer, renderer))
            {
                shared_renderer = renderer;
                var resources = new NamespacedResourceStore<byte[]>(new DllResourceStore(typeof(O2LazerRuleset).Assembly), "Resources");
                shared_texture_store = new TextureStore(renderer, new TextureLoaderStore(resources), false);
            }

            return shared_texture_store;
        }
    }

    protected override void Update()
    {
        base.Update();

        var fitScale = Math.Min(DrawWidth / design_size, DrawHeight / design_size);
        iconContent.Scale = new Vector2(fitScale);
    }
}



