using System;
using System.IO;
using System.Linq;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal static class O2JamSourcePresence
{
    public static bool IsDefinitelyMissing(string path)
    {
        try
        {
            var candidate = Path.GetFullPath(path);
            // File.Exists also returns false for inaccessible/offline sources. Walk missing
            // parents only until a readable ancestor proves a path segment is absent. An
            // existing but unreadable mount/directory is inconclusive, even after walking up.
            while (Path.GetDirectoryName(candidate) is { } parent)
            {
                try
                {
                    var entries = Directory.EnumerateFileSystemEntries(parent, "*", new EnumerationOptions
                    {
                        IgnoreInaccessible = false,
                        AttributesToSkip = 0,
                    }).ToArray();
                    return !entries.Any(entry => string.Equals(Path.GetFileName(entry), Path.GetFileName(candidate), StringComparison.OrdinalIgnoreCase));
                }
                catch (DirectoryNotFoundException)
                {
                    candidate = parent;
                }
            }
            return false;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or ArgumentException or NotSupportedException)
        {
            return false;
        }
    }
}
