using osu.Game.Rulesets.O2Lazer.Skinning.Components;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal interface IO2LazerGameplaySkinDrawableSource
{
    O2LazerResolvedDrawableFactory? GetDrawableFactory(O2LazerSkinComponentLookup lookup);
}
