using System;
using System.Globalization;
using System.IO;

namespace osu.Game.Rulesets.O2Lazer.Replays;

internal static class O2LazerReplayFileNameParser
{
    private const string playing_separator = " playing ";

    public static bool TryParse(string fileName, out O2LazerReplayFileMetadata metadata)
    {
        metadata = default;

        var name = Path.GetFileNameWithoutExtension(fileName);
        var playingIndex = name.IndexOf(playing_separator, StringComparison.Ordinal);

        if (playingIndex <= 0)
            return false;

        var player = name[..playingIndex];
        var remainder = name[(playingIndex + playing_separator.Length)..];

        var dateStart = remainder.LastIndexOf(" (", StringComparison.Ordinal);
        if (dateStart < 0)
            return false;

        var datePart = remainder[(dateStart + 2)..^1];
        if (!DateTimeOffset.TryParseExact(datePart, "yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var date))
            return false;

        var diffStart = remainder.LastIndexOf(" [", dateStart, StringComparison.Ordinal);
        if (diffStart < 0)
            return false;

        var diffEnd = remainder.IndexOf(']', diffStart);
        if (diffEnd < 0 || diffEnd >= dateStart)
            return false;

        var difficulty = remainder[(diffStart + 2)..diffEnd];
        var mapperStart = remainder.LastIndexOf(" (", diffStart, StringComparison.Ordinal);
        if (mapperStart < 0)
            return false;

        var artistTitle = remainder[..mapperStart];
        var dashIndex = artistTitle.IndexOf(" - ", StringComparison.Ordinal);
        if (dashIndex <= 0)
            return false;

        metadata = new O2LazerReplayFileMetadata(
            player,
            artistTitle[..dashIndex],
            artistTitle[(dashIndex + 3)..],
            difficulty,
            date);

        return true;
    }
}

internal readonly record struct O2LazerReplayFileMetadata(
    string Player,
    string Artist,
    string Title,
    string Difficulty,
    DateTimeOffset Date);
