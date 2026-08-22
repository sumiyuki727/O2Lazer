using System.Collections.Generic;
using osu.Framework.Graphics.Rendering;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Skinning.Legacy;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Embedded;

/// <summary>
/// Builds the embedded O2LAZER skin fallback chain that matches the currently active osu! skin style.
/// </summary>
public static class O2LazerEmbeddedSkinFallbackFactory
{
    public static O2LazerEmbeddedSkinFallbackChain? Create(IEnumerable<ISkin> parentSources, O2LazerBeatmap beatmap, IRenderer renderer)
    {
        var kind = GetEmbeddedSkinKind(parentSources);
        var primary = createTransformer(kind, beatmap, renderer);
        var fallback = kind == O2LazerEmbeddedSkinKind.LegacyOld
            ? null
            : createTransformer(O2LazerEmbeddedSkinKind.LegacyOld, beatmap, renderer);

        return new O2LazerEmbeddedSkinFallbackChain(primary, fallback);
    }

    /// <summary>
    /// Determines which O2LazerEmbeddedSkinKind to use based on the currently active skin sources.
    /// </summary>
    public static O2LazerEmbeddedSkinKind GetEmbeddedSkinKind(IEnumerable<ISkin> sources)
    {
        foreach (var source in sources)
        {
            var skin = source is ISkinTransformer transformer ? transformer.Skin : source;

            if (skin is LegacyBeatmapSkin or O2LazerEmbeddedSkin)
                continue;

            if (O2LazerEmbeddedSkinDefinition.TryGetKind(skin, out var kind))
                return kind;

            if (skin is Skin)
                return O2LazerEmbeddedSkinKind.LegacyOld;
        }

        return O2LazerEmbeddedSkinKind.LegacyOld;
    }

    private static O2LazerLegacySkinTransformer createTransformer(O2LazerEmbeddedSkinKind kind, O2LazerBeatmap beatmap, IRenderer renderer) =>
        new(new O2LazerEmbeddedSkin(kind, renderer), beatmap);
}
