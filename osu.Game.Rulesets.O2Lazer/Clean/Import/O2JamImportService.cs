using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Game.Rulesets.O2Lazer.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Import;

public sealed record O2JamImportSummary(int Imported, int Updated, int AlreadyPresent, int Failed, bool RulesetUnavailable);

public sealed class O2JamImportService(O2JamImportPlanner planner, O2JamLibraryWriter writer)
{
    private const int refresh_batch_size = 8;

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
        var processed = 0;

        progress?.Invoke(0, paths.Count);

        for (var offset = 0; offset < paths.Count; offset += refresh_batch_size)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var count = Math.Min(refresh_batch_size, paths.Count - offset);
            var prepared = new PreparedImport[count];

            Parallel.For(0, count, new ParallelOptions
            {
                MaxDegreeOfParallelism = refresh_batch_size,
                CancellationToken = cancellationToken,
            }, index =>
            {
                var path = paths[offset + index];

                try
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    importedSources.TryGetValue(Path.GetFullPath(path), out var source);
                    if (source != null && isUnchanged(path, source))
                    {
                        prepared[index] = new PreparedImport(path, source, null, null, true);
                        return;
                    }

                    prepared[index] = new PreparedImport(path, source, planner.Create(path), null, false);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception exception)
                {
                    prepared[index] = new PreparedImport(path, null, null, exception, false);
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
                else if (item.WasSkipped)
                    existing++;
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

                progress?.Invoke(++processed, paths.Count);
            }
        }

        cancellationToken.ThrowIfCancellationRequested();
        writer.MarkMissingSources();

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

    private sealed record PreparedImport(
        string Path,
        O2JamImportedSource? Source,
        O2JamImportPlan? Plan,
        Exception? Exception,
        bool WasSkipped);
}
