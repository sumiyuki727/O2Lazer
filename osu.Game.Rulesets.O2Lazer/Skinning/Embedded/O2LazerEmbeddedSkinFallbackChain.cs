using System;
using System.Collections.Generic;
using osu.Framework.Audio.Sample;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Game.Audio;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Rulesets.O2Lazer.Skinning.Runtime;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Embedded;

/// <summary>
/// Owns the ruleset-embedded fallback skins used after the parent osu! skin chain misses.
/// </summary>
public sealed class O2LazerEmbeddedSkinFallbackChain : IDisposable
{
    private readonly O2LazerLegacySkinTransformer primary;
    private readonly O2LazerLegacySkinTransformer? fallback;

    internal O2LazerEmbeddedSkinFallbackChain(O2LazerLegacySkinTransformer primary, O2LazerLegacySkinTransformer? fallback)
    {
        this.primary = primary;
        this.fallback = fallback;
    }

    internal IEnumerable<ISkin> AllSources
    {
        get
        {
            yield return primary;

            if (fallback != null)
                yield return fallback;
        }
    }

    internal Drawable? GetDrawableComponent(ISkinComponentLookup lookup) =>
        primary.GetDrawableComponent(lookup) ?? fallback?.GetDrawableComponent(lookup);

    internal O2LazerResolvedDrawableFactory? GetDrawableFactory(O2LazerSkinComponentLookup lookup)
    {
        var primaryFactory = ((IO2LazerGameplaySkinDrawableSource)primary).GetDrawableFactory(lookup);
        var fallbackFactory = ((IO2LazerGameplaySkinDrawableSource?)fallback)?.GetDrawableFactory(lookup);

        return primaryFactory ?? fallbackFactory;
    }

    internal Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) =>
        primary.GetTexture(componentName, wrapModeS, wrapModeT)
        ?? fallback?.GetTexture(componentName, wrapModeS, wrapModeT);

    internal ISample? GetSample(ISampleInfo sampleInfo) =>
        primary.GetSample(sampleInfo) ?? fallback?.GetSample(sampleInfo);

    internal IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull
        => primary.GetConfig<TLookup, TValue>(lookup)
           ?? fallback?.GetConfig<TLookup, TValue>(lookup);

    internal ISkin? FindProvider(Func<ISkin, bool> lookupFunction)
    {
        if (lookupFunction(primary))
            return primary;

        if (fallback != null && lookupFunction(fallback))
            return fallback;

        return null;
    }

    public void Dispose()
    {
        dispose(primary);
        dispose(fallback);
    }

    private static void dispose(O2LazerLegacySkinTransformer? transformer)
    {
        if (transformer?.Skin is IDisposable disposable)
            disposable.Dispose();
    }
}
