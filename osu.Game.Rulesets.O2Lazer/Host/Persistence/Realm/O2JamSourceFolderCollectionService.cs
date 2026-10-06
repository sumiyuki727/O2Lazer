using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Configuration;
using osu.Game.Database;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Localisation;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal sealed class O2JamSourceFolderCollectionService(RealmAccess realm)
{
    internal const string OwnershipSettingKey = "O2LazerSourceFolderCollectionOwnership";

    internal O2JamSourceFolderCollectionResult Synchronise(string? libraryRoot) => realm.Write(database =>
    {
        var ownershipSetting = findOwnershipSetting(database);
        var ownership = readOwnership(ownershipSetting);
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
        var existing = ownership.ToDictionary(entry => entry.Name, entry => database.Find<BeatmapCollection>(entry.ID), StringComparer.Ordinal);
        var retained = new HashSet<Guid>();
        var currentOwnership = new List<CollectionOwnership>();
        var created = 0;
        var updated = 0;

        foreach (var plan in plans)
        {
            existing.TryGetValue(plan.Name, out var collection);

            if (collection == null)
            {
                collection = database.Add(new BeatmapCollection(plan.Name));
                created++;
            }

            retained.Add(collection.ID);
            currentOwnership.Add(new CollectionOwnership(collection.ID, plan.Name));
            if (replaceHashes(collection, plan.Hashes))
            {
                collection.LastModified = DateTimeOffset.UtcNow;
                updated++;
            }
        }

        var removed = 0;
        foreach (var collection in existing.Values.Where(collection => collection != null && !retained.Contains(collection.ID)))
        {
            database.Remove(collection!);
            removed++;
        }

        writeOwnership(database, ownershipSetting, currentOwnership);
        return new O2JamSourceFolderCollectionResult(created, updated, removed);
    });

    internal int DeleteFeatureCollections() => realm.Write(database =>
    {
        var ownershipSetting = findOwnershipSetting(database);
        var ownership = readOwnership(ownershipSetting);
        var removed = 0;

        foreach (var entry in ownership)
        {
            if (database.Find<BeatmapCollection>(entry.ID) is not { } collection)
                continue;

            database.Remove(collection);
            removed++;
        }

        writeOwnership(database, ownershipSetting, []);
        return removed;
    });

    // Native collections have no ownership field. A private native ruleset setting lets the
    // collection and its ownership commit together, without changing the host's Realm schema.
    // Collections are shared across variants, so their registry always uses variant zero.
    private static RealmRulesetSetting? findOwnershipSetting(Realm database) => database.All<RealmRulesetSetting>()
        .SingleOrDefault(setting => setting.RulesetName == O2LazerIdentity.ShortName
                                    && setting.Variant == 0 && setting.Key == OwnershipSettingKey);

    private static List<CollectionOwnership> readOwnership(RealmRulesetSetting? setting)
    {
        // Older releases only recorded a display prefix; it cannot distinguish their collections
        // from user-created ones. Leave all untracked collections untouched rather than adopt them.
        if (setting == null)
            return [];

        try
        {
            var entries = JsonSerializer.Deserialize<List<CollectionOwnership>>(setting.Value);
            if (entries == null || entries.Any(entry => entry == null || entry.ID == Guid.Empty || string.IsNullOrWhiteSpace(entry.Name))
                                || entries.Select(entry => entry.ID).Distinct().Count() != entries.Count
                                || entries.Select(entry => entry.Name).Distinct(StringComparer.Ordinal).Count() != entries.Count)
                throw new InvalidDataException("The source-folder collection ownership setting is invalid.");

            return entries;
        }
        catch (JsonException exception)
        {
            // Do not replace damaged ownership evidence with a fresh registry and orphan or claim data.
            throw new InvalidDataException("The source-folder collection ownership setting is invalid.", exception);
        }
    }

    private static void writeOwnership(Realm database, RealmRulesetSetting? setting, List<CollectionOwnership> ownership)
    {
        if (ownership.Count == 0)
        {
            if (setting != null)
                database.Remove(setting);
            return;
        }

        var value = JsonSerializer.Serialize(ownership);
        if (setting == null)
        {
            database.Add(new RealmRulesetSetting
            {
                RulesetName = O2LazerIdentity.ShortName,
                Variant = 0,
                Key = OwnershipSettingKey,
                Value = value,
            });
        }
        else if (setting.Value != value)
            setting.Value = value;
    }

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

    private sealed record CollectionOwnership(Guid ID, string Name);
}

internal sealed record O2JamSourceFolderBeatmap(string SourceDirectory, string Md5Hash);

internal sealed record O2JamSourceFolderCollectionPlan(string Name, IReadOnlySet<string> Hashes);

internal sealed record O2JamSourceFolderCollectionResult(int Created, int Updated, int Removed);
