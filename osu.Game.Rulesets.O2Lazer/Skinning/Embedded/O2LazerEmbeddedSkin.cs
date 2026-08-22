using System;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Rendering;
using osu.Framework.Graphics.Textures;
using osu.Framework.IO.Stores;
using osu.Game.Audio;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Embedded;

/// <inheritdoc cref="ISkin" />
/// <summary>
/// A lightweight DLL-embedded skin that provides O2LAZER-specific fallback textures.
/// </summary>
/// <remarks>
/// This is the last-resort visual layer in the O2LAZER skin source chain: it is only consulted
/// after the user's selected skin and all other supported osu! skin sources have failed to
/// satisfy a lookup. Beatmap skins are intentionally excluded from O2LAZER gameplay.
/// </remarks>
public sealed class O2LazerEmbeddedSkin : ISkin, IDisposable
{
    /// <summary>
    /// The raw byte store backing this skin, exposed so that callers such as
    /// O2LazerLegacySkinTransformer can read <c>skin.ini</c> without reflection.
    /// </summary>
    internal readonly IResourceStore<byte[]> Resources;

    private readonly TextureStore textures;

    /// <param name="kind">Which embedded asset set to load.</param>
    /// <param name="renderer">Renderer used to upload textures to the GPU.</param>
    public O2LazerEmbeddedSkin(O2LazerEmbeddedSkinKind kind, IRenderer renderer)
    {
        Resources = createStore(kind);

        textures = new TextureStore(renderer, new MaxDimensionLimitedTextureLoaderStore(new TextureLoaderStore(Resources)), scaleAdjust: 1);
    }

    #region Disposal

    /// <inheritdoc/>
    public void Dispose()
    {
        textures.Dispose();
        Resources.Dispose();
    }

    #endregion

    /// <inheritdoc/>
    /// <remarks>Always returns <c>null</c>; this skin provides no drawable components.</remarks>
    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

    /// <inheritdoc />
    /// <summary>
    /// Returns a texture from the embedded store.
    /// Textures are stored at 2x resolution (with <c>@2x</c> suffix in the resource name)
    /// so the lookup explicitly tries the <c>@2x</c>-suffixed name first and stamps
    /// Texture.ScaleAdjust on the result so the framework renders it
    /// at the correct 1x display size.
    /// </summary>
    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT)
    {
        // First try @2x variant (matching the embedded resource filenames).
        var texture = textures.Get($"{componentName}@2x", wrapModeS, wrapModeT);

        if (texture != null)
        {
            texture.ScaleAdjust = 2;
            return texture;
        }

        // Fallback to plain name (for LegacyModern or any 1x texture).
        return textures.Get(componentName, wrapModeS, wrapModeT);
    }

    /// <inheritdoc/>
    /// <remarks>Always returns <c>null</c>; this skin provides no samples.</remarks>
    public ISample? GetSample(ISampleInfo sampleInfo) => null;

    /// <inheritdoc/>
    /// <remarks>Always returns <c>null</c>; this skin provides no configuration values.</remarks>
    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => null;

    private static IResourceStore<byte[]> createStore(O2LazerEmbeddedSkinKind kind)
    {
        var resources = new NamespacedResourceStore<byte[]>(new DllResourceStore(typeof(O2LazerRuleset).Assembly), "Resources");

        return kind switch
        {
            O2LazerEmbeddedSkinKind.LegacyModern => new NamespacedResourceStore<byte[]>(resources, "Skins/Modern"),
            _ => new NamespacedResourceStore<byte[]>(resources, "Textures"),
        };
    }
}

/// <summary>
/// Selects which set of O2LAZER-ruleset-embedded fallback assets to use, based on
/// the aesthetic style of the user's currently active skin.
/// </summary>
public enum O2LazerEmbeddedSkinKind
{
    /// <summary>
    /// Classic osu!stable-style assets (loaded from <c>Resources/Textures/</c>).
    /// Used when the active user skin is DefaultLegacySkin,
    /// RetroSkin, an unrecognised Skin subclass,
    /// or when no skin is active.
    /// </summary>
    LegacyOld,

    /// <summary>
    /// Modern Argon-compatible assets (loaded from <c>Resources/Skins/Modern/</c>).
    /// Used when the active user skin is ArgonSkin,
    /// ArgonProSkin, or TrianglesSkin.
    /// </summary>
    LegacyModern,
}
