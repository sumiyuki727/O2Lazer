using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Rulesets.O2Lazer.Skinning.Components;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Runtime;

internal readonly record struct O2LazerDrawableFactoryCacheKey(
    O2LazerSkinComponents Component,
    O2LazerLayoutVariant LayoutVariant,
    int? Column,
    bool IsLongNote)
{
    public static O2LazerDrawableFactoryCacheKey From(O2LazerSkinComponentLookup lookup) =>
        new(lookup.Component, lookup.LayoutVariant, lookup.ColumnIndex, lookup.IsLongNote);
}
