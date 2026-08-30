using System;
using osu.Framework.Allocation;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Sprites;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osuTK;

namespace osu.Game.Rulesets.O2Lazer.UI.Icons;

public sealed partial class O2JamRulesetIcon : Sprite
{
    private static readonly object textureStoreLock = new();

    private static TextureStore? sharedTextureStore;
    private static IRenderer? sharedRenderer;

    public O2JamRulesetIcon()
    {
        Size = new Vector2(40);
        Anchor = Anchor.Centre;
        Origin = Anchor.Centre;
        FillMode = FillMode.Fit;
    }

    [BackgroundDependencyLoader]
    private void load(IRenderer renderer)
    {
        Texture = getSharedTextureStore(renderer).Get("Textures/Icons/RulesetO2Jam");
    }

    private static TextureStore getSharedTextureStore(IRenderer renderer)
    {
        lock (textureStoreLock)
        {
            if (sharedTextureStore == null || !ReferenceEquals(sharedRenderer, renderer))
            {
                sharedRenderer = renderer;
                var resources = new NamespacedResourceStore<byte[]>(
                    new DllResourceStore(typeof(O2LazerRuleset).Assembly), "Resources");
                sharedTextureStore = new TextureStore(renderer, new TextureLoaderStore(resources), false);
            }

            return sharedTextureStore;
        }
    }
}
