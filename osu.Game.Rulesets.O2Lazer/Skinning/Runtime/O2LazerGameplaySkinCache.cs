using System;
using System.Collections.Generic;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal sealed class O2LazerGameplaySkinCache : IDisposable
{
    private readonly ISkinSource skin;
    private readonly Dictionary<O2LazerDrawableFactoryCacheKey, O2LazerResolvedDrawableFactory?> drawableFactories = new();
    private readonly Dictionary<O2LazerDrawableFactoryCacheKey, O2LazerResolvedNoteMetrics> noteMetrics = new();

    public O2LazerGameplaySkinCache(ISkinSource skin)
    {
        this.skin = skin;
        skin.SourceChanged += clear;
    }

    #region Disposal

    public void Dispose()
    {
        skin.SourceChanged -= clear;
        clear();
    }

    #endregion

    public O2LazerResolvedDrawableFactory? GetDrawableFactory(O2LazerSkinComponentLookup lookup)
    {
        var key = O2LazerDrawableFactoryCacheKey.From(lookup);

        if (!drawableFactories.TryGetValue(key, out var factory))
        {
            factory = O2LazerGameplaySkinDrawableResolver.Resolve(skin, lookup);
            drawableFactories[key] = factory;
        }

        return factory;
    }

    public float GetNoteHeight(O2LazerSkinComponentLookup lookup, float drawWidth)
    {
        var key = O2LazerDrawableFactoryCacheKey.From(lookup);

        if (!noteMetrics.TryGetValue(key, out var metrics))
        {
            metrics = O2LazerGameplaySkinMetricsResolver.ResolveNoteMetrics(skin, lookup);
            noteMetrics[key] = metrics;
        }

        return metrics.HeightFor(drawWidth);
    }

    private void clear()
    {
        drawableFactories.Clear();
        noteMetrics.Clear();
    }
}
