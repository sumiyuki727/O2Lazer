using osu.Game.Rulesets.O2Lazer.Parsing;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Skinning.Components;

public class O2LazerSkinComponentLookup(
    O2LazerSkinComponents component,
    O2LazerLayoutVariant layoutVariant = O2LazerLayoutVariant.O2Jam7K,
    int? columnIndex = null,
    bool isLongNote = false
)
    : SkinComponentLookup<O2LazerSkinComponents>(component)
{
    public readonly O2LazerLayoutVariant LayoutVariant = layoutVariant;

    public readonly int? ColumnIndex = columnIndex;

    public readonly bool IsLongNote = isLongNote;

    public bool IsScratch => ColumnIndex != null && O2LazerLayout.IsScratchColumn(ColumnIndex.Value, LayoutVariant);

    public int ManiaKeyCount => O2LazerLayout.GetManiaKeyCount(LayoutVariant);

    public int? ManiaColumnIndex => ColumnIndex == null ? null : O2LazerLayout.MapToManiaColumn(ColumnIndex.Value, LayoutVariant);

}

public enum O2LazerSkinComponents
{
    ColumnBackground,
    ColumnLight,
    HitTarget,
    KeyArea,
    Mine,
    Note,
    HoldNoteHead,
    HoldNoteTail,
    HoldNoteBody,
    HitExplosion,
    StageBackground,
    StageForeground,
    BarLine,
}
