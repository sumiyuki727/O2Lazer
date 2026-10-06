using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

public sealed record O2JamImportSummary(int Imported, int Updated, int AlreadyPresent, int Failed, bool RulesetUnavailable)
{
    public int PendingNotifications { get; init; }
    public int FailedDifficultyCalculations { get; init; }
}

public sealed class O2JamImportCancelledException(O2JamImportSummary summary, CancellationToken cancellationToken)
    : OperationCanceledException("The refresh stopped after committing part of the library.", cancellationToken)
{
    public O2JamImportSummary Summary { get; } = summary;
}

public sealed class O2JamImportService
{
    // Amortise native transaction costs while keeping prepared bytes and rollback scope bounded.
    private const int write_batch_size = 16;
    private const int scan_batch_size = 128;
    private const int scan_parallelism = 4;
    private const int preparation_parallelism = 2;
    private readonly Func<string, O2JamImportedSource?, O2JamImportPlan> prepareSource;
    private readonly IO2JamLibraryWriter writer;
    private readonly Action<string> invalidateCaches;
    private readonly Func<string, string> readSourceHash;
    private readonly O2JamLibraryNotificationQueue<string, string> notifications = new();

    public O2JamImportService(O2JamImportPlanner planner, IO2JamLibraryWriter writer)
        : this(planner, writer, invalidateSourceCaches)
    {
    }

    internal O2JamImportService(O2JamImportPlanner planner, IO2JamLibraryWriter writer, Action<string> invalidateCaches,
                               Func<string, string>? readSourceHash = null,
                               Func<string, O2JamImportedSource?, O2JamImportPlan>? prepareSource = null)
    {
        this.prepareSource = prepareSource ?? planner.Create;
        this.writer = writer;
        this.invalidateCaches = invalidateCaches;
        this.readSourceHash = readSourceHash ?? O2JamSourceSnapshot.ReadHash;
    }

    public O2JamImportSummary Import(IEnumerable<string> paths, Action<Exception, string>? failure = null)
        => Refresh(paths.ToArray(), new Dictionary<string, O2JamImportedSource>(), failure: failure);

    public O2JamImportSummary Refresh(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, O2JamImportedSource> importedSources,
        Action<int, int>? progress = null,
        Action<Exception, string>? failure = null,
        CancellationToken cancellationToken = default,
        O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update) =>
        Refresh(paths, importedSources, progress, failure, cancellationToken, null, mode);

    internal O2JamImportSummary Refresh(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, O2JamImportedSource> importedSources,
        Action<int, int>? progress,
        Action<Exception, string>? failure,
        CancellationToken cancellationToken,
        Action<O2JamLibraryProgress>? preparationProgress,
        O2JamLibraryRefreshMode mode = O2JamLibraryRefreshMode.Update,
        Action<IReadOnlyList<O2JamLibraryWriteRequest>>? committed = null)
    {
        OjnDirectoryEncoding.Shared.Clear();
        writer.RetryNotifications();
        notifications.Retry([invalidateCaches]);
        var orderedPaths = paths.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase)
                                .ThenBy(path => path, StringComparer.Ordinal).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var sourcePaths = orderedPaths.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var imported = 0;
        var updated = 0;
        var existing = 0;
        var failed = 0;
        var processed = 0;
        var progressTotal = 0;
        var unavailable = false;
        var scanned = new List<ScannedSource>();
        var pending = new List<ImportGroup>();
        var scannedPaths = new Dictionary<string, ScannedSource>(StringComparer.OrdinalIgnoreCase);
        using var preparationCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        Task<(PreparedImport[] Items, double Elapsed)>? preparation = null;
        var content = importedSources.Where(source => !string.IsNullOrWhiteSpace(source.Value.SourceHash))
                                     .GroupBy(source => source.Value.SourceHash!, StringComparer.OrdinalIgnoreCase)
                                     .ToDictionary(group => group.Key, group => group.ToArray(), StringComparer.OrdinalIgnoreCase);

        O2JamImportSummary summary() => new(imported, updated, existing, failed, unavailable)
        {
            PendingNotifications = writer.PendingNotifications + notifications.Count,
        };

        void report(Exception exception, string path)
        {
            try { failure?.Invoke(exception, path); }
            catch (Exception observerFailure) { Logger.Error(observerFailure, "O2Jam refresh failure observer failed."); }
        }

        void publishProgress()
        {
            try { progress?.Invoke(processed, progressTotal); }
            catch (Exception exception) { Logger.Error(exception, "O2Jam refresh progress observer failed."); }
        }

        void invalidate(string path) => notifications.Publish(path, path, [invalidateCaches]);

        try
        {
            if (orderedPaths.Length > 0)
                new O2JamLibraryProgress(0, orderedPaths.Length, O2JamLibraryStage.ReadingSourceFiles).Publish(preparationProgress);
            Logger.Log($"O2Jam refresh: reading {orderedPaths.Length} source fingerprints.");
            // Source bytes remain the identity proof even when an editor preserves the file
            // stamp. Normal refresh reuses this one fingerprint instead of reading a snapshot
            // again just to decide whether the current projection can be skipped.
            // Only fingerprints survive the scan. Retaining every OJN snapshot here would make
            // memory proportional to the entire library instead of the bounded preparation batch.
            for (var offset = 0; offset < orderedPaths.Length; offset += scan_batch_size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(scan_batch_size, orderedPaths.Length - offset);
                var batch = new ScannedSource[count];
                Parallel.For(0, count, new ParallelOptions
                {
                    MaxDegreeOfParallelism = scan_parallelism,
                    CancellationToken = cancellationToken,
                }, index =>
                {
                    var path = orderedPaths[offset + index];
                    try { batch[index] = new ScannedSource(path, readSourceHash(path), null); }
                    catch (Exception exception) { batch[index] = new ScannedSource(path, null, exception); }
                });
                foreach (var item in batch)
                {
                    scannedPaths.Add(item.Path, item);
                    if (item.Exception != null)
                    {
                        failed++;
                        processed++;
                        report(item.Exception, item.Path);
                    }
                    else
                    {
                        scanned.Add(item);
                    }
                }
                new O2JamLibraryProgress(offset + count, orderedPaths.Length, O2JamLibraryStage.ReadingSourceFiles).Publish(preparationProgress);
            }

            var matched = 0;
            if (scanned.Count > 0)
                new O2JamLibraryProgress(0, scanned.Count, O2JamLibraryStage.MatchingSources).Publish(preparationProgress);
            Logger.Log($"O2Jam refresh: matching {scanned.Count} source fingerprints.");
            var sourceGroups = scanned.GroupBy(source => source.Hash!, StringComparer.OrdinalIgnoreCase).ToArray();
            // Refresh counts represent content identities, not duplicate file paths.
            // Unreadable paths remain separate failed work because their identity is unknown.
            progressTotal = sourceGroups.Length + processed;
            if (preparationProgress == null)
                publishProgress();
            foreach (var group in sourceGroups)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var copies = group.ToArray();
                var path = copies[0].Path;
                O2JamImportedSource? source = null;
                try
                {
                    if (content.TryGetValue(group.Key, out var matches))
                    {
                        if (matches.Select(match => match.Value.SetId).Distinct().Count() != 1)
                            throw new InvalidDataException("Multiple stored sets claim this OJN content; automatic merging is unavailable.");
                        var registered = matches.OrderBy(match => match.Key, StringComparer.OrdinalIgnoreCase).First();
                        source = registered.Value;
                        if (!O2JamSourcePresence.IsDefinitelyMissing(registered.Key))
                        {
                            // Existence alone does not prove this path still contains the registered
                            // source. An unreadable path cannot be silently replaced by a copy either.
                            // A successful scan already proved this path's content for this round.
                            // Preparation and commit still revalidate bytes before publishing changes.
                            if (scannedPaths.TryGetValue(registered.Key, out var registeredScan) && registeredScan.Exception != null)
                                throw registeredScan.Exception;
                            var registeredHash = registeredScan != null ? registeredScan.Hash : readSourceHash(registered.Key);
                            if (string.Equals(registeredHash, group.Key, StringComparison.OrdinalIgnoreCase))
                                path = registered.Key;
                        }
                        if (!string.Equals(path, registered.Key, StringComparison.OrdinalIgnoreCase)
                            && importedSources.TryGetValue(path, out var destination) && destination.SetId != source.SetId)
                            throw new InvalidDataException("The copy's destination is registered to another OJN set; automatic path exchanges are unavailable.");

                        if (string.Equals(path, registered.Key, StringComparison.OrdinalIgnoreCase)
                            && isUnchanged(path, source, mode == O2JamLibraryRefreshMode.Repair))
                        {
                            existing += copies.Length;
                            processed++;
                            invalidate(path);
                            continue;
                        }
                    }
                    pending.Add(new ImportGroup(path, group.Key, source, copies.Length));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed += copies.Length;
                    processed++;
                    report(exception, path);
                }
                finally
                {
                    matched += copies.Length;
                    new O2JamLibraryProgress(matched, scanned.Count, O2JamLibraryStage.MatchingSources).Publish(preparationProgress);
                }
            }
            if (processed != 0)
                publishProgress();
            else
                new O2JamLibraryProgress(0, progressTotal).Publish(preparationProgress);
            Logger.Log($"O2Jam refresh: preparing {pending.Count} unique sources for bounded writes.");

            // Relocate existing content before importing replacement bytes at its previous path.
            // This preserves the old beatmap IDs and scores when both copies are in one refresh.
            pending = pending.OrderBy(group => group.Source == null).ThenBy(group => group.Path, StringComparer.OrdinalIgnoreCase).ToList();
            Task<(PreparedImport[] Items, double Elapsed)> prepareBatch(int offset) => Task.Run(() =>
            {
                var watch = Stopwatch.StartNew();
                var items = new PreparedImport[Math.Min(write_batch_size, pending.Count - offset)];
                Parallel.For(0, items.Length, new ParallelOptions
                {
                    MaxDegreeOfParallelism = preparation_parallelism,
                    CancellationToken = preparationCancellation.Token,
                }, index =>
                {
                    var group = pending[offset + index];
                    try
                    {
                        var plan = prepareSource(group.Path, group.Source);
                        if (!string.Equals(plan.SourceHash, group.Hash, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("The OJN changed after its content scan.");
                        items[index] = new PreparedImport(group, plan, null);
                    }
                    catch (Exception exception) { items[index] = new PreparedImport(group, null, exception); }
                });
                return (items, watch.Elapsed.TotalMilliseconds);
            });
            if (pending.Count > 0)
                preparation = prepareBatch(0);
            for (var offset = 0; offset < pending.Count; offset += write_batch_size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(write_batch_size, pending.Count - offset);
                var wait = Stopwatch.StartNew();
                var batch = preparation!.GetAwaiter().GetResult();
                wait.Stop();
                var prepared = batch.Items;
                // Keep just one batch ahead. Commit still verifies the source guards, so
                // preparing early cannot publish bytes edited while the previous batch writes.
                preparation = offset + count < pending.Count ? prepareBatch(offset + count) : null;
                var requests = prepared.Where(item => item.Plan != null)
                                       .Select(item => new O2JamLibraryWriteRequest(item.Plan!, item.Group.Source?.SetId)).ToArray();
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<O2JamLibraryWriteResult> results = [];
                Exception? writeFailure = null;
                var writeTime = Stopwatch.StartNew();
                try { results = writer.WriteBatch(requests, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { writeFailure = exception; }
                writeTime.Stop();
                if (offset == 0 || offset / write_batch_size % 16 == 0 || offset + count == pending.Count)
                    Logger.Log(FormattableString.Invariant($"O2Jam import batch: sources={offset + count}/{pending.Count}, prepare_overlap=one-batch-v1, prepare={batch.Elapsed:F1} ms, prepare_wait={wait.Elapsed.TotalMilliseconds:F1} ms, write={writeTime.Elapsed.TotalMilliseconds:F1} ms."));
                var writeIndex = 0;
                foreach (var item in prepared)
                {
                    var exception = item.Exception ?? writeFailure;
                    if (exception != null)
                    {
                        failed += item.Group.Count;
                        report(exception, item.Group.Path);
                    }
                    else
                    {
                        switch (results[writeIndex++])
                        {
                            case O2JamLibraryWriteResult.Imported:
                                imported++;
                                existing += item.Group.Count - 1;
                                invalidate(item.Group.Path);
                                break;
                            case O2JamLibraryWriteResult.Updated:
                                updated++;
                                existing += item.Group.Count - 1;
                                invalidate(item.Group.Path);
                                break;
                            case O2JamLibraryWriteResult.AlreadyPresent:
                                existing += item.Group.Count;
                                invalidate(item.Group.Path);
                                break;
                            case O2JamLibraryWriteResult.RulesetUnavailable:
                                unavailable = true;
                                break;
                        }
                    }
                    processed++;
                }
                // Account for the entire committed transaction and finish its invalidations
                // before honouring cancellation raised by a post-commit observer.
                publishProgress();
                if (writeFailure == null && !cancellationToken.IsCancellationRequested)
                {
                    try { committed?.Invoke(requests); }
                    catch (Exception exception) { Logger.Error(exception, "O2Jam could not schedule committed sources for difficulty calculation."); }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
            var retainedSetIds = pending.Where(group => group.Source != null).Select(group => group.Source!.SetId).ToHashSet();
            if (failed == 0 && !unavailable)
                writer.MarkDeleted(importedSources.Where(source => !retainedSetIds.Contains(source.Value.SetId)
                                                                   && !sourcePaths.Contains(source.Key)
                                                                   && O2JamSourcePresence.IsDefinitelyMissing(source.Key))
                                                  .Select(source => source.Value.SetId));
            return summary();
        }
        catch (OperationCanceledException) when (imported + updated > 0)
        {
            throw new O2JamImportCancelledException(summary(), cancellationToken);
        }
        finally
        {
            preparationCancellation.Cancel();
            if (preparation != null)
            {
                // No reader may outlive the refresh and overlap the next operation's caches.
                try { preparation.GetAwaiter().GetResult(); }
                catch (OperationCanceledException) { }
            }
        }
    }

    internal static bool isUnchanged(string path, O2JamImportedSource source, bool verifyContent = true)
    {
        if (!source.HasCurrentMetadata || !source.CanReuseFiles || source.LastLocalUpdate == null || source.SourceLength == null
            || string.IsNullOrWhiteSpace(source.SourceHash))
            return false;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != source.SourceLength || O2JamSourceTimestamp.Read(path) != source.LastLocalUpdate)
            return false;
        if (source.EncodingFallback is { } encoding && OjnDirectoryEncoding.Shared.GetForFile(path) != encoding)
            return false;
        if (!source.HasCurrentEncoding)
            return false;
        if (!verifyContent)
            return true;
        var snapshot = O2JamSourceSnapshot.Read(path);
        if (snapshot.Timestamp != source.LastLocalUpdate
            || !string.Equals(snapshot.Hash, source.SourceHash, StringComparison.OrdinalIgnoreCase)
            || source.DifficultyIdentities?.Any(identity => !string.Equals(identity.Md5Hash,
                O2JamBeatmapIdentity.Md5FromSource(snapshot.Data, identity.Difficulty), StringComparison.OrdinalIgnoreCase)) == true)
            return false;
        return true;
    }

    private static void invalidateSourceCaches(string sourcePath)
    {
        // Explicit refresh also discards OJM data: an archive may change without an OJN change,
        // and these playback caches deliberately use cheap file stamps on the hot path.
        OjnDocumentCache.Shared.Invalidate(sourcePath);
        OjmArchiveCache.Shared.InvalidateSource(sourcePath);
    }

    private sealed record ScannedSource(string Path, string? Hash, Exception? Exception);
    private sealed record ImportGroup(string Path, string Hash, O2JamImportedSource? Source, int Count);
    private sealed record PreparedImport(ImportGroup Group, O2JamImportPlan? Plan, Exception? Exception);
}
