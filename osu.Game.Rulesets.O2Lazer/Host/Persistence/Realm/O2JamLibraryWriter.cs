using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Models;
using osu.Game.Rulesets;
using osu.Game.Rulesets.O2Lazer.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Difficulty;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Localisation;
using Realms;

namespace osu.Game.Rulesets.O2Lazer.Import;

internal enum O2JamLibraryWriteStage
{
    FilesReserved,
    RequestWritten,
    BeforeCommit,
}

/// <summary>
/// Reserves native files before publishing models so a failed transaction remains recoverable by native cleanup.
/// </summary>
public sealed class O2JamLibraryWriter : IO2JamLibraryWriter
{
    internal const string MetadataMarker = "o2lazer-clean:2";
    private const string encoding_marker_prefix = "o2lazer-encoding:";
    internal const string EncodingMarker = encoding_marker_prefix + "2";
    private const string source_length_prefix = "o2lazer-source-size:";

    private readonly RealmAccess realm;
    private readonly RealmFileStore files;
    private readonly Action<O2JamLibraryWriteStage, int>? checkpoint;
    private readonly O2JamLibraryNotificationQueue<Guid, BeatmapInfo> notifications = new();

    public int PendingNotifications => notifications.Count;

    private Action<BeatmapInfo>[] recipients => BeatmapUpdated?.GetInvocationList().Cast<Action<BeatmapInfo>>().ToArray() ?? [];

    public void RetryNotifications() => notifications.Retry(recipients);

    // Detached snapshots are published after committing, so consumers can invalidate native
    // working/difficulty caches without coupling database writes to UI services or Realm threads.
    public event Action<BeatmapInfo>? BeatmapUpdated;

    public O2JamLibraryWriter(RealmAccess realm, Storage storage)
        : this(realm, storage, null)
    {
    }

    internal O2JamLibraryWriter(RealmAccess realm, Storage storage, Action<O2JamLibraryWriteStage, int>? checkpoint)
    {
        this.realm = realm;
        files = new RealmFileStore(realm, storage);
        this.checkpoint = checkpoint;
    }

    public O2JamLibraryWriteResult Write(O2JamImportPlan plan) =>
        WriteBatch([new O2JamLibraryWriteRequest(plan)])[0];

    public IReadOnlyList<O2JamLibraryWriteResult> WriteBatch(IReadOnlyList<O2JamLibraryWriteRequest> requests, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requests.Count == 0)
            return [];

        // The reservation and model transactions must consume the same bytes even if a
        // caller retains mutable arrays from its plan. Only this bounded batch is copied.
        requests = requests.Select(request => request with
        {
            Plan = request.Plan with
            {
                SourceData = request.Plan.SourceData.ToArray(),
                Background = request.Plan.Background.ToArray(),
                Charts = request.Plan.Charts.ToArray(),
                Slots = request.Plan.Slots.ToArray(),
            },
        }).ToArray();
        var results = Enumerable.Repeat(O2JamLibraryWriteResult.RulesetUnavailable, requests.Count).ToArray();
        var updatedBeatmaps = new List<BeatmapInfo>();

        var sourceGuards = new List<FileStream>();
        try
        {
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceGuards.Add(O2JamSourceSnapshot.AcquireGuard(request.Plan));
            }

            var requiredHashes = requests.SelectMany(request => fileHashes(request.Plan)).Distinct(StringComparer.Ordinal).ToArray();
            var reserved = realm.Write(database =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (database.Find<RulesetInfo>(O2LazerIdentity.ShortName)?.Available != true)
                    return false;
                // A committed zero-reference row makes a later failed disk write discoverable
                // by native startup cleanup. No beatmap or score changes belong in this phase.
                foreach (var hash in requiredHashes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    if (database.Find<RealmFile>(hash) == null)
                        database.Add(new RealmFile { Hash = hash });
                }
                return true;
            });
            if (!reserved)
                return results;

            checkpoint?.Invoke(O2JamLibraryWriteStage.FilesReserved, -1);
            cancellationToken.ThrowIfCancellationRequested();

            realm.Write(database =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                var ruleset = database.Find<RulesetInfo>(O2LazerIdentity.ShortName);
                if (ruleset?.Available != true)
                    return;

                // Cleanup may run between the two transactions. Never fall back to creating
                // an unreserved row while writing bytes inside the model transaction.
                if (requiredHashes.Any(hash => database.Find<RealmFile>(hash) == null))
                    throw new IOException("An OJN file reservation was removed before its model transaction; prepare and retry the batch.");
                var writeIndex = new O2JamLibraryWriteIndex(database);
                for (var index = 0; index < requests.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results[index] = write(database, ruleset, requests[index], updatedBeatmaps, writeIndex, sourceGuards);
                    checkpoint?.Invoke(O2JamLibraryWriteStage.RequestWritten, index);
                }
                checkpoint?.Invoke(O2JamLibraryWriteStage.BeforeCommit, -1);
                cancellationToken.ThrowIfCancellationRequested();
            });
        }
        finally
        {
            foreach (var guard in sourceGuards)
                guard.Dispose();
        }

        foreach (var beatmap in updatedBeatmaps.GroupBy(beatmap => beatmap.ID).Select(group => group.Last()))
            notifications.Publish(beatmap.ID, beatmap, recipients);

        return results;
    }

    private static IEnumerable<string> fileHashes(O2JamImportPlan plan)
    {
        yield return plan.SourceHash.ToLowerInvariant();
        if (plan.Background.Length > 0)
        {
            using var stream = new MemoryStream(plan.Background, writable: false);
            yield return stream.ComputeSHA2Hash();
        }
    }

    public IReadOnlyDictionary<string, O2JamImportedSource> GetImportedSources() => realm.Run(database =>
    {
        var sources = new Dictionary<string, O2JamImportedSource>(StringComparer.OrdinalIgnoreCase);
        var beatmaps = database.All<BeatmapInfo>()
                               .Filter($@"{nameof(BeatmapInfo.BeatmapSet)}.{nameof(BeatmapSetInfo.DeletePending)} == false")
                               .Filter($@"{nameof(BeatmapInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $0", O2LazerIdentity.ShortName)
                               .AsEnumerable()
                               .Where(beatmap => beatmap.BeatmapSet != null)
                               .GroupBy(beatmap => beatmap.BeatmapSet!.ID);

        // Querying O2Lazer difficulties directly keeps unrelated osu! beatmap sets out of the
        // refresh index, which matters when the native library is much larger than the OJN folder.
        foreach (var setBeatmaps in beatmaps)
        {
            var ownedBeatmaps = setBeatmaps.ToArray();
            var sourceBeatmap = ownedBeatmaps.FirstOrDefault(candidate => tryGetSourcePath(candidate, out _));
            if (sourceBeatmap == null || !tryGetSourcePath(sourceBeatmap, out var sourcePath))
                continue;

            var sourceHash = findSourceHash(sourceBeatmap.BeatmapSet!, Path.GetFileName(sourcePath));
            var metadata = ownedBeatmaps.Select(candidate =>
                O2JamImportMetadata.Read(candidate.Metadata.Tags, out var projection) == O2JamImportMetadataStatus.Valid
                    ? projection : null).ToArray();
            var sourceLength = parseSourceLength(sourceBeatmap.Metadata.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries));
            var validCharts = sourceHash != null && isOwnedByO2Lazer(sourceBeatmap.BeatmapSet!)
                              && metadata.All(projection => projection != null)
                              && metadata.Select(projection => projection!.Difficulty).Distinct().Count() == ownedBeatmaps.Length
                              && ownedBeatmaps.Select((candidate, index) => string.Equals(candidate.Hash,
                                  O2JamBeatmapIdentity.FromSource(sourceHash, metadata[index]!.Difficulty), StringComparison.OrdinalIgnoreCase)).All(value => value);
            var hasManiaCache = ownedBeatmaps.All(candidate => O2JamStarRatingMetadata.HasCurrentManiaVersion(candidate.Metadata.Tags)
                                                             && O2JamStarRatingMetadata.ReadMania(candidate).HasValue
                                                             && O2JamStarRatingMetadata.ReadManiaMaxCombo(candidate.Metadata.Tags).HasValue);
            var current = validCharts && sourceLength != null && hasManiaCache
                          && string.Equals(sourceBeatmap.BeatmapSet!.Hash, O2JamBeatmapIdentity.SetFromMd5Hashes(ownedBeatmaps.Select(candidate => candidate.MD5Hash)), StringComparison.Ordinal)
                          && metadata.All(projection => projection!.ProjectionVersion == O2JamImportMetadata.ProjectionVersion)
                          && ownedBeatmaps.All(candidate => !candidate.Metadata.Tags.Contains(O2JamStarRatingMetadata.O2JamTagPrefix, StringComparison.Ordinal))
                          && ownedBeatmaps.All(candidate => parseSourceLength(candidate.Metadata.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)) == sourceLength
                                                           && candidate.LastLocalUpdate == sourceBeatmap.LastLocalUpdate
                                                           && candidate.Metadata.Source == sourceBeatmap.Metadata.Source
                                                           && candidate.Metadata.AudioFile == sourceBeatmap.Metadata.AudioFile);
            var encodingCurrent = ownedBeatmaps.All(candidate => candidate.Metadata.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                                                                           .Count(tag => tag.StartsWith(encoding_marker_prefix, StringComparison.Ordinal)) == 1
                                                                 && candidate.Metadata.Tags.Split(' ', StringSplitOptions.RemoveEmptyEntries).Contains(EncodingMarker))
                                  && metadata.All(projection => projection != null && projection.EncodingFallback == metadata[0]!.EncodingFallback);
            var maniaCache = validCharts && hasManiaCache ? ownedBeatmaps.Select((candidate, index) => new O2JamImportDifficultyCache(
                metadata[index]!.Difficulty, candidate.StarRating, O2JamStarRatingMetadata.ReadManiaMaxCombo(candidate.Metadata.Tags)!.Value, O2JamManiaStarRating.CacheVersion)).ToArray() : null;
            var source = new O2JamImportedSource(
                setBeatmaps.Key, sourceBeatmap.LastLocalUpdate, sourceLength, current, encodingCurrent, sourceHash,
                encodingCurrent ? metadata[0]!.EncodingFallback : null, maniaCache,
                validCharts ? ownedBeatmaps.Select((candidate, index) => new O2JamStoredDifficultyIdentity(metadata[index]!.Difficulty, candidate.MD5Hash)).ToArray() : null);
            if (!sources.TryAdd(sourcePath, source))
                throw new InvalidDataException("Multiple stored sets claim the same OJN path; automatic selection is unavailable.");
        }

        return sources;
    });

    public int MarkDeleted(IEnumerable<Guid> setIds)
    {
        var ids = setIds.Distinct().ToArray();
        return realm.Write(database =>
        {
            var count = 0;
            foreach (var id in ids)
            {
                var set = database.Find<BeatmapSetInfo>(id);
                if (set is not { DeletePending: false } || !isOwnedByO2Lazer(set))
                    continue;
                // The scan's set ID can survive an in-place move within this refresh.
                // Recheck the committed location rather than deleting from its stale index.
                if (!tryGetSourcePath(set, out var path) || !O2JamSourcePresence.IsDefinitelyMissing(path))
                    continue;

                set.DeletePending = true;
                count++;
            }

            return count;
        });
    }

    public int DeleteAll() => realm.Write(database =>
    {
        var sets = database.All<BeatmapSetInfo>()
                           .Where(set => !set.DeletePending)
                           .AsEnumerable()
                           .Where(isOwnedByO2Lazer)
                           .ToArray();

        foreach (var set in sets)
            set.DeletePending = true;

        return sets.Length;
    });

    public int MarkMissingSources()
    {
        var sources = realm.Run(database => database.All<BeatmapSetInfo>()
                                                    .Where(set => !set.DeletePending)
                                                    .AsEnumerable()
                                                    .Where(isOwnedByO2Lazer)
                                                    .Select(set => tryGetSourcePath(set, out var sourcePath)
                                                        ? (set.ID, SourcePath: sourcePath)
                                                        : (Guid.Empty, SourcePath: string.Empty))
                                                    .Where(source => source.Item1 != Guid.Empty)
                                                    .ToArray());
        var missing = sources.Where(source => O2JamSourcePresence.IsDefinitelyMissing(source.SourcePath))
                             .Select(source => source.Item1);
        return MarkDeleted(missing);
    }

    private O2JamLibraryWriteResult write(Realm database, RulesetInfo ruleset, O2JamLibraryWriteRequest request,
                                        List<BeatmapInfo> updatedBeatmaps, O2JamLibraryWriteIndex index, List<FileStream> sourceGuards)
    {
        var plan = request.Plan;

        O2JamLibraryWriteResult updateMetadata(BeatmapSetInfo set)
        {
            var charts = validateMetadata(set, plan);
            var changed = synchroniseFiles(database, set, plan);
            changed |= refreshMetadata(set, plan, charts, database);
            index.Update(set);
            if (!changed)
                return O2JamLibraryWriteResult.AlreadyPresent;

            updatedBeatmaps.AddRange(set.Beatmaps.Where(beatmap => beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName)
                                       .Select(beatmap => beatmap.Detach()));
            return O2JamLibraryWriteResult.Updated;
        }

        var sourceSet = index.FindPath(plan.SourcePath);

        if (request.KnownSourceSetId != null)
        {
            var knownSet = database.Find<BeatmapSetInfo>(request.KnownSourceSetId.Value);
            if (knownSet is { DeletePending: false })
            {
                if (sourceSet != null && sourceSet.ID != knownSet.ID)
                    throw new InvalidDataException("The destination path is registered to another OJN set.");
                sourceSet = knownSet;
            }
        }

        // Older releases can produce a different set hash when an OJN difficulty contains
        // blocks but no playable notes. The unchanged source file is the stronger identity;
        // migrate its metadata in place so Beatmap IDs and attached scores remain intact.
        if (sourceSet != null)
            ensureSupportedProjection(sourceSet);

        var matchingSet = index.FindContent(plan.SourceHash);
        if (sourceSet != null && !containsSourceChart(sourceSet, plan.SourcePath) && !containsSourceContent(sourceSet, plan))
            throw new InvalidDataException("The source index no longer identifies this OJN.");

        if (matchingSet != null)
        {
            ensureSupportedProjection(matchingSet);
            var result = O2JamLibraryWriteResult.AlreadyPresent;
            var hasPreviousPath = tryGetSourcePath(matchingSet, out var previousPath);
            if (containsSourceChart(matchingSet, plan.SourcePath))
                result = updateMetadata(matchingSet);
            else
            {
                if (!hasPreviousPath)
                    throw new IOException("The previous source location is unavailable; it cannot safely be replaced by this copy.");
                if (O2JamSourcePresence.IsDefinitelyMissing(previousPath))
                    result = updateMetadata(matchingSet);
                else
                {
                    var guard = new FileStream(previousPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                    sourceGuards.Add(guard);
                    var previousHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(guard));
                    if (!string.Equals(previousHash, plan.SourceHash, StringComparison.OrdinalIgnoreCase))
                        result = updateMetadata(matchingSet);
                }
            }

            if (sourceSet != null && sourceSet.ID != matchingSet.ID)
            {
                sourceSet.DeletePending = true;
                index.Update(sourceSet);
            }

            return result;
        }

        if (index.ContainsSetHash(plan.SetHash))
            throw new InvalidDataException("The set hash matches without verifiable source content.");

        var replacedSet = sourceSet;

        var beatmapSet = new BeatmapSetInfo
        {
            OnlineID = -1,
            DateAdded = DateTimeOffset.UtcNow,
            Hash = plan.SetHash,
        };
        synchroniseFiles(database, beatmapSet, plan);

        foreach (var chart in plan.Charts)
            addDifficulty(beatmapSet, ruleset, plan, chart);

        database.Add(beatmapSet);
        index.Update(beatmapSet);
        if (replacedSet != null)
        {
            replacedSet.DeletePending = true;
            index.Update(replacedSet);
        }

        return O2JamLibraryWriteResult.Imported;
    }

    internal static bool containsSourceChart(BeatmapSetInfo set, string sourcePath)
    {
        string expected;

        try
        {
            expected = Path.GetFullPath(sourcePath);
        }
        catch (Exception)
        {
            return false;
        }

        foreach (var beatmap in set.Beatmaps)
        {
            var sourceFileName = O2JamExternalChart.SourceFileName(beatmap);
            if (!string.Equals(beatmap.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal)
                || string.IsNullOrWhiteSpace(beatmap.Metadata.Source)
                || string.IsNullOrWhiteSpace(sourceFileName))
                continue;

            try
            {
                var candidate = Path.GetFullPath(Path.Combine(beatmap.Metadata.Source, sourceFileName));
                if (string.Equals(candidate, expected, StringComparison.OrdinalIgnoreCase))
                    return true;
            }
            catch (Exception)
            {
                // A malformed legacy source path must not prevent unrelated charts from importing.
            }
        }

        return false;
    }

    internal static bool refreshMetadata(BeatmapSetInfo set, O2JamImportPlan plan)
        => refreshMetadata(set, plan, validateMetadata(set, plan), set.Realm);

    private static bool refreshMetadata(BeatmapSetInfo set, O2JamImportPlan plan, (BeatmapInfo Beatmap, O2JamImportSlot Slot)[] charts, Realm? database)
    {
        // Snapshot every association and collection owner before changing any shared legacy
        // hash. A later chart must not see evidence altered by an earlier chart's migration.
        var collectionOwners = database != null && charts.Any(entry => entry.Slot.IsPlayable && entry.Beatmap.MD5Hash != entry.Slot.Md5Hash)
            ? database.All<BeatmapInfo>().AsEnumerable().GroupBy(candidate => candidate.MD5Hash, StringComparer.OrdinalIgnoreCase)
                      .ToDictionary(group => group.Key, group => group.Count(), StringComparer.OrdinalIgnoreCase)
            : null;
        var snapshots = charts.Select(entry => (
            entry.Beatmap, entry.Slot,
            Scores: entry.Beatmap.IsManaged ? entry.Beatmap.Scores.ToArray() : [],
            OldMd5: entry.Beatmap.MD5Hash,
            TransferCollections: collectionOwners != null && !string.IsNullOrEmpty(entry.Beatmap.MD5Hash)
                && collectionOwners.GetValueOrDefault(entry.Beatmap.MD5Hash) == 1)).ToArray();
        var ruleset = set.Beatmaps[0].Ruleset;
        var changed = update(set.Hash, plan.SetHash, value => set.Hash = value);
        foreach (var file in set.Files.Where(file => string.Equals(file.File.Hash, plan.SourceHash, StringComparison.OrdinalIgnoreCase)))
            changed |= update(file.Filename, plan.FileName, value => file.Filename = value);

        foreach (var (beatmap, slot, scores, oldMd5, transferCollections) in snapshots)
        {
            var chart = plan.Charts.SingleOrDefault(candidate => candidate.Difficulty == slot.Difficulty);
            if (chart == null)
            {
                if (database == null)
                    throw new InvalidOperationException("Removing a historical difficulty requires the native model transaction.");
                foreach (var score in scores)
                    score.BeatmapInfo = null;
                set.Beatmaps.Remove(beatmap);
                database.Remove(beatmap.Metadata);
                database.Remove(beatmap);
                changed = true;
                continue;
            }

            var hash = O2JamBeatmapIdentity.FromSource(plan.SourceHash, chart.Difficulty);
            changed |= update(beatmap.Hash, hash, value => beatmap.Hash = value);
            foreach (var score in scores)
                changed |= update(score.BeatmapHash, hash, value => score.BeatmapHash = value);

            if (beatmap.MD5Hash != chart.Md5Hash)
            {
                beatmap.MD5Hash = chart.Md5Hash;
                if (transferCollections)
                    beatmap.TransferCollectionReferences(database!, oldMd5);
                else if (database != null && database.All<BeatmapCollection>().AsEnumerable()
                                                   .Any(collection => collection.BeatmapMD5Hashes.Contains(oldMd5)))
                    Logger.Log($"O2Lazer retained ambiguous collection references while migrating difficulty {beatmap.ID}.");
                changed = true;
            }
            changed |= applyProjection(beatmap, plan, chart);
        }

        foreach (var chart in plan.Charts.Where(chart => !charts.Any(entry => entry.Slot.Difficulty == chart.Difficulty)))
        {
            addDifficulty(set, ruleset, plan, chart);
            changed = true;
        }
        return changed;
    }

    private static void addDifficulty(BeatmapSetInfo set, RulesetInfo ruleset, O2JamImportPlan plan, O2JamImportChart chart)
    {
        var beatmap = new BeatmapInfo(ruleset)
        {
            Hash = O2JamBeatmapIdentity.FromSource(plan.SourceHash, chart.Difficulty),
            MD5Hash = chart.Md5Hash,
            BeatmapSet = set,
        };
        applyProjection(beatmap, plan, chart);
        set.Beatmaps.Add(beatmap);
    }

    private static (BeatmapInfo Beatmap, O2JamImportSlot Slot)[] validateMetadata(BeatmapSetInfo set, O2JamImportPlan plan)
    {
        ensureSupportedProjection(set);

        var slots = plan.Slots.Count > 0 ? plan.Slots : plan.Charts.Select(chart => new O2JamImportSlot(chart.Difficulty, chart.Md5Hash, true)).ToArray();
        if (slots.Select(slot => slot.Difficulty).Distinct().Count() != slots.Count
            || slots.Any(slot => slot.IsPlayable != plan.Charts.Any(chart => chart.Difficulty == slot.Difficulty)
                                 || slot.IsPlayable && plan.Charts.Single(chart => chart.Difficulty == slot.Difficulty).Md5Hash != slot.Md5Hash))
            throw new InvalidDataException("The prepared OJN slot inventory conflicts with its playable projection.");
        var result = new List<(BeatmapInfo, O2JamImportSlot)>();
        foreach (var beatmap in set.Beatmaps)
        {
            var status = O2JamImportMetadata.Read(beatmap.Metadata.Tags, out var projection);
            if (status is O2JamImportMetadataStatus.Invalid or O2JamImportMetadataStatus.Unsupported)
                throw new InvalidDataException("The stored O2Lazer projection is conflicting or newer than this ruleset.");

            var evidence = slots.Where(slot => string.Equals(beatmap.Hash,
                                             O2JamBeatmapIdentity.FromSource(plan.SourceHash, slot.Difficulty), StringComparison.OrdinalIgnoreCase)
                                             || string.Equals(beatmap.MD5Hash, slot.Md5Hash, StringComparison.OrdinalIgnoreCase)).ToArray();
            if (evidence.Length > 1 || projection != null && evidence.Any(slot => slot.Difficulty != projection.Difficulty))
                throw new InvalidDataException("The stored OJN hash and difficulty evidence conflict.");
            var matches = projection != null ? slots.Where(slot => slot.Difficulty == projection.Difficulty).ToArray() : evidence;
            if (matches.Length == 0 && status == O2JamImportMetadataStatus.Legacy)
            {
                // Keep the known legacy storage format as a bounded migration fallback.
                // Arbitrary display-name prefixes cannot identify a difficulty.
                matches = slots.Where(chart =>
                    beatmap.DifficultyName.StartsWith(chart.Difficulty + " Lv.", StringComparison.OrdinalIgnoreCase)
                    && ushort.TryParse(beatmap.DifficultyName.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out _)).ToArray();
            }

            if (matches.Length != 1 || result.Any(candidate => candidate.Item2.Difficulty == matches[0].Difficulty))
                throw new InvalidDataException("The stored OJN difficulty cannot be uniquely matched.");
            if (projection != null && !string.Equals(beatmap.Hash,
                    O2JamBeatmapIdentity.FromSource(plan.SourceHash, projection.Difficulty), StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The stored chart slot conflicts with its content identity.");
            result.Add((beatmap, matches[0]));
        }
        return result.ToArray();
    }

    private static void ensureSupportedProjection(BeatmapSetInfo set)
    {
        if (!isOwnedByO2Lazer(set))
            throw new InvalidDataException("The source set is not exclusively owned by O2Lazer.");
        foreach (var beatmap in set.Beatmaps)
        {
            if (O2JamImportMetadata.Read(beatmap.Metadata.Tags, out _) is O2JamImportMetadataStatus.Invalid or O2JamImportMetadataStatus.Unsupported)
                throw new InvalidDataException("The stored O2Lazer projection is conflicting or newer than this ruleset.");
        }
    }

    private static bool applyProjection(BeatmapInfo beatmap, O2JamImportPlan plan, O2JamImportChart chart)
    {
        var metadata = beatmap.Metadata;
        var changed = update(beatmap.DifficultyName, O2LazerStrings.DifficultyName(chart.Difficulty, chart.Level).ToString(), value => beatmap.DifficultyName = value);
        changed |= update(metadata.Title, plan.Title, value => metadata.Title = value);
        changed |= update(metadata.Artist, plan.Artist, value => metadata.Artist = value);
        var author = string.IsNullOrWhiteSpace(plan.Author) ? "O2Jam" : plan.Author;
        if (metadata.Author.Username != author)
        {
            // Native authors are embedded in managed metadata. Clone via the native API as
            // detached callers can still share a reference; do not mutate that other snapshot.
            var replacement = metadata.Author.DeepClone();
            replacement.Username = author;
            metadata.Author = replacement;
            changed = true;
        }
        changed |= update(metadata.Source, plan.SourceDirectory, value => metadata.Source = value);
        changed |= update(metadata.AudioFile, plan.FileName, value => metadata.AudioFile = value);
        changed |= update(metadata.BackgroundFile, backgroundFileName(plan), value => metadata.BackgroundFile = value);
        changed |= update(metadata.PreviewTime, 0, value => metadata.PreviewTime = value);
        var difficulty = new BeatmapDifficulty
        {
            CircleSize = Beatmaps.O2JamBeatmap.ColumnCount,
            OverallDifficulty = Math.Clamp((int)chart.Level, 0, 10),
        };
        if (beatmap.Difficulty.CircleSize != difficulty.CircleSize
            || beatmap.Difficulty.OverallDifficulty != difficulty.OverallDifficulty
            || beatmap.Difficulty.DrainRate != difficulty.DrainRate
            || beatmap.Difficulty.ApproachRate != difficulty.ApproachRate
            || beatmap.Difficulty.SliderMultiplier != difficulty.SliderMultiplier
            || beatmap.Difficulty.SliderTickRate != difficulty.SliderTickRate)
        {
            difficulty.CopyTo(beatmap.Difficulty);
            changed = true;
        }
        changed |= update(beatmap.BPM, plan.InitialBpm, value => beatmap.BPM = value);
        changed |= update(beatmap.Length, chart.Length, value => beatmap.Length = value);
        changed |= update(beatmap.TotalObjectCount, chart.TotalObjectCount, value => beatmap.TotalObjectCount = value);
        changed |= update(beatmap.EndTimeObjectCount, chart.HoldObjectCount, value => beatmap.EndTimeObjectCount = value);
        changed |= update(beatmap.StarRating, chart.ManiaStarRating, value => beatmap.StarRating = value);
        changed |= update(beatmap.LastLocalUpdate, plan.SourceTimestamp, value => beatmap.LastLocalUpdate = value);

        var songTag = string.Create(CultureInfo.InvariantCulture, $"o2ma{plan.SongId}");
        var tags = metadata.Tags.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
                           .Where(tag => !O2JamImportMetadata.OwnsToken(tag)
                                         && !tag.StartsWith("o2lazer-clean:", StringComparison.Ordinal)
                                         && !tag.StartsWith(encoding_marker_prefix, StringComparison.Ordinal)
                                         && !tag.StartsWith(source_length_prefix, StringComparison.Ordinal)
                                         && !tag.StartsWith(O2JamStarRatingMetadata.O2JamTagPrefix, StringComparison.Ordinal)
                                         && !tag.StartsWith(O2JamStarRatingMetadata.ManiaVersionPrefix, StringComparison.Ordinal)
                                         && !tag.StartsWith(O2JamStarRatingMetadata.ManiaMaxComboPrefix, StringComparison.Ordinal)
                                         && tag != "o2jam" && tag != songTag);
        var projectedTags = string.Join(' ', tags.Append(string.Create(CultureInfo.InvariantCulture,
            $"o2jam {songTag} {MetadataMarker} {EncodingMarker} {source_length_prefix}{plan.SourceData.LongLength} {O2JamImportMetadata.Create(plan, chart)} {O2JamStarRatingMetadata.ManiaVersionTag} {O2JamStarRatingMetadata.CreateManiaMaxComboTag(chart.ManiaMaxCombo)}")));
        changed |= update(metadata.Tags, projectedTags, value => metadata.Tags = value);
        return changed;
    }

    private bool synchroniseFiles(Realm database, BeatmapSetInfo set, O2JamImportPlan plan)
    {
        var changed = false;
        using var sourceStream = new MemoryStream(plan.SourceData, writable: false);
        var source = files.Add(sourceStream, database, preferHardLinks: false);
        var usage = set.Files.FirstOrDefault(file => file.File.Hash == source.Hash);
        if (usage == null)
        {
            set.Files.Add(new RealmNamedFileUsage(source, plan.FileName));
            changed = true;
        }
        else
            changed |= update(usage.Filename, plan.FileName, value => usage.Filename = value);

        var backgroundName = backgroundFileName(plan);
        RealmFile? background = null;
        if (plan.Background.Length > 0)
        {
            using var stream = new MemoryStream(plan.Background, writable: false);
            background = files.Add(stream, database, preferHardLinks: false);
        }
        foreach (var old in set.Files.Where(file => file.Filename is "o2jam-background.png" or "o2jam-background.jpg" or "o2jam-background.bmp").ToArray())
        {
            if (background != null && old.Filename == backgroundName && old.File.Hash == background.Hash)
                continue;
            set.Files.Remove(old);
            changed = true;
        }
        if (background != null && !set.Files.Any(file => file.Filename == backgroundName && file.File.Hash == background.Hash))
        {
            set.Files.Add(new RealmNamedFileUsage(background, backgroundName));
            changed = true;
        }
        return changed;
    }

    private static string backgroundFileName(O2JamImportPlan plan) =>
        plan.Background.Length == 0 ? string.Empty : $"o2jam-background{detectImageExtension(plan.Background)}";

    private static bool update<T>(T current, T projected, Action<T> assign)
    {
        if (EqualityComparer<T>.Default.Equals(current, projected))
            return false;
        assign(projected);
        return true;
    }

    internal static bool isOwnedByO2Lazer(BeatmapSetInfo set) =>
        set.Beatmaps.Count > 0 && set.Beatmaps.All(beatmap => string.Equals(
            beatmap.Ruleset.ShortName,
            O2LazerIdentity.ShortName,
            StringComparison.Ordinal));

    internal static bool containsSourceContent(BeatmapSetInfo set, O2JamImportPlan plan)
    {
        var sourceHashes = set.Files.Where(file => file.Filename.EndsWith(".ojn", StringComparison.OrdinalIgnoreCase))
                              .Select(file => file.File.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return sourceHashes.Length == 1
            ? string.Equals(sourceHashes[0], plan.SourceHash, StringComparison.OrdinalIgnoreCase)
            : sourceHashes.Length == 0 && set.Beatmaps.Any(beatmap => string.Equals(beatmap.Hash, plan.SourceHash, StringComparison.OrdinalIgnoreCase));
    }

    private static bool tryGetSourcePath(BeatmapSetInfo set, out string sourcePath)
    {
        foreach (var beatmap in set.Beatmaps)
        {
            if (tryGetSourcePath(beatmap, out sourcePath))
                return true;
        }

        sourcePath = string.Empty;
        return false;
    }

    internal static bool tryGetSourcePath(BeatmapInfo beatmap, out string sourcePath)
    {
        if (string.Equals(beatmap.Ruleset.ShortName, O2LazerIdentity.ShortName, StringComparison.Ordinal))
        {
            var sourceFileName = O2JamExternalChart.SourceFileName(beatmap);
            if (!string.IsNullOrWhiteSpace(beatmap.Metadata.Source) && !string.IsNullOrWhiteSpace(sourceFileName))
            {
                try
                {
                    sourcePath = Path.GetFullPath(Path.Combine(beatmap.Metadata.Source, sourceFileName));
                    return true;
                }
                catch (Exception)
                {
                    // Malformed paths remain untouched because their intended source cannot be proven.
                }
            }
        }

        sourcePath = string.Empty;
        return false;
    }

    private static long? parseSourceLength(IEnumerable<string> tags)
    {
        var tokens = tags.Where(tag => tag.StartsWith(source_length_prefix, StringComparison.Ordinal)).ToArray();
        return tokens.Length == 1
               && long.TryParse(tokens[0].AsSpan(source_length_prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture, out var length)
            ? length : null;
    }

    private static string? findSourceHash(BeatmapSetInfo set, string sourceFileName) =>
        set.Files.FirstOrDefault(file => string.Equals(file.Filename, sourceFileName, StringComparison.OrdinalIgnoreCase))?.File.Hash;

    private static string detectImageExtension(IReadOnlyList<byte> image)
    {
        if (image.Count >= 8
            && image[0] == 0x89 && image[1] == (byte)'P' && image[2] == (byte)'N' && image[3] == (byte)'G')
            return ".png";
        if (image.Count >= 3 && image[0] == 0xff && image[1] == 0xd8 && image[2] == 0xff)
            return ".jpg";
        if (image.Count >= 2 && image[0] == (byte)'B' && image[1] == (byte)'M')
            return ".bmp";

        return ".jpg";
    }
}
