using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Rulesets.O2Lazer.Skinning.Configuration;
using osu.Game.Rulesets.O2Lazer.Skinning.NoteTextures;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal static class O2LazerGameplaySkinMetricsResolver
{
    /// <summary>
    /// Default note height used when no skin texture or configuration provides a value.
    /// </summary>
    public const float DEFAULT_NOTE_HEIGHT = 14;

    public static O2LazerResolvedNoteMetrics ResolveNoteMetrics(ISkinSource skin, O2LazerSkinComponentLookup lookup)
    {
        var configuredReferenceWidth = skin.GetConfig<O2LazerSkinConfigurationLookup, float>(
            new O2LazerSkinConfigurationLookup(LegacyManiaSkinConfigurationLookups.WidthForNoteHeightScale, lookup))?.Value;

        var texture = O2LazerLegacyTextureResolver.ResolveNoteTexture(skin, lookup);
        if (texture != null)
            return new O2LazerResolvedNoteMetrics(configuredReferenceWidth, texture.DisplayHeight / texture.DisplayWidth);

        return new O2LazerResolvedNoteMetrics(configuredReferenceWidth, null);
    }

    /// <summary>
    /// Static fallback for callers without a O2LazerGameplaySkinCache.
    /// Returns the note height for the given lookup at the given draw width.
    /// </summary>
    public static float ResolveNoteHeight(ISkinSource? skin, O2LazerSkinComponentLookup lookup, float drawWidth)
    {
        if (skin == null)
            return DEFAULT_NOTE_HEIGHT;

        var metrics = ResolveNoteMetrics(skin, lookup);
        return metrics.HeightFor(drawWidth);
    }
}
