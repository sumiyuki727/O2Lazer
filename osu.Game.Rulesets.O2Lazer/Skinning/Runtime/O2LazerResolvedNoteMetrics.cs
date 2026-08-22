using System;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal readonly record struct O2LazerResolvedNoteMetrics(float? ConfiguredReferenceWidth, float? TextureHeightAspect)
{
    public float HeightFor(float drawWidth)
    {
        var referenceWidth = ConfiguredReferenceWidth ?? drawWidth;

        if (TextureHeightAspect != null)
            return Math.Max(1, TextureHeightAspect.Value * referenceWidth);

        return ConfiguredReferenceWidth != null
            ? Math.Max(1, referenceWidth)
            : O2LazerGameplaySkinMetricsResolver.DEFAULT_NOTE_HEIGHT;
    }
}
