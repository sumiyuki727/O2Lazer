using System;
using System.Collections.Generic;
using System.IO;
using osu.Game.Rulesets.O2Lazer.IO.ResourceStore;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Preview;

internal static class O2LazerPreviewAudioResolver
{
    internal static IReadOnlyList<string> GetExistingDedicatedPreviewCandidates(string basePath, string? previewFile)
    {
        using var fileStore = new O2LazerFileResourceStore(basePath);
        var candidates = new List<string>();
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var candidate in getDedicatedPreviewCandidates(previewFile))
        {
            foreach (var resolvedPath in resolveCandidatePaths(fileStore, candidate))
            {
                var relativePath = Path.GetRelativePath(basePath, resolvedPath);

                if (seenPaths.Add(relativePath))
                    candidates.Add(relativePath);
            }
        }

        return candidates;
    }

    private static IEnumerable<string> getDedicatedPreviewCandidates(string? previewFile)
    {
        if (!string.IsNullOrWhiteSpace(previewFile))
            yield return previewFile;

        foreach (var extension in O2LazerAudioResourceStore.Extensions)
            yield return $"preview.{extension}";
    }

    private static IEnumerable<string> resolveCandidatePaths(O2LazerFileResourceStore fileStore, string candidate)
    {
        var seenPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var lookup in new O2LazerSampleInfo(candidate).LookupNames)
        {
            if (fileStore.TryResolve(lookup, out var resolvedPath) && seenPaths.Add(resolvedPath))
                yield return resolvedPath;

            var stem = Path.ChangeExtension(lookup, null);

            foreach (var extension in O2LazerAudioResourceStore.Extensions)
            {
                if (fileStore.TryResolve($"{stem}.{extension}", out resolvedPath) && seenPaths.Add(resolvedPath))
                    yield return resolvedPath;
            }
        }
    }
}
