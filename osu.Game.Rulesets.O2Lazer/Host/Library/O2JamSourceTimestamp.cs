using System;
using System.IO;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal static class O2JamSourceTimestamp
{
    public static DateTimeOffset Read(string path)
    {
        var milliseconds = new DateTimeOffset(File.GetLastWriteTimeUtc(path)).ToUnixTimeMilliseconds();
        return DateTimeOffset.FromUnixTimeMilliseconds(milliseconds);
    }

}
