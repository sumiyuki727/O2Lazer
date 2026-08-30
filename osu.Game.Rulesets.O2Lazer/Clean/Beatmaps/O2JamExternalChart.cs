using System;
using System.IO;
using osu.Game.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Beatmaps;

public static class O2JamExternalChart
{
    public static bool IsO2JamEntry(BeatmapInfo beatmapInfo) =>
        string.Equals(beatmapInfo.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal)
        && SourceFileName(beatmapInfo) != null
        && !string.IsNullOrWhiteSpace(beatmapInfo.Metadata.Source);

    public static bool TryResolve(BeatmapInfo beatmapInfo, out string path)
    {
        path = string.Empty;

        var fileName = SourceFileName(beatmapInfo);
        if (fileName == null || !IsO2JamEntry(beatmapInfo) || !Directory.Exists(beatmapInfo.Metadata.Source))
            return false;

        try
        {
            var source = Path.GetFullPath(beatmapInfo.Metadata.Source);
            var candidate = Path.GetFullPath(Path.Combine(source, fileName));

            if (!isWithin(candidate, source) || !File.Exists(candidate))
                return false;

            path = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    internal static string? SourceFileName(BeatmapInfo beatmapInfo)
    {
        // Imported difficulties intentionally have distinct gameplay hashes, so BeatmapInfo.Path
        // can no longer resolve the shared OJN Realm file by hash. AudioFile is the explicit
        // source filename written by the importer; Path remains a legacy compatibility fallback.
        if (string.Equals(Path.GetExtension(beatmapInfo.Metadata.AudioFile), ".ojn", StringComparison.OrdinalIgnoreCase))
            return beatmapInfo.Metadata.AudioFile;
        if (string.Equals(Path.GetExtension(beatmapInfo.Path), ".ojn", StringComparison.OrdinalIgnoreCase))
            return beatmapInfo.Path;

        return null;
    }

    public static bool TryResolveResource(string chartPath, string resourceName, out string path)
    {
        path = string.Empty;

        try
        {
            var source = Path.GetDirectoryName(Path.GetFullPath(chartPath));
            if (source == null)
                return false;

            var candidate = Path.GetFullPath(Path.Combine(source, resourceName));
            if (!isWithin(candidate, source) || !File.Exists(candidate))
                return false;

            path = candidate;
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private static bool isWithin(string candidate, string directory)
    {
        var relative = Path.GetRelativePath(directory, candidate);
        return !Path.IsPathRooted(relative)
               && !relative.Equals("..", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
               && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal);
    }
}
