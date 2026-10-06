using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Framework.Platform;
using osu.Game.Beatmaps;
using osu.Game.Collections;
using osu.Game.Database;
using osu.Game.Extensions;
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
    DifficultySourceWritten,
}

/// <summary>
/// Reserves native files before publishing models so a failed transaction remains recoverable by native cleanup.
/// </summary>
public sealed class O2JamLibraryWriter : IO2JamLibraryWriter, IO2JamLibraryDifficultyBatchStore
{
    internal const string MetadataMarker = "o2lazer-clean:2";
    private const string encoding_marker_prefix = "o2lazer-encoding:";
    internal const string EncodingMarker = encoding_marker_prefix + "2";
    private const string source_length_prefix = "o2lazer-source-size:";

    private readonly RealmAccess realm;
    private readonly RealmFileStore files;
    private readonly Action<O2JamLibraryWriteStage, int>? checkpoint;
    private readonly O2JamLibraryNotificationQueue<Guid, BeatmapInfo> notifications = new();
    private readonly HashSet<Guid> pendingFileInvalidations = [];
    private int completedBatches;
    private readonly bool observeNativeFileChecks;
    private bool warnedAboutFileObservation;

    public int PendingNotifications => notifications.Count;

    private Action<BeatmapInfo>[] recipients => BeatmapUpdated?.GetInvocationList().Cast<Action<BeatmapInfo>>().ToArray() ?? [];

    public void RetryNotifications() => notifications.Retry(recipients);

    IReadOnlyList<O2JamLibraryDifficultySource> IO2JamLibraryDifficultyStore.GetPendingDifficultySources(string path, CancellationToken cancellationToken)
    {
        var root = Path.GetFullPath(path);
        if (!Path.EndsInDirectorySeparator(root))
            root += Path.DirectorySeparatorChar;
        return readImportedSources(cancellationToken, null)
            .Where(entry => entry.Key.StartsWith(root, StringComparison.OrdinalIgnoreCase)
                            && entry.Value.Source.HasCurrentMetadata && entry.Value.Source.HasCurrentEncoding
                            && entry.Value.Source.DifficultyIdentities is { } identities
                            && identities.Count > (entry.Value.Source.ManiaCache?.Count ?? 0))
            .Select(entry => new O2JamLibraryDifficultySource(entry.Key, entry.Value.Source)).ToArray();
    }

    byte[] IO2JamLibraryDifficultyStore.ReadDifficultySource(O2JamLibraryDifficultySource source)
    {
        using var stream = files.Storage.GetStream(new RealmFile { Hash = source.Source.SourceHash!.ToLowerInvariant() }.GetStoragePath());
        using var data = new MemoryStream();
        stream.CopyTo(data);
        return data.ToArray();
    }

    O2JamLibraryDifficultySource IO2JamLibraryDifficultyStore.RefreshDifficultySource(O2JamLibraryDifficultySource source,
                                                                                    CancellationToken cancellationToken) => realm.Run(database =>
        refreshDifficultySource(database, source, cancellationToken));

    private static O2JamLibraryDifficultySource refreshDifficultySource(Realm database, O2JamLibraryDifficultySource source, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var set = findDifficultySet(database, source);
        var caches = source.Source.DifficultyIdentities!.Select(identity =>
            readDifficultyCache(findDifficultyBeatmap(set, source, identity.Difficulty), identity.Difficulty))
            .OfType<O2JamImportDifficultyCache>().ToArray();
        return source with { Source = source.Source with { ManiaCache = caches } };
    }

    IReadOnlyList<O2JamLibraryDifficultySourceCheck> IO2JamLibraryDifficultyBatchStore.RefreshDifficultySources(
        IReadOnlyList<O2JamLibraryDifficultySource> sources, CancellationToken cancellationToken) => realm.Run(database =>
    {
        if (sources.Count > O2JamLibraryDifficultyProcessor.BatchSize)
            throw new ArgumentOutOfRangeException(nameof(sources));
        var checks = new List<O2JamLibraryDifficultySourceCheck>();
        foreach (var source in sources)
        {
            cancellationToken.ThrowIfCancellationRequested();
            try { checks.Add(new O2JamLibraryDifficultySourceCheck(refreshDifficultySource(database, source, cancellationToken))); }
            catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
            {
                checks.Add(new O2JamLibraryDifficultySourceCheck(source, exception));
            }
        }
        return (IReadOnlyList<O2JamLibraryDifficultySourceCheck>)checks;
    });

    private static BeatmapSetInfo findDifficultySet(Realm database, O2JamLibraryDifficultySource source)
    {
        var set = database.Find<BeatmapSetInfo>(source.Source.SetId);
        // Results belong to committed content, never to a replacement at its former path.
        if (set is not { DeletePending: false } || !isOwnedByO2Lazer(set)
            || !string.Equals(findSourceHash(set, Path.GetFileName(source.Path)), source.Source.SourceHash, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException("The imported source changed before its difficulty cache could be committed.");
        return set;
    }

    private static BeatmapInfo findDifficultyBeatmap(BeatmapSetInfo set, O2JamLibraryDifficultySource source, O2Jam.Core.O2JamDifficulty difficulty)
    {
        var beatmap = set.Beatmaps.Single(candidate => candidate.Ruleset.ShortName == O2LazerIdentity.ShortName
            && string.Equals(candidate.Hash, O2JamBeatmapIdentity.FromSource(source.Source.SourceHash!, difficulty), StringComparison.OrdinalIgnoreCase));
        var identity = source.Source.DifficultyIdentities!.Single(candidate => candidate.Difficulty == difficulty);
        if (!string.Equals(beatmap.MD5Hash, identity.Md5Hash, StringComparison.OrdinalIgnoreCase)
            || O2JamImportMetadata.Read(beatmap.Metadata.Tags, out var projection) != O2JamImportMetadataStatus.Valid
            || projection!.ProjectionVersion != O2JamImportMetadata.ProjectionVersion || projection.Difficulty != difficulty)
            throw new InvalidDataException("The difficulty projection changed before its cache could be committed.");
        return beatmap;
    }

    private static O2JamImportDifficultyCache? readDifficultyCache(BeatmapInfo beatmap, O2Jam.Core.O2JamDifficulty difficulty) =>
        O2JamStarRatingMetadata.HasCurrentManiaVersion(beatmap.Metadata.Tags)
        && O2JamStarRatingMetadata.ReadMania(beatmap) is { } stars
        && O2JamStarRatingMetadata.ReadManiaMaxCombo(beatmap.Metadata.Tags) is { } maxCombo
            ? new O2JamImportDifficultyCache(difficulty, stars, maxCombo, O2JamManiaStarRating.CacheVersion)
            : null;

    void IO2JamLibraryDifficultyStore.WriteDifficulties(O2JamLibraryDifficultySource source, IReadOnlyList<O2JamImportDifficultyCache> difficulties,
                                                        CancellationToken cancellationToken)
    {
        var result = writeDifficultyBatch([new O2JamLibraryDifficultyCacheWrite(source, difficulties)], cancellationToken)[0];
        if (result.Failure != null)
            ExceptionDispatchInfo.Capture(result.Failure).Throw();
    }

    IReadOnlyList<O2JamLibraryDifficultyOutcome> IO2JamLibraryDifficultyBatchStore.WriteDifficultyBatch(
        IReadOnlyList<O2JamLibraryDifficultyCacheWrite> writes, CancellationToken cancellationToken) => writeDifficultyBatch(writes, cancellationToken);

    private IReadOnlyList<O2JamLibraryDifficultyOutcome> writeDifficultyBatch(IReadOnlyList<O2JamLibraryDifficultyCacheWrite> writes,
                                                                            CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (writes.Count > O2JamLibraryDifficultyProcessor.BatchSize)
            throw new ArgumentOutOfRangeException(nameof(writes));
        if (writes.Count == 0)
            return [];
        var updated = new List<BeatmapInfo>();
        var results = new O2JamLibraryDifficultyOutcome[writes.Count];
        realm.Write(database =>
        {
            for (var index = 0; index < writes.Count; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var write = writes[index];
                List<(BeatmapInfo Beatmap, O2JamImportDifficultyCache Cache)> changes;
                try { changes = validateDifficultyWrite(database, write, cancellationToken); }
                catch (Exception exception) when (exception is InvalidDataException or InvalidOperationException)
                {
                    results[index] = new O2JamLibraryDifficultyOutcome(write.Source, Failure: exception);
                    continue;
                }
                // Validate every slot of this source before changing any of them. An invalid
                // identity rejects only this source; mutation/commit failures roll back all.
                foreach (var (beatmap, cache) in changes)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    beatmap.StarRating = cache.StarRating;
                    beatmap.Metadata.Tags = O2JamStarRatingMetadata.WithManiaCache(beatmap.Metadata.Tags, cache.MaxCombo);
                    updated.Add(beatmap.Detach());
                }
                results[index] = new O2JamLibraryDifficultyOutcome(write.Source, changes.Count);
                checkpoint?.Invoke(O2JamLibraryWriteStage.DifficultySourceWritten, index);
            }
            cancellationToken.ThrowIfCancellationRequested();
        });
        foreach (var beatmap in updated)
            notifications.Publish(beatmap.ID, beatmap, recipients);
        return results;
    }

    private static List<(BeatmapInfo Beatmap, O2JamImportDifficultyCache Cache)> validateDifficultyWrite(
        Realm database, O2JamLibraryDifficultyCacheWrite write, CancellationToken cancellationToken)
    {
        var set = findDifficultySet(database, write.Source);
        if (write.Difficulties.Select(cache => cache.Difficulty).Distinct().Count() != write.Difficulties.Count)
            throw new InvalidDataException("The deferred mania cache has duplicate difficulty slots.");
        var changes = new List<(BeatmapInfo, O2JamImportDifficultyCache)>();
        foreach (var cache in write.Difficulties)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (cache.Version != O2JamManiaStarRating.CacheVersion || !double.IsFinite(cache.StarRating) || cache.StarRating < 0 || cache.MaxCombo < 0)
                throw new InvalidDataException("The deferred mania difficulty cache is invalid.");
            var beatmap = findDifficultyBeatmap(set, write.Source, cache.Difficulty);
            if (readDifficultyCache(beatmap, cache.Difficulty) == null)
                changes.Add((beatmap, cache));
        }
        return changes;
    }

    // Detached snapshots are published after committing, so consumers can invalidate native
    // working/difficulty caches without coupling database writes to UI services or Realm threads.
    public event Action<BeatmapInfo>? BeatmapUpdated;

    public O2JamLibraryWriter(RealmAccess realm, Storage storage)
        : this(realm, storage, null)
    {
    }

    internal O2JamLibraryWriter(RealmAccess realm, Storage storage, Action<O2JamLibraryWriteStage, int>? checkpoint,
                               bool observeNativeFileChecks = true)
    {
        this.realm = realm;
        files = new RealmFileStore(realm, storage);
        this.checkpoint = checkpoint;
        this.observeNativeFileChecks = observeNativeFileChecks;
    }

    public O2JamLibraryWriteResult Write(O2JamImportPlan plan) =>
        WriteBatch([new O2JamLibraryWriteRequest(plan)])[0];

    public IReadOnlyList<O2JamLibraryWriteResult> WriteBatch(IReadOnlyList<O2JamLibraryWriteRequest> requests, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (requests.Count == 0)
            return [];
        var totalTime = Stopwatch.StartNew();
        var sourceTime = new Stopwatch();
        var reserveTime = new Stopwatch();
        var verifyTime = new Stopwatch();
        var modelTime = new Stopwatch();
        var modelBodyTime = new Stopwatch();
        var lookupTime = new Stopwatch();
        var setQueryTime = new Stopwatch();
        var legacyQueryTime = new Stopwatch();
        var pathQueryTime = new Stopwatch();
        var legacyPathQueryTime = new Stopwatch();
        var hashQueryTime = new Stopwatch();
        var fileTime = new Stopwatch();
        var projectionTime = new Stopwatch();
        var nativeFileChecks = 0;
        var fallbackFileChecks = 0;
        var pathPredicates = 0;
        var pathWildcardGroups = 0;
        var canObserve = observeNativeFileChecks && O2JamFileVerificationPatch.IsInstalled;

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
            sourceTime.Start();
            foreach (var request in requests)
            {
                cancellationToken.ThrowIfCancellationRequested();
                sourceGuards.Add(O2JamSourceSnapshot.AcquireGuard(request.Plan));
            }
            sourceTime.Stop();

            var requiredHashes = requests.SelectMany(request => fileHashes(request.Plan)).Distinct(StringComparer.Ordinal).ToArray();
            reserveTime.Start();
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
            reserveTime.Stop();
            if (!reserved)
                return results;

            checkpoint?.Invoke(O2JamLibraryWriteStage.FilesReserved, -1);
            cancellationToken.ThrowIfCancellationRequested();
            var damagedFiles = new HashSet<string>(StringComparer.Ordinal);

            void rememberDamage(RealmFile file)
            {
                damagedFiles.Add(file.Hash);
                // Capture all active owners before native repair. The bytes may survive a
                // rolled-back model transaction or be shared with a later refresh batch.
                if (file.IsManaged)
                {
                    foreach (var usage in file.Usages)
                    {
                        if (usage.Parent is BeatmapSetInfo { DeletePending: false } set && isOwnedByO2Lazer(set))
                            pendingFileInvalidations.Add(set.ID);
                    }
                }
            }

            RealmFile addFile(Realm database, Stream stream, string hash)
            {
                if (!canObserve)
                {
                    verifyTime.Start();
                    var healthy = hasCurrentFile(hash);
                    verifyTime.Stop();
                    fallbackFileChecks++;
                    if (!healthy)
                        rememberDamage(database.Find<RealmFile>(hash)!);
                    return files.Add(stream, database, preferHardLinks: false);
                }
                using var observation = O2JamFileVerificationPatch.Observe(files, hash, rememberDamage, verifyTime);
                var file = files.Add(stream, database, preferHardLinks: false);
                if (observation.WasObserved)
                    nativeFileChecks++;
                else
                {
                    // A replaced host implementation or another patch may bypass the
                    // expected check. Preserve invalidation instead of silently losing it.
                    rememberDamage(file);
                    if (!warnedAboutFileObservation)
                    {
                        warnedAboutFileObservation = true;
                        Logger.Log("O2Lazer native file verification was not observed; conservatively invalidating its file owners.", level: LogLevel.Important);
                    }
                }
                return file;
            }

            modelTime.Start();
            realm.Write(database =>
            {
                modelBodyTime.Start();
                cancellationToken.ThrowIfCancellationRequested();
                var ruleset = database.Find<RulesetInfo>(O2LazerIdentity.ShortName);
                if (ruleset?.Available != true)
                {
                    modelBodyTime.Stop();
                    return;
                }

                // Cleanup may run between the two transactions. Never fall back to creating
                // an unreserved row while writing bytes inside the model transaction.
                if (requiredHashes.Any(hash => database.Find<RealmFile>(hash) == null))
                    throw new IOException("An OJN file reservation was removed before its model transaction; prepare and retry the batch.");
                lookupTime.Start();
                var lookup = new O2JamLibraryLookup(database, requests, setQueryTime, legacyQueryTime, pathQueryTime, legacyPathQueryTime, hashQueryTime);
                pathPredicates = lookup.PathPredicateCount;
                pathWildcardGroups = lookup.PathWildcardGroupCount;
                lookupTime.Stop();
                for (var index = 0; index < requests.Count; index++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    results[index] = write(database, ruleset, requests[index], updatedBeatmaps, lookup, sourceGuards, damagedFiles,
                        lookupTime, fileTime, projectionTime, addFile);
                    checkpoint?.Invoke(O2JamLibraryWriteStage.RequestWritten, index);
                }
                checkpoint?.Invoke(O2JamLibraryWriteStage.BeforeCommit, -1);
                cancellationToken.ThrowIfCancellationRequested();
                modelBodyTime.Stop();
            });
            modelTime.Stop();
        }
        finally
        {
            foreach (var guard in sourceGuards)
                guard.Dispose();
        }

        var notifyTime = Stopwatch.StartNew();
        foreach (var beatmap in updatedBeatmaps.GroupBy(beatmap => beatmap.ID).Select(group => group.Last()))
        {
            pendingFileInvalidations.Remove(beatmap.BeatmapSet!.ID);
            notifications.Publish(beatmap.ID, beatmap, recipients);
        }
        notifyTime.Stop();
        totalTime.Stop();
        // Sample the first and every sixteenth batch rather than logging every source. Include
        // callback time separately so native opening/locking/commit costs are not blamed on queries.
        if (++completedBatches == 1 || completedBatches % 16 == 0)
            Logger.Log(string.Create(CultureInfo.InvariantCulture,
                $"O2Jam write batch {completedBatches}: sources={requests.Count}, lookup_mode=native-link-digit-v2, path_predicates={pathPredicates}, path_wildcard_groups={pathWildcardGroups}, file_check_mode={(observeNativeFileChecks && O2JamFileVerificationPatch.IsInstalled ? "native-observed-v1" : "fallback-v1")}, native_file_checks={nativeFileChecks}, fallback_file_checks={fallbackFileChecks}, total={totalTime.Elapsed.TotalMilliseconds:F1} ms, source={sourceTime.Elapsed.TotalMilliseconds:F1}, reserve={reserveTime.Elapsed.TotalMilliseconds:F1}, verify={verifyTime.Elapsed.TotalMilliseconds:F1}, model={modelTime.Elapsed.TotalMilliseconds:F1}, body={modelBodyTime.Elapsed.TotalMilliseconds:F1}, lookup={lookupTime.Elapsed.TotalMilliseconds:F1}, files={fileTime.Elapsed.TotalMilliseconds:F1}, projection={projectionTime.Elapsed.TotalMilliseconds:F1}, notify={notifyTime.Elapsed.TotalMilliseconds:F1}, set_query={setQueryTime.Elapsed.TotalMilliseconds:F1}, legacy_query={legacyQueryTime.Elapsed.TotalMilliseconds:F1}, path_query={pathQueryTime.Elapsed.TotalMilliseconds:F1}, legacy_path_query={legacyPathQueryTime.Elapsed.TotalMilliseconds:F1}, hash_query={hashQueryTime.Elapsed.TotalMilliseconds:F1}."));

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

    public IReadOnlyDictionary<string, O2JamImportedSource> GetImportedSources(CancellationToken cancellationToken = default,
                                                                            O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update) =>
        GetImportedSources(cancellationToken, null, mode);

    internal IReadOnlyDictionary<string, O2JamImportedSource> GetImportedSources(CancellationToken cancellationToken,
                                                                              Action<O2JamLibraryProgress>? progress,
                                                                              O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var snapshots = readImportedSources(cancellationToken, progress);
        pendingFileInvalidations.IntersectWith(snapshots.Values.Select(snapshot => snapshot.Source.SetId));
        // Normal refresh only probes availability. Deep verification is an explicit repair
        // operation; native Add owns byte validation and repair for every actual write.
        var candidates = snapshots.Values.Count(snapshot => snapshot.Source.HasCurrentMetadata && snapshot.Source.HasCurrentEncoding);
        var checkedSources = 0;
        if (candidates > 0 && mode == O2JamLibraryRefreshMode.Repair)
            new O2JamLibraryProgress(0, candidates, O2JamLibraryStage.CheckingStoredFiles).Publish(progress);
        var checkedFiles = new Dictionary<string, bool>(StringComparer.Ordinal);
        bool checkFile(string hash)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!checkedFiles.TryGetValue(hash, out var current))
                checkedFiles.Add(hash, current = mode == O2JamLibraryRefreshMode.Repair ? hasCurrentFile(hash)
                    : hash.Length == 64 && hash.All(Uri.IsHexDigit) && files.Storage.Exists(new RealmFile { Hash = hash }.GetStoragePath()));
            return current;
        }
        O2JamImportedSource checkSource((O2JamImportedSource Source, string[]? Hashes) snapshot)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (!snapshot.Source.HasCurrentMetadata || !snapshot.Source.HasCurrentEncoding)
                return snapshot.Source;
            if (snapshot.Hashes == null || !snapshot.Hashes.All(checkFile))
                pendingFileInvalidations.Add(snapshot.Source.SetId);
            if (mode == O2JamLibraryRefreshMode.Repair)
                new O2JamLibraryProgress(++checkedSources, candidates, O2JamLibraryStage.CheckingStoredFiles).Publish(progress);
            return snapshot.Source with { CanReuseFiles = !pendingFileInvalidations.Contains(snapshot.Source.SetId) };
        }

        // Only plain file references leave Realm; filesystem probes stay outside its callback.
        var sources = snapshots.ToDictionary(entry => entry.Key, entry => checkSource(entry.Value), StringComparer.OrdinalIgnoreCase);
        cancellationToken.ThrowIfCancellationRequested();
        return sources;
    }

    private Dictionary<string, (O2JamImportedSource Source, string[]? Hashes)> readImportedSources(CancellationToken cancellationToken,
                                                                                               Action<O2JamLibraryProgress>? progress) => realm.Run(database =>
    {
        var sources = new Dictionary<string, (O2JamImportedSource Source, string[]? Hashes)>(StringComparer.OrdinalIgnoreCase);
        var query = database.All<BeatmapInfo>()
                               .Filter($@"{nameof(BeatmapInfo.BeatmapSet)}.{nameof(BeatmapSetInfo.DeletePending)} == false")
                               .Filter($@"{nameof(BeatmapInfo.Ruleset)}.{nameof(RulesetInfo.ShortName)} == $0", O2LazerIdentity.ShortName);
        var total = query.Count();
        var processed = 0;
        new O2JamLibraryProgress(0, total, O2JamLibraryStage.ReadingImportedMetadata).Publish(progress);
        var beatmaps = query
                               .AsEnumerable()
                               .Select(beatmap =>
                               {
                                   cancellationToken.ThrowIfCancellationRequested();
                                   return beatmap;
                               })
                               .Where(beatmap => beatmap.BeatmapSet != null)
                               .GroupBy(beatmap => beatmap.BeatmapSet!.ID);

        // Querying O2Lazer difficulties directly keeps unrelated osu! beatmap sets out of the
        // refresh index, which matters when the native library is much larger than the OJN folder.
        foreach (var setBeatmaps in beatmaps)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var ownedBeatmaps = setBeatmaps.ToArray();
            processed += ownedBeatmaps.Length;
            var sourceBeatmap = ownedBeatmaps.FirstOrDefault(candidate => tryGetSourcePath(candidate, out _));
            if (sourceBeatmap == null || !tryGetSourcePath(sourceBeatmap, out var sourcePath))
            {
                new O2JamLibraryProgress(processed, total, O2JamLibraryStage.ReadingImportedMetadata).Publish(progress);
                continue;
            }

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
            var current = validCharts && sourceLength != null
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
            var maniaCache = validCharts ? ownedBeatmaps.Select((candidate, index) => readDifficultyCache(candidate, metadata[index]!.Difficulty))
                .OfType<O2JamImportDifficultyCache>().ToArray() : null;
            var source = new O2JamImportedSource(
                setBeatmaps.Key, sourceBeatmap.LastLocalUpdate, sourceLength, current, encodingCurrent, sourceHash,
                encodingCurrent ? metadata[0]!.EncodingFallback : null, maniaCache,
                validCharts ? ownedBeatmaps.Select((candidate, index) => new O2JamStoredDifficultyIdentity(metadata[index]!.Difficulty, candidate.MD5Hash)).ToArray() : null);
            if (!sources.TryAdd(sourcePath, (source, referencedFileHashes(sourceBeatmap.BeatmapSet!, ownedBeatmaps, sourceHash))))
                throw new InvalidDataException("Multiple stored sets claim the same OJN path; automatic selection is unavailable.");
            new O2JamLibraryProgress(processed, total, O2JamLibraryStage.ReadingImportedMetadata).Publish(progress);
        }

        return sources;
    });

    private static string[]? referencedFileHashes(BeatmapSetInfo set, BeatmapInfo[] beatmaps, string? sourceHash)
    {
        if (sourceHash == null)
            return null;
        var hashes = new HashSet<string>(StringComparer.Ordinal) { sourceHash };
        foreach (var name in beatmaps.Select(beatmap => beatmap.Metadata.BackgroundFile)
                                     .Where(name => !string.IsNullOrEmpty(name)).Distinct(StringComparer.Ordinal))
        {
            var matches = set.Files.Where(usage => usage.Filename == name).Select(usage => usage.File.Hash)
                             .Distinct(StringComparer.Ordinal).ToArray();
            if (matches.Length != 1)
                return null;
            hashes.Add(matches[0]);
        }
        return hashes.ToArray();
    }

    private bool hasCurrentFile(string hash)
    {
        if (hash.Length != 64 || !hash.All(Uri.IsHexDigit))
            return false;
        try
        {
            using var stream = files.Store.GetStream(new RealmFile { Hash = hash }.GetStoragePath());
            return stream != null && stream.ComputeSHA2Hash() == hash;
        }
        catch (IOException)
        {
            return false;
        }
        catch (UnauthorizedAccessException)
        {
            return false;
        }
    }

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

    public int DeleteAll() => DeleteAll(null, CancellationToken.None);

    internal int DeleteAll(Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var ids = realm.Run(database => database.All<BeatmapSetInfo>()
                           .Where(set => !set.DeletePending)
                           .AsEnumerable()
                           .Where(isOwnedByO2Lazer)
                           .Select(set => set.ID).ToArray());
        var deleted = 0;
        new O2JamLibraryProgress(0, ids.Length, O2JamLibraryStage.ClearingCharts).Publish(progress);
        for (var offset = 0; offset < ids.Length; offset += 128)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(128, ids.Length - offset);
            deleted += realm.Write(database =>
            {
                var changed = 0;
                foreach (var id in ids.Skip(offset).Take(count))
                {
                    var set = database.Find<BeatmapSetInfo>(id);
                    if (set is not { DeletePending: false } || !isOwnedByO2Lazer(set))
                        continue;
                    set.DeletePending = true;
                    changed++;
                }
                return changed;
            });
            new O2JamLibraryProgress(offset + count, ids.Length, O2JamLibraryStage.ClearingCharts).Publish(progress);
        }
        return deleted;
    }

    internal IReadOnlyList<O2JamLibraryDifficultySource> GetCommittedDifficultySources(IReadOnlyList<O2JamLibraryWriteRequest> requests) =>
        realm.Run(database =>
        {
            var sources = new List<O2JamLibraryDifficultySource>();
            var ids = new HashSet<Guid>();
            foreach (var request in requests)
            {
                var plan = request.Plan;
                var file = database.Find<RealmFile>(plan.SourceHash.ToLowerInvariant());
                var sets = file?.Usages.AsEnumerable().Select(usage => usage.Parent).OfType<BeatmapSetInfo>()
                    .Where(set => !set.DeletePending && isOwnedByO2Lazer(set) && containsSourceContent(set, plan))
                    .DistinctBy(set => set.ID).Take(2).ToArray() ?? [];
                if (sets.Length != 1 || !ids.Add(sets[0].ID) || !tryGetSourcePath(sets[0], out var sourcePath))
                    continue;
                var caches = sets[0].Beatmaps.Select(beatmap =>
                    O2JamImportMetadata.Read(beatmap.Metadata.Tags, out var projection) == O2JamImportMetadataStatus.Valid
                        ? readDifficultyCache(beatmap, projection!.Difficulty) : null).OfType<O2JamImportDifficultyCache>().ToArray();
                if (caches.Length == plan.Charts.Count)
                    continue;
                sources.Add(new O2JamLibraryDifficultySource(sourcePath, new O2JamImportedSource(sets[0].ID,
                    plan.SourceTimestamp, plan.SourceData.LongLength, true, true, plan.SourceHash, plan.EncodingFallback, caches,
                    plan.Charts.Select(chart => new O2JamStoredDifficultyIdentity(chart.Difficulty, chart.Md5Hash)).ToArray())));
            }
            return (IReadOnlyList<O2JamLibraryDifficultySource>)sources;
        });

    private O2JamLibraryWriteResult write(Realm database, RulesetInfo ruleset, O2JamLibraryWriteRequest request,
                                        List<BeatmapInfo> updatedBeatmaps, O2JamLibraryLookup lookup, List<FileStream> sourceGuards,
                                        HashSet<string> damagedFiles, Stopwatch lookupTime, Stopwatch fileTime, Stopwatch projectionTime,
                                        Func<Realm, Stream, string, RealmFile> addFile)
    {
        var plan = request.Plan;

        O2JamLibraryWriteResult updateMetadata(BeatmapSetInfo set)
        {
            projectionTime.Start();
            var charts = validateMetadata(set, plan);
            projectionTime.Stop();
            // A disk repair can survive a rolled-back model transaction. Keep its cache
            // invalidation pending until this set commits, including shared-cover batches.
            if (fileHashes(plan).Any(damagedFiles.Contains))
                pendingFileInvalidations.Add(set.ID);
            fileTime.Start();
            var changed = synchroniseFiles(database, set, plan, damagedFiles, addFile);
            fileTime.Stop();
            changed |= pendingFileInvalidations.Contains(set.ID);
            projectionTime.Start();
            changed |= refreshMetadata(set, plan, charts, database);
            projectionTime.Stop();
            if (!changed)
                return O2JamLibraryWriteResult.AlreadyPresent;

            updatedBeatmaps.AddRange(set.Beatmaps.Where(beatmap => beatmap.Ruleset.ShortName == O2LazerIdentity.ShortName)
                                       .Select(beatmap => beatmap.Detach()));
            return O2JamLibraryWriteResult.Updated;
        }

        lookupTime.Start();
        var sourceSet = lookup.FindSource(request);

        // Older releases can produce a different set hash when an OJN difficulty contains
        // blocks but no playable notes. The unchanged source file is the stronger identity;
        // migrate its metadata in place so Beatmap IDs and attached scores remain intact.
        if (sourceSet != null)
            ensureSupportedProjection(sourceSet);

        var matchingSet = lookup.FindContent(plan.SourceHash);
        lookupTime.Stop();
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
                sourceSet.DeletePending = true;

            return result;
        }

        lookupTime.Start();
        var setHashExists = lookup.ContainsSetHash(plan.SetHash);
        lookupTime.Stop();
        if (setHashExists)
            throw new InvalidDataException("The set hash matches without verifiable source content.");

        var replacedSet = sourceSet;

        var beatmapSet = new BeatmapSetInfo
        {
            OnlineID = -1,
            DateAdded = DateTimeOffset.UtcNow,
            Hash = plan.SetHash,
        };
        fileTime.Start();
        synchroniseFiles(database, beatmapSet, plan, damagedFiles, addFile);
        fileTime.Stop();

        projectionTime.Start();
        foreach (var chart in plan.Charts)
            addDifficulty(beatmapSet, ruleset, plan, chart);

        database.Add(beatmapSet);
        lookup.Track(beatmapSet);
        projectionTime.Stop();
        if (replacedSet != null)
            replacedSet.DeletePending = true;

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
        // Only changing MD5s need ownership counts. Native Count avoids materialising
        // the entire library for every source while retaining foreign and deleted owners.
        var collectionOwners = database == null ? null : charts
            .Where(entry => entry.Slot.IsPlayable && entry.Beatmap.MD5Hash != entry.Slot.Md5Hash && !string.IsNullOrEmpty(entry.Beatmap.MD5Hash))
            .Select(entry => entry.Beatmap.MD5Hash).Distinct(StringComparer.OrdinalIgnoreCase)
            .ToDictionary(hash => hash, hash => database.All<BeatmapInfo>()
                .Filter($@"{nameof(BeatmapInfo.MD5Hash)} ==[c] $0", hash).Count(), StringComparer.OrdinalIgnoreCase);
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
        changed |= update(beatmap.StarRating, chart.HasManiaDifficulty ? chart.ManiaStarRating : -1, value => beatmap.StarRating = value);
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
            $"o2jam {songTag} {MetadataMarker} {EncodingMarker} {source_length_prefix}{plan.SourceData.LongLength} {O2JamImportMetadata.Create(plan, chart)}")));
        if (chart.HasManiaDifficulty)
            projectedTags += $" {O2JamStarRatingMetadata.ManiaVersionTag} {O2JamStarRatingMetadata.CreateManiaMaxComboTag(chart.ManiaMaxCombo)}";
        changed |= update(metadata.Tags, projectedTags, value => metadata.Tags = value);
        return changed;
    }

    private static bool synchroniseFiles(Realm database, BeatmapSetInfo set, O2JamImportPlan plan, HashSet<string> damagedFiles,
                                         Func<Realm, Stream, string, RealmFile> addFile)
    {
        var changed = false;
        using var sourceStream = new MemoryStream(plan.SourceData, writable: false);
        var source = addFile(database, sourceStream, plan.SourceHash.ToLowerInvariant());
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
            background = addFile(database, stream, stream.ComputeSHA2Hash());
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
        return changed || fileHashes(plan).Any(damagedFiles.Contains);
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
        => containsSourceContent(set, plan.SourceHash);

    internal static bool containsSourceContent(BeatmapSetInfo set, string sourceHash)
    {
        var sourceHashes = set.Files.Where(file => file.Filename.EndsWith(".ojn", StringComparison.OrdinalIgnoreCase))
                              .Select(file => file.File.Hash).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return sourceHashes.Length == 1
            ? string.Equals(sourceHashes[0], sourceHash, StringComparison.OrdinalIgnoreCase)
            : sourceHashes.Length == 0 && set.Beatmaps.Any(beatmap => string.Equals(beatmap.Hash, sourceHash, StringComparison.OrdinalIgnoreCase));
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
