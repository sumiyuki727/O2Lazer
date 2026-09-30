using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using osu.Game.Beatmaps;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Import;

// Realm objects stay within the transaction. Reindex touched sets immediately so a later
// request sees the committed candidate paths and content, rather than a scan-time hint.
internal sealed class O2JamLibraryWriteIndex
{
    private readonly Dictionary<Guid, Entry> entries = new();
    private readonly Dictionary<string, HashSet<Guid>> paths = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<Guid>> content = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, HashSet<Guid>> hashes = new(StringComparer.OrdinalIgnoreCase);

    public O2JamLibraryWriteIndex(Realm database)
    {
        var sets = database.All<BeatmapInfo>()
                           .Filter($@"{nameof(BeatmapInfo.BeatmapSet)}.{nameof(BeatmapSetInfo.DeletePending)} == false")
                           .Filter($@"{nameof(BeatmapInfo.Ruleset)}.{nameof(osu.Game.Rulesets.RulesetInfo.ShortName)} == $0", O2LazerIdentity.ShortName)
                           .AsEnumerable().Where(beatmap => beatmap.BeatmapSet != null)
                           .Select(beatmap => beatmap.BeatmapSet!).DistinctBy(set => set.ID).ToArray();
        foreach (var set in sets)
            Update(set);
    }

    public BeatmapSetInfo? FindPath(string path) => unique(paths, Path.GetFullPath(path));
    public BeatmapSetInfo? FindContent(string hash) => unique(content, hash);
    public bool ContainsSetHash(string hash) => hashes.ContainsKey(hash);

    public void Update(BeatmapSetInfo set)
    {
        if (entries.Remove(set.ID, out var old))
        {
            remove(paths, old.Paths, set.ID);
            remove(content, old.Content, set.ID);
            remove(hashes, [old.Hash], set.ID);
        }
        if (set.DeletePending)
            return;

        var sourcePaths = set.Beatmaps.Select(beatmap => O2JamLibraryWriter.tryGetSourcePath(beatmap, out var path) ? path : null)
                             .OfType<string>().Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sourceHashes = set.Files.Where(file => file.Filename.EndsWith(".ojn", StringComparison.OrdinalIgnoreCase))
                              .Select(file => file.File.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var contentHashes = sourceHashes.Length == 1 ? sourceHashes : sourceHashes.Length == 0
            ? set.Beatmaps.Where(beatmap => beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName).Select(beatmap => beatmap.Hash).Distinct().ToArray() : [];
        entries[set.ID] = new Entry(set, sourcePaths, contentHashes, set.Hash);
        add(paths, sourcePaths, set.ID);
        add(content, contentHashes, set.ID);
        add(hashes, [set.Hash], set.ID);
    }

    private BeatmapSetInfo? unique(Dictionary<string, HashSet<Guid>> index, string key)
    {
        if (!index.TryGetValue(key, out var ids))
            return null;
        if (ids.Count != 1)
            throw new InvalidDataException("Multiple stored sets claim the same OJN path or content; automatic merging is unavailable.");
        return entries[ids.Single()].Set;
    }

    private static void add(Dictionary<string, HashSet<Guid>> index, IEnumerable<string> keys, Guid id)
    {
        foreach (var key in keys)
        {
            if (!index.TryGetValue(key, out var ids))
                index[key] = ids = [];
            ids.Add(id);
        }
    }

    private static void remove(Dictionary<string, HashSet<Guid>> index, IEnumerable<string> keys, Guid id)
    {
        foreach (var key in keys)
        {
            var ids = index[key];
            ids.Remove(id);
            if (ids.Count == 0)
                index.Remove(key);
        }
    }

    private sealed record Entry(BeatmapSetInfo Set, string[] Paths, string[] Content, string Hash);
}
