using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using osu.Game.Beatmaps;
using osu.Game.Models;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Import;

// Materialise only this batch's native query matches once. Tracked models stay inside the
// transaction so path/hash changes and newly imported sets are visible to later requests.
internal sealed class O2JamLibraryLookup
{
    private readonly Realm database;
    private readonly Dictionary<Guid, BeatmapSetInfo> candidates = [];
    internal int PathPredicateCount { get; private set; }
    internal int PathWildcardGroupCount { get; private set; }

    public O2JamLibraryLookup(Realm database, IReadOnlyList<O2JamLibraryWriteRequest> requests,
                            Stopwatch? setQueryTime = null, Stopwatch? legacyQueryTime = null,
                            Stopwatch? pathQueryTime = null, Stopwatch? legacyPathQueryTime = null, Stopwatch? hashQueryTime = null)
    {
        this.database = database;
        var names = requests.Select(request => Path.GetFileName(request.Plan.SourcePath)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var hashes = requests.Select(request => request.Plan.SourceHash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var setHashes = requests.Select(request => request.Plan.SetHash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var ruleset = database.Find<RulesetInfo>(O2LazerIdentity.ShortName);
        if (ruleset == null || requests.Count == 0)
            return;

        // Compare the native ruleset link instead of traversing its name on every row.
        // No path predicate traverses parent Files; that combined query regressed on the host.
        var owned = database.All<BeatmapInfo>().Filter("Ruleset == $0 AND BeatmapSet != NULL AND BeatmapSet.DeletePending == false", ruleset);
        setQueryTime?.Start();
        pathQueryTime?.Start();
        var pathQueries = createPathQueries(names);
        PathPredicateCount = pathQueries.Count;
        PathWildcardGroupCount = pathQueries.Count(query => query.Wildcard);
        add(owned.Filter(string.Join(" OR ", pathQueries.Select((query, index) =>
                $"Metadata.AudioFile {(query.Wildcard ? "LIKE[c]" : "ENDSWITH[c]")} ${index}")),
                pathQueries.Select(query => (QueryArgument)query.Pattern).ToArray())
            .AsEnumerable()
            // '?' also admits non-digits. Keep the original suffix test before tracking
            // candidates, so grouping cannot invent a path or content owner.
            .Where(beatmap => names.Any(name => beatmap.Metadata.AudioFile.EndsWith(name, StringComparison.OrdinalIgnoreCase)))
            .Select(beatmap => beatmap.BeatmapSet!));
        pathQueryTime?.Stop();

        // Only records without an explicit OJN filename need the hash-to-file Path fallback.
        // Resolve those matches in managed code, never as an ANY parent Files query.
        legacyPathQueryTime?.Start();
        foreach (var beatmap in owned.Filter("NOT Metadata.AudioFile ENDSWITH[c] $0", ".ojn"))
        {
            var name = O2JamExternalChart.SourceFileName(beatmap);
            if (name != null && names.Any(candidate => name.EndsWith(candidate, StringComparison.OrdinalIgnoreCase)))
                Track(beatmap.BeatmapSet!);
        }
        legacyPathQueryTime?.Stop();

        // Hash collision checks do not need metadata or file traversal. Ownership is
        // checked on these few matches; path queries remain scoped by the native link.
        hashQueryTime?.Start();
        add(database.All<BeatmapSetInfo>().Filter(
                $"DeletePending == false AND ({string.Join(" OR ", setHashes.Select((_, index) => $"Hash ==[c] ${index}"))})",
                setHashes.Select(hash => (QueryArgument)hash).ToArray())
            .AsEnumerable().Where(set => set.Beatmaps.Any(beatmap => beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName)));
        hashQueryTime?.Stop();
        setQueryTime?.Stop();
        // Legacy and malformed hash casing must retain the original comparison semantics.
        legacyQueryTime?.Start();
        add(owned.Filter(string.Join(" OR ", hashes.Select((_, index) => $"Hash ==[c] ${index}")),
            hashes.Select(hash => (QueryArgument)hash).ToArray())
            .AsEnumerable().Select(beatmap => beatmap.BeatmapSet!));
        legacyQueryTime?.Stop();
    }

    public void Track(BeatmapSetInfo set) => candidates[set.ID] = set;

    private static IReadOnlyList<(string Pattern, bool Wildcard)> createPathQueries(string[] names)
    {
        var remaining = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
        var queries = new List<(string, bool)>();
        // O2MA numeric filenames often fill a decimal group in one import batch. Only
        // replace a complete ten-digit group; arbitrary names retain literal suffixes.
        var groups = names.Where(name => name.Length > 8 && name.StartsWith("o2ma", StringComparison.OrdinalIgnoreCase)
            && name.EndsWith(".ojn", StringComparison.OrdinalIgnoreCase)
            && name.Skip(4).Take(name.Length - 8).All(character => character is >= '0' and <= '9'))
            .GroupBy(name => name[..^5], StringComparer.OrdinalIgnoreCase);
        foreach (var group in groups.Where(group => group.Count() == 10))
        {
            queries.Add(($"*{group.Key}?.ojn", true));
            foreach (var name in group)
                remaining.Remove(name);
        }
        queries.AddRange(remaining.Select(name => (name, false)));
        return queries;
    }

    private void add(IEnumerable<BeatmapSetInfo> sets)
    {
        foreach (var set in sets)
            Track(set);
    }

    public BeatmapSetInfo? FindPath(string path)
    {
        return unique(candidates.Values.Where(set => !set.DeletePending && O2JamLibraryWriter.containsSourceChart(set, path)));
    }

    public BeatmapSetInfo? FindSource(O2JamLibraryWriteRequest request)
    {
        var known = request.KnownSourceSetId is Guid id ? database.Find<BeatmapSetInfo>(id) : null;
        var path = FindPath(request.Plan.SourcePath);
        if (known is not { DeletePending: false })
            return path;
        // A scan ID is a hint. A new path owner may have appeared since that scan.
        if (path != null && path.ID != known.ID)
            throw new InvalidDataException("The destination path is registered to another OJN set.");
        return known;
    }

    public BeatmapSetInfo? FindContent(string hash)
    {
        // Native file backlinks already identify the owners of these exact bytes. Legacy
        // records without a source usage can still be found by their stored beatmap hash.
        var file = database.Find<RealmFile>(hash.ToLowerInvariant());
        var owners = file?.Usages.AsEnumerable().Select(usage => usage.Parent).OfType<BeatmapSetInfo>() ?? [];
        // Backlinks also expose files assigned to new sets earlier in this transaction.
        add(owners);
        return unique(candidates.Values
            .Where(set => !set.DeletePending && set.Beatmaps.Any(beatmap => beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName)
                         && O2JamLibraryWriter.containsSourceContent(set, hash)));
    }

    public bool ContainsSetHash(string hash) => candidates.Values.Any(set => !set.DeletePending
        && set.Beatmaps.Any(beatmap => beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName)
        && string.Equals(set.Hash, hash, StringComparison.OrdinalIgnoreCase));

    private static BeatmapSetInfo? unique(IEnumerable<BeatmapSetInfo> candidates)
    {
        var matches = candidates.Take(2).ToArray();
        if (matches.Length > 1)
            throw new InvalidDataException("Multiple stored sets claim the same OJN path or content; automatic merging is unavailable.");
        return matches.SingleOrDefault();
    }
}
