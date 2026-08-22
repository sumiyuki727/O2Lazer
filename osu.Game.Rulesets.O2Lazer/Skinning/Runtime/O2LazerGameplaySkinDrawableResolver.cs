using osu.Game.Rulesets.O2Lazer.Skinning.Components;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal static class O2LazerGameplaySkinDrawableResolver
{
    public static O2LazerResolvedDrawableFactory Resolve(ISkinSource skin, O2LazerSkinComponentLookup lookup)
    {
        if (skin is IO2LazerGameplaySkinDrawableSource source
            && source.GetDrawableFactory(lookup) is { } sourceFactory)
        {
            return sourceFactory;
        }

        foreach (var provider in skin.AllSources)
        {
            if (provider is IO2LazerGameplaySkinDrawableSource factorySource
                && factorySource.GetDrawableFactory(lookup) is { } factory)
            {
                return factory;
            }
        }

        return new O2LazerResolvedDrawableFactory(() => skin.GetDrawableComponent(lookup));
    }
}
