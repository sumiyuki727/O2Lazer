using System;
using System.Globalization;

namespace osu.Game.Rulesets.O2Lazer.Core;

/// <summary>
/// Maps OJN's three-digit-capable chart level onto osu!'s conventional star-rating scale.
/// </summary>
public static class O2JamDifficultyRating
{
    public static double FromLevel(double level) => level * 0.1;

    public static bool TryParseLevel(string difficultyName, out ushort level)
    {
        level = 0;
        if (string.IsNullOrWhiteSpace(difficultyName))
            return false;

        var end = difficultyName.Length;
        while (end > 0 && char.IsWhiteSpace(difficultyName[end - 1]))
            end--;

        var start = end;
        while (start > 0 && char.IsAsciiDigit(difficultyName[start - 1]))
            start--;

        return start < end
               && ushort.TryParse(difficultyName.AsSpan(start, end - start), NumberStyles.None, CultureInfo.InvariantCulture, out level);
    }

    public static ushort ResolveLevel(string difficultyName, double existingStarRating)
    {
        if (TryParseLevel(difficultyName, out var level))
            return level;

        return existingStarRating < 0
            ? (ushort)0
            : (ushort)Math.Clamp(Math.Round(existingStarRating * 10), 0, ushort.MaxValue);
    }
}
