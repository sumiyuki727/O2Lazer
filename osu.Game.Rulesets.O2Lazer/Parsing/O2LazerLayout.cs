using System;

namespace osu.Game.Rulesets.O2Lazer.Parsing;

public static class O2LazerLayout
{
    public const int O2JAM_KEY_COLUMNS = 7;

    public static O2LazerLayoutVariant VariantFromTotalColumns(int totalColumns) => O2LazerLayoutVariant.O2Jam7K;

    public static int GetTotalColumns(O2LazerLayoutVariant variant) => O2JAM_KEY_COLUMNS;

    public static (int Left, int Right) RemapColum2PGapIdx(int idx, int columns) => (idx, idx);

    public static bool Is2P(O2LazerLayoutVariant variant) => false;

    public static bool IsScratchColumn(int column, O2LazerLayoutVariant layoutVariant) => false;

    public static int GetManiaKeyCount(O2LazerLayoutVariant layoutVariant) => O2JAM_KEY_COLUMNS;

    public static int MapToManiaColumn(int column, O2LazerLayoutVariant layoutVariant) => Math.Clamp(column, 0, O2JAM_KEY_COLUMNS - 1);
}
