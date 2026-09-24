using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal sealed class O2JamSourceFolderCollectionService(RealmAccess realm)
{
    internal O2JamSourceFolderCollectionResult Synchronise(string? libraryRoot) => realm.Write(database =>
    {
        var beatmaps = database.All<BeatmapSetInfo>()
                               .Where(set => !set.DeletePending)
                               .AsEnumerable()
                               .Where(O2JamLibraryWriter.isOwnedByO2Lazer)
                               .SelectMany(set => set.Beatmaps)
                               .Where(beatmap => string.Equals(
                                   beatmap.Ruleset.ShortName,
                                   O2LazerIdentity.ShortName,
                                   StringComparison.Ordinal))
                               .Select(beatmap => new O2JamSourceFolderBeatmap(
                                   beatmap.Metadata.Source,
                                   beatmap.MD5Hash))
                               .ToArray();
        var plans = BuildPlans(libraryRoot, beatmaps);
        var prefix = O2LazerStrings.SourceFolderCollectionPrefix.ToString();
        var existing = database.All<BeatmapCollection>()
                               .AsEnumerable()
                               .Where(collection => collection.Name.StartsWith(prefix, StringComparison.Ordinal))
                               .ToList();
        var retained = new HashSet<Guid>();
        var created = 0;
        var updated = 0;

        foreach (var plan in plans)
        {
            var collection = existing.FirstOrDefault(candidate =>
                !retained.Contains(candidate.ID)
                && string.Equals(candidate.Name, plan.Name, StringComparison.Ordinal));

            if (collection == null)
            {
                collection = database.Add(new BeatmapCollection(plan.Name));
                existing.Add(collection);
                created++;
            }

            retained.Add(collection.ID);
            if (replaceHashes(collection, plan.Hashes))
            {
                collection.LastModified = DateTimeOffset.UtcNow;
                updated++;
            }
        }

        var removed = 0;
        foreach (var collection in existing.Where(collection => !retained.Contains(collection.ID)).ToArray())
        {
            database.Remove(collection);
            removed++;
        }

        return new O2JamSourceFolderCollectionResult(created, updated, removed);
    });

    internal int DeleteFeatureCollections() => realm.Write(database =>
    {
        var prefix = O2LazerStrings.SourceFolderCollectionPrefix.ToString();
        var collections = database.All<BeatmapCollection>()
                                  .AsEnumerable()
                                  .Where(collection => collection.Name.StartsWith(prefix, StringComparison.Ordinal))
                                  .ToArray();

        foreach (var collection in collections)
            database.Remove(collection);

        return collections.Length;
    });

    internal static IReadOnlyList<O2JamSourceFolderCollectionPlan> BuildPlans(
        string? libraryRoot,
        IEnumerable<O2JamSourceFolderBeatmap> beatmaps) => beatmaps
            .Where(beatmap => !string.IsNullOrWhiteSpace(beatmap.SourceDirectory)
                              && !string.IsNullOrWhiteSpace(beatmap.Md5Hash))
            .GroupBy(
                beatmap => folderLabel(libraryRoot, beatmap.SourceDirectory),
                StringComparer.OrdinalIgnoreCase)
            .Select(group => new O2JamSourceFolderCollectionPlan(
                O2LazerStrings.SourceFolderCollectionName(group.Key).ToString(),
                group.Select(beatmap => beatmap.Md5Hash)
                     .ToHashSet(StringComparer.OrdinalIgnoreCase)))
            .OrderBy(plan => plan.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static bool replaceHashes(BeatmapCollection collection, IReadOnlySet<string> desired)
    {
        var changed = false;

        foreach (var hash in collection.BeatmapMD5Hashes.ToArray())
        {
            if (desired.Contains(hash))
                continue;

            collection.BeatmapMD5Hashes.Remove(hash);
            changed = true;
        }

        foreach (var hash in desired)
        {
            if (collection.BeatmapMD5Hashes.Contains(hash, StringComparer.OrdinalIgnoreCase))
                continue;

            collection.BeatmapMD5Hashes.Add(hash);
            changed = true;
        }

        return changed;
    }

    private static string folderLabel(string? libraryRoot, string sourceDirectory)
    {
        try
        {
            var source = Path.GetFullPath(sourceDirectory)
                             .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.IsNullOrWhiteSpace(libraryRoot))
            {
                var root = Path.GetFullPath(libraryRoot)
                               .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                var relative = Path.GetRelativePath(root, source);

                if (relative == ".")
                    return leafName(source);

                if (!Path.IsPathRooted(relative)
                    && relative != ".."
                    && !relative.StartsWith($"..{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                    && !relative.StartsWith($"..{Path.AltDirectorySeparatorChar}", StringComparison.Ordinal))
                    return relative.Replace(Path.DirectorySeparatorChar, '/').Replace(Path.AltDirectorySeparatorChar, '/');
            }

            return leafName(source);
        }
        catch (Exception)
        {
            return sourceDirectory;
        }
    }

    private static string leafName(string path)
    {
        var name = Path.GetFileName(path);
        return string.IsNullOrWhiteSpace(name) ? path : name;
    }
}

internal sealed record O2JamSourceFolderBeatmap(string SourceDirectory, string Md5Hash);

internal sealed record O2JamSourceFolderCollectionPlan(string Name, IReadOnlySet<string> Hashes);

internal sealed record O2JamSourceFolderCollectionResult(int Created, int Updated, int Removed);
