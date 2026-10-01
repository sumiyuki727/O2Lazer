using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using O2Jam.Formats.Ojn;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Host.Library;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojm;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

public sealed record O2JamImportSummary(int Imported, int Updated, int AlreadyPresent, int Failed, bool RulesetUnavailable)
{
    public int PendingNotifications { get; init; }
}

public sealed class O2JamImportCancelledException(O2JamImportSummary summary, CancellationToken cancellationToken)
    : OperationCanceledException("The refresh stopped after committing part of the library.", cancellationToken)
{
    public O2JamImportSummary Summary { get; } = summary;
}

public sealed class O2JamImportService
{
    private const int write_batch_size = 8;
    private const int scan_batch_size = 128;
    private const int scan_parallelism = 4;
    private const int preparation_parallelism = 2;
    private readonly O2JamImportPlanner planner;
    private readonly IO2JamLibraryWriter writer;
    private readonly Action<string> invalidateCaches;
    private readonly O2JamLibraryNotificationQueue<string, string> notifications = new();

    public O2JamImportService(O2JamImportPlanner planner, IO2JamLibraryWriter writer)
        : this(planner, writer, invalidateSourceCaches)
    {
    }

    internal O2JamImportService(O2JamImportPlanner planner, IO2JamLibraryWriter writer, Action<string> invalidateCaches)
    {
        this.planner = planner;
        this.writer = writer;
        this.invalidateCaches = invalidateCaches;
    }

    public O2JamImportSummary Import(IEnumerable<string> paths, Action<Exception, string>? failure = null)
        => Refresh(paths.ToArray(), new Dictionary<string, O2JamImportedSource>(), failure: failure);

    public O2JamImportSummary Refresh(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, O2JamImportedSource> importedSources,
        Action<int, int>? progress = null,
        Action<Exception, string>? failure = null,
        CancellationToken cancellationToken = default)
    {
        OjnDirectoryEncoding.Shared.Clear();
        writer.RetryNotifications();
        notifications.Retry([invalidateCaches]);
        var orderedPaths = paths.Select(Path.GetFullPath).Order(StringComparer.OrdinalIgnoreCase)
                                .ThenBy(path => path, StringComparer.Ordinal).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var imported = 0;
        var updated = 0;
        var existing = 0;
        var failed = 0;
        var processed = 0;
        var unavailable = false;
        var scanned = new List<ScannedSource>();
        var pending = new List<ImportGroup>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
            try { progress?.Invoke(processed, orderedPaths.Length); }
            catch (Exception exception) { Logger.Error(exception, "O2Jam refresh progress observer failed."); }
        }

        void invalidate(string path) => notifications.Publish(path, path, [invalidateCaches]);

        publishProgress();
        try
        {
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
                    try { batch[index] = new ScannedSource(path, O2JamSourceSnapshot.ReadHash(path), null); }
                    catch (Exception exception) { batch[index] = new ScannedSource(path, null, exception); }
                });
                foreach (var item in batch)
                {
                    if (item.Exception != null)
                    {
                        failed++;
                        processed++;
                        report(item.Exception, item.Path);
                    }
                    else
                    {
                        scanned.Add(item);
                        scannedPaths.Add(item.Path);
                    }
                }
            }

            foreach (var group in scanned.GroupBy(source => source.Hash!, StringComparer.OrdinalIgnoreCase))
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
                            if (string.Equals(O2JamSourceSnapshot.ReadHash(registered.Key), group.Key, StringComparison.OrdinalIgnoreCase))
                                path = registered.Key;
                        }
                        if (!string.Equals(path, registered.Key, StringComparison.OrdinalIgnoreCase)
                            && importedSources.TryGetValue(path, out var destination) && destination.SetId != source.SetId)
                            throw new InvalidDataException("The copy's destination is registered to another OJN set; automatic path exchanges are unavailable.");

                        if (string.Equals(path, registered.Key, StringComparison.OrdinalIgnoreCase) && isUnchanged(path, source))
                        {
                            existing += copies.Length;
                            processed += copies.Length;
                            invalidate(path);
                            continue;
                        }
                    }
                    pending.Add(new ImportGroup(path, group.Key, source, copies.Length));
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    failed += copies.Length;
                    processed += copies.Length;
                    report(exception, path);
                }
            }
            if (processed != 0)
                publishProgress();

            // Relocate existing content before importing replacement bytes at its previous path.
            // This preserves the old beatmap IDs and scores when both copies are in one refresh.
            pending = pending.OrderBy(group => group.Source == null).ThenBy(group => group.Path, StringComparer.OrdinalIgnoreCase).ToList();
            for (var offset = 0; offset < pending.Count; offset += write_batch_size)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var count = Math.Min(write_batch_size, pending.Count - offset);
                var prepared = new PreparedImport[count];
                Parallel.For(0, count, new ParallelOptions
                {
                    MaxDegreeOfParallelism = preparation_parallelism,
                    CancellationToken = cancellationToken,
                }, index =>
                {
                    var group = pending[offset + index];
                    try
                    {
                        var plan = planner.Create(group.Path, group.Source);
                        if (!string.Equals(plan.SourceHash, group.Hash, StringComparison.OrdinalIgnoreCase))
                            throw new IOException("The OJN changed after its content scan.");
                        prepared[index] = new PreparedImport(group, plan, null);
                    }
                    catch (Exception exception) { prepared[index] = new PreparedImport(group, null, exception); }
                });
                var requests = prepared.Where(item => item.Plan != null)
                                       .Select(item => new O2JamLibraryWriteRequest(item.Plan!, item.Group.Source?.SetId)).ToArray();
                cancellationToken.ThrowIfCancellationRequested();
                IReadOnlyList<O2JamLibraryWriteResult> results = [];
                Exception? writeFailure = null;
                try { results = writer.WriteBatch(requests, cancellationToken); }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
                catch (Exception exception) { writeFailure = exception; }
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
                    processed += item.Group.Count;
                }
                // Account for the entire committed transaction and finish its invalidations
                // before honouring cancellation raised by a post-commit observer.
                publishProgress();
            }
            cancellationToken.ThrowIfCancellationRequested();
            var retainedSetIds = pending.Where(group => group.Source != null).Select(group => group.Source!.SetId).ToHashSet();
            if (failed == 0 && !unavailable)
                writer.MarkDeleted(importedSources.Where(source => !retainedSetIds.Contains(source.Value.SetId)
                                                                   && !scannedPaths.Contains(source.Key)
                                                                   && O2JamSourcePresence.IsDefinitelyMissing(source.Key))
                                                  .Select(source => source.Value.SetId));
            return summary();
        }
        catch (OperationCanceledException) when (imported + updated > 0)
        {
            throw new O2JamImportCancelledException(summary(), cancellationToken);
        }
    }

    internal static bool isUnchanged(string path, O2JamImportedSource source)
    {
        if (!source.HasCurrentMetadata || source.LastLocalUpdate == null || source.SourceLength == null
            || string.IsNullOrWhiteSpace(source.SourceHash))
            return false;
        var info = new FileInfo(path);
        if (!info.Exists || info.Length != source.SourceLength || O2JamSourceTimestamp.Read(path) != source.LastLocalUpdate)
            return false;
        var snapshot = O2JamSourceSnapshot.Read(path);
        if (snapshot.Timestamp != source.LastLocalUpdate
            || !string.Equals(snapshot.Hash, source.SourceHash, StringComparison.OrdinalIgnoreCase)
            || source.DifficultyIdentities?.Any(identity => !string.Equals(identity.Md5Hash,
                O2JamBeatmapIdentity.Md5FromSource(snapshot.Data, identity.Difficulty), StringComparison.OrdinalIgnoreCase)) == true)
            return false;
        if (source.EncodingFallback is { } encoding && OjnDirectoryEncoding.Shared.GetForFile(path) != encoding)
            return false;
        return source.HasCurrentEncoding || !OjnReader.RequiresLegacyEncodingMigration(path);
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
