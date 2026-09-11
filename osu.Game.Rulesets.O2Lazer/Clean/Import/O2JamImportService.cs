using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

public sealed record O2JamImportSummary(int Imported, int Updated, int AlreadyPresent, int Failed, bool RulesetUnavailable);

public sealed class O2JamImportService(O2JamImportPlanner planner, O2JamLibraryWriter writer)
{
    private const int write_batch_size = 8;
    private const int scan_batch_size = 128;
    private const int scan_parallelism = 4;
    private const int preparation_parallelism = 2;

    public O2JamImportSummary Import(IEnumerable<string> paths, Action<Exception, string>? failure = null)
    {
        OjnDirectoryEncoding.Shared.Clear();
        var imported = 0;
        var updated = 0;
        var existing = 0;
        var failed = 0;
        var unavailable = false;

        foreach (var path in paths)
        {
            try
            {
                switch (writer.Write(planner.Create(path)))
                {
                    case O2JamLibraryWriteResult.Imported:
                        imported++;
                        break;

                    case O2JamLibraryWriteResult.Updated:
                        updated++;
                        break;

                    case O2JamLibraryWriteResult.AlreadyPresent:
                        existing++;
                        break;

                    case O2JamLibraryWriteResult.RulesetUnavailable:
                        unavailable = true;
                        break;
                }
            }
            catch (Exception exception)
            {
                failed++;
                failure?.Invoke(exception, path);
            }
        }

        return new O2JamImportSummary(imported, updated, existing, failed, unavailable);
    }

    public O2JamImportSummary Refresh(
        IReadOnlyList<string> paths,
        IReadOnlyDictionary<string, O2JamImportedSource> importedSources,
        Action<int, int>? progress = null,
        Action<Exception, string>? failure = null,
        CancellationToken cancellationToken = default)
    {
        // Editing existing files need not change the directory timestamp. A user-requested refresh
        // therefore also refreshes the bounded encoding samples, without polling during song select.
        OjnDirectoryEncoding.Shared.Clear();
        var imported = 0;
        var updated = 0;
        var existing = 0;
        var failed = 0;
        var unavailable = false;
        progress?.Invoke(0, paths.Count);

        var importedContent = importedSources
                              .Where(source => source.Value.SourceLength != null && !string.IsNullOrWhiteSpace(source.Value.SourceHash))
                              .GroupBy(source => source.Value.SourceLength!.Value)
                              .ToDictionary(
                                  lengthGroup => lengthGroup.Key,
                                  lengthGroup => lengthGroup.GroupBy(source => source.Value.SourceHash!, StringComparer.OrdinalIgnoreCase)
                                                            .ToDictionary(
                                                                hashGroup => hashGroup.Key,
                                                                hashGroup => hashGroup.ToArray(),
                                                                StringComparer.OrdinalIgnoreCase));

        var pending = new List<ScannedImport>();
        var scannedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        // A bounded scan batch keeps unchanged sources out of the expensive importer while still
        // publishing enough intermediate values for the native progress animation to remain clear.
        for (var offset = 0; offset < paths.Count; offset += scan_batch_size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(scan_batch_size, paths.Count - offset);
            var scanned = new ScannedImport[count];

            Parallel.For(0, count, new ParallelOptions
            {
                MaxDegreeOfParallelism = scan_parallelism,
                CancellationToken = cancellationToken,
            }, index =>
            {
                var path = paths[offset + index];

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    var fullPath = Path.GetFullPath(path);
                    importedSources.TryGetValue(fullPath, out var source);
                    var wasSkipped = source != null && isUnchanged(fullPath, source);

                    if (source == null && tryFindImportedContent(fullPath, importedContent, out var matches))
                    {
                        var existingMatch = matches.FirstOrDefault(match => File.Exists(match.Key));
                        if (!existingMatch.Equals(default(KeyValuePair<string, O2JamImportedSource>)))
                        {
                            // A second path for the same source adds no new library entry.
                            wasSkipped = true;
                        }
                        else
                        {
                            // The source moved. Reusing its set preserves beatmap IDs and lets the
                            // writer replace the stale external path before missing-source cleanup.
                            source = matches[0].Value;
                        }
                    }

                    scanned[index] = new ScannedImport(fullPath, source, wasSkipped, null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    scanned[index] = new ScannedImport(path, null, false, exception);
                }
            });

            foreach (var item in scanned)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Exception != null)
                {
                    failed++;
                    failure?.Invoke(item.Exception, item.Path);
                }
                else
                {
                    scannedPaths.Add(item.Path);
                    if (item.WasSkipped)
                        existing++;
                    else
                        pending.Add(item);
                }
            }

            progress?.Invoke(existing + failed, paths.Count);
        }

        var processed = existing + failed;

        for (var offset = 0; offset < pending.Count; offset += write_batch_size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(write_batch_size, pending.Count - offset);
            var prepared = new PreparedImport[count];

            // OJN decoding and native mania difficulty calculation are CPU and I/O intensive.
            // Keeping two workers leaves headroom for osu!'s update, audio and song-select tasks.
            Parallel.For(0, count, new ParallelOptions
            {
                MaxDegreeOfParallelism = preparation_parallelism,
                CancellationToken = cancellationToken,
            }, index =>
            {
                var item = pending[offset + index];

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    prepared[index] = new PreparedImport(item.Path, item.Source, planner.Create(item.Path), null);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    prepared[index] = new PreparedImport(item.Path, item.Source, null, exception);
                }
            });

            var requests = prepared.Where(item => item.Plan != null)
                                   .Select(item => new O2JamLibraryWriteRequest(
                                       item.Plan!,
                                       item.Source?.SetId,
                                       SourceIndexWasLoaded: true))
                                   .ToArray();
            cancellationToken.ThrowIfCancellationRequested();
            var writeResults = writer.WriteBatch(requests);
            var writeIndex = 0;

            foreach (var item in prepared)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (item.Exception != null)
                {
                    failed++;
                    failure?.Invoke(item.Exception, item.Path);
                }
                else
                {
                    switch (writeResults[writeIndex++])
                    {
                        case O2JamLibraryWriteResult.Imported:
                            imported++;
                            break;

                        case O2JamLibraryWriteResult.Updated:
                            updated++;
                            break;

                        case O2JamLibraryWriteResult.AlreadyPresent:
                            existing++;
                            break;

                        case O2JamLibraryWriteResult.RulesetUnavailable:
                            unavailable = true;
                            break;
                    }
                }
            }

            processed += count;
            progress?.Invoke(processed, paths.Count);
        }

        cancellationToken.ThrowIfCancellationRequested();
        var retainedSetIds = pending.Where(item => item.Source != null)
                                    .Select(item => item.Source!.SetId)
                                    .ToHashSet();
        writer.MarkDeleted(importedSources.Where(source => !retainedSetIds.Contains(source.Value.SetId)
                                                           && !scannedPaths.Contains(source.Key)
                                                           && !File.Exists(source.Key))
                                          .Select(source => source.Value.SetId));

        return new O2JamImportSummary(imported, updated, existing, failed, unavailable);
    }

    internal static bool isUnchanged(string path, O2JamImportedSource source)
    {
        if (!source.HasCurrentMetadata || source.LastLocalUpdate == null || source.SourceLength == null)
            return false;

        var info = new FileInfo(path);
        if (!info.Exists
            || info.Length != source.SourceLength
            || O2JamLibraryWriter.getSourceTimestamp(path) != source.LastLocalUpdate)
            return false;

        return source.HasCurrentEncoding || !OjnReader.RequiresLegacyEncodingMigration(path);
    }

    private static bool tryFindImportedContent(
        string path,
        IReadOnlyDictionary<long, Dictionary<string, KeyValuePair<string, O2JamImportedSource>[]>> importedContent,
        out KeyValuePair<string, O2JamImportedSource>[] matches)
    {
        matches = [];
        var info = new FileInfo(path);
        if (!info.Exists || !importedContent.TryGetValue(info.Length, out var hashes))
            return false;

        using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        return hashes.TryGetValue(Convert.ToHexString(SHA256.HashData(stream)), out matches!);
    }

    private sealed record PreparedImport(
        string Path,
        O2JamImportedSource? Source,
        O2JamImportPlan? Plan,
        Exception? Exception);

    private sealed record ScannedImport(
        string Path,
        O2JamImportedSource? Source,
        bool WasSkipped,
        Exception? Exception);
}
