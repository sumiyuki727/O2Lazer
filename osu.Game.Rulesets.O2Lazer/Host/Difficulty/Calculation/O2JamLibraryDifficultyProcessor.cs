using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Threading;
using O2Jam.Formats.Ojn;
using osu.Framework.Logging;
using osu.Game.Rulesets.Mania.Beatmaps;
using osu.Game.Rulesets.O2Lazer.Import;
using osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

internal sealed record O2JamLibraryDifficultySource(string Path, O2JamImportedSource Source);

internal interface IO2JamLibraryDifficultyStore
{
    IReadOnlyList<O2JamLibraryDifficultySource> GetPendingDifficultySources(string path, CancellationToken cancellationToken);
    O2JamLibraryDifficultySource RefreshDifficultySource(O2JamLibraryDifficultySource source, CancellationToken cancellationToken);
    byte[] ReadDifficultySource(O2JamLibraryDifficultySource source);
    void WriteDifficulties(O2JamLibraryDifficultySource source, IReadOnlyList<O2JamImportDifficultyCache> difficulties, CancellationToken cancellationToken);
}

internal readonly record struct O2JamLibraryDifficultySourceCheck(O2JamLibraryDifficultySource Source, Exception? Failure = null);
internal sealed record O2JamLibraryDifficultyCacheWrite(O2JamLibraryDifficultySource Source, IReadOnlyList<O2JamImportDifficultyCache> Difficulties);
internal readonly record struct O2JamLibraryDifficultyOutcome(O2JamLibraryDifficultySource Source, int WrittenSlots = 0, Exception? Failure = null);

internal interface IO2JamLibraryDifficultyBatchStore : IO2JamLibraryDifficultyStore
{
    // Results preserve input order. Identity failures belong to one source; a transaction
    // failure or cancellation aborts the write batch and is thrown to its caller.
    IReadOnlyList<O2JamLibraryDifficultySourceCheck> RefreshDifficultySources(IReadOnlyList<O2JamLibraryDifficultySource> sources, CancellationToken cancellationToken);
    IReadOnlyList<O2JamLibraryDifficultyOutcome> WriteDifficultyBatch(IReadOnlyList<O2JamLibraryDifficultyCacheWrite> writes, CancellationToken cancellationToken);
}

/// <summary>
/// Calculates versioned native mania attributes from committed source snapshots.
/// </summary>
internal sealed class O2JamLibraryDifficultyProcessor(IO2JamLibraryDifficultyStore store, string phase = "deferred")
{
    internal const int BatchSize = 16;
    private readonly Stopwatch cacheReadTime = new();
    private readonly Stopwatch sourceReadTime = new();
    private readonly Stopwatch decodeTime = new();
    private readonly Stopwatch calculateTime = new();
    private readonly Stopwatch cacheWriteTime = new();
    private int processedBatches;
    private int attemptedSources;

    public int Process(string path, Action<O2JamLibraryProgress>? progress, CancellationToken cancellationToken,
                       int completed = 0, IReadOnlySet<Guid>? failedDuringImport = null)
    {
        var elapsed = Stopwatch.StartNew();
        new O2JamLibraryProgress(completed, 0, O2JamLibraryStage.CalculatingDifficulties).Publish(progress);
        var sources = store.GetPendingDifficultySources(path, cancellationToken)
            .Where(source => failedDuringImport?.Contains(source.Source.SetId) != true).ToArray();
        var failed = failedDuringImport?.Count ?? 0;
        var calculated = 0;
        var skipped = 0;
        new O2JamLibraryProgress(completed, completed + sources.Length, O2JamLibraryStage.CalculatingDifficulties).Publish(progress);
        Logger.Log($"O2Jam refresh: calculating missing mania difficulty for {sources.Length} stored sources after collection synchronisation; completed_during_import={completed}.");
        for (var offset = 0; offset < sources.Length; offset += BatchSize)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var batch = sources.Skip(offset).Take(BatchSize).ToArray();
            var results = ProcessBatch(batch, cancellationToken);
            foreach (var result in results)
            {
                if (result.Failure != null)
                {
                    failed++;
                    Logger.Error(result.Failure, $"O2Jam deferred difficulty calculation failed for '{result.Source.Path}'.");
                }
                else if (result.WrittenSlots == 0)
                    skipped++;
                else
                    calculated += result.WrittenSlots;
            }
            new O2JamLibraryProgress(completed + offset + batch.Length, completed + sources.Length, O2JamLibraryStage.CalculatingDifficulties).Publish(progress);
        }
        cancellationToken.ThrowIfCancellationRequested();
        LogTimings();
        Logger.Log($"O2Jam refresh: deferred difficulty finished; sources={sources.Length}, calculated_slots={calculated}, skipped_sources={skipped}, failed_sources={failed}, elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F1}.");
        return failed;
    }

    internal IReadOnlyList<O2JamLibraryDifficultyOutcome> ProcessBatch(IReadOnlyList<O2JamLibraryDifficultySource> sources, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (sources.Count > BatchSize)
            throw new ArgumentOutOfRangeException(nameof(sources));
        if (sources.Count == 0)
            return [];
        processedBatches++;
        attemptedSources += sources.Count;
        try { return processBatch(sources, cancellationToken); }
        finally
        {
            if (processedBatches == 1 || processedBatches % 16 == 0)
                LogTimings();
        }
    }

    private IReadOnlyList<O2JamLibraryDifficultyOutcome> processBatch(IReadOnlyList<O2JamLibraryDifficultySource> sources, CancellationToken cancellationToken)
    {
        var results = sources.Select(source => new O2JamLibraryDifficultyOutcome(source)).ToArray();
        IReadOnlyList<O2JamLibraryDifficultySourceCheck> checks;
        cacheReadTime.Start();
        try
        {
            checks = store is IO2JamLibraryDifficultyBatchStore batchStore
                ? batchStore.RefreshDifficultySources(sources, cancellationToken)
                : sources.Select(source => checkSource(source, cancellationToken)).ToArray();
            if (checks.Count != sources.Count)
                throw new InvalidOperationException("The difficulty cache read batch returned an incomplete result.");
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception) { return sources.Select(source => new O2JamLibraryDifficultyOutcome(source, Failure: exception)).ToArray(); }
        finally { cacheReadTime.Stop(); }

        var writes = new List<O2JamLibraryDifficultyCacheWrite>();
        var writeIndices = new List<int>();
        for (var index = 0; index < checks.Count; index++)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var check = checks[index];
            results[index] = new O2JamLibraryDifficultyOutcome(check.Source, Failure: check.Failure);
            if (check.Failure != null)
                continue;
            try
            {
                // Each call releases its OJN/document before the next source. Only numeric
                // results and plain identity snapshots survive until the bounded commit.
                var difficulties = calculateSource(check.Source, cancellationToken);
                if (difficulties.Count == 0)
                    continue;
                writes.Add(new O2JamLibraryDifficultyCacheWrite(check.Source, difficulties));
                writeIndices.Add(index);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
            catch (Exception exception) { results[index] = new O2JamLibraryDifficultyOutcome(check.Source, Failure: exception); }
        }
        if (writes.Count == 0)
            return results;

        cacheWriteTime.Start();
        try
        {
            var outcomes = store is IO2JamLibraryDifficultyBatchStore batchStore
                ? batchStore.WriteDifficultyBatch(writes, cancellationToken)
                : writes.Select(write => writeSource(write, cancellationToken)).ToArray();
            if (outcomes.Count != writes.Count)
                throw new InvalidOperationException("The difficulty cache write batch returned an incomplete result.");
            for (var index = 0; index < outcomes.Count; index++)
                results[writeIndices[index]] = outcomes[index];
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { throw; }
        catch (Exception exception)
        {
            // Native writes are atomic. Leave every affected source for the next refresh,
            // without retrying a failed transaction source by source in this round.
            foreach (var index in writeIndices)
                results[index] = new O2JamLibraryDifficultyOutcome(results[index].Source, Failure: exception);
        }
        finally { cacheWriteTime.Stop(); }
        return results;
    }

    private O2JamLibraryDifficultySourceCheck checkSource(O2JamLibraryDifficultySource source, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try { return new O2JamLibraryDifficultySourceCheck(store.RefreshDifficultySource(source, token)); }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new O2JamLibraryDifficultySourceCheck(source, exception); }
    }

    private O2JamLibraryDifficultyOutcome writeSource(O2JamLibraryDifficultyCacheWrite write, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        try
        {
            store.WriteDifficulties(write.Source, write.Difficulties, token);
            return new O2JamLibraryDifficultyOutcome(write.Source, write.Difficulties.Count);
        }
        catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
        catch (Exception exception) { return new O2JamLibraryDifficultyOutcome(write.Source, Failure: exception); }
    }

    private IReadOnlyList<O2JamImportDifficultyCache> calculateSource(O2JamLibraryDifficultySource source, CancellationToken cancellationToken)
    {
        var missing = source.Source.DifficultyIdentities!
            .Where(identity => source.Source.ManiaCache?.Any(cache => cache.Difficulty == identity.Difficulty) != true).ToArray();
        if (missing.Length == 0)
            return [];
        byte[] data;
        sourceReadTime.Start();
        try
        {
            data = store.ReadDifficultySource(source);
            if (!string.Equals(Convert.ToHexString(SHA256.HashData(data)), source.Source.SourceHash, StringComparison.OrdinalIgnoreCase))
                throw new InvalidDataException("The stored OJN no longer matches the imported source.");
        }
        finally { sourceReadTime.Stop(); }
        OjnDocument document;
        decodeTime.Start();
        try
        {
            document = new OjnReader(OjnMetadataEncoding.Automatic,
                source.Source.EncodingFallback is { } encoding ? () => encoding : null).Read(data);
        }
        finally { decodeTime.Stop(); }
        var difficulties = new List<O2JamImportDifficultyCache>();
        foreach (var identity in missing)
        {
            cancellationToken.ThrowIfCancellationRequested();
            decodeTime.Start();
            ManiaBeatmap beatmap;
            try { beatmap = new OjnBeatmapFactory().CreateDifficultyProjection(document, identity.Difficulty); }
            finally { decodeTime.Stop(); }
            calculateTime.Start();
            try
            {
                var attributes = O2JamManiaStarRating.CalculateBaselineAttributes(beatmap, cancellationToken);
                difficulties.Add(new O2JamImportDifficultyCache(identity.Difficulty, attributes.StarRating, attributes.MaxCombo, O2JamManiaStarRating.CacheVersion));
            }
            finally { calculateTime.Stop(); }
        }
        return difficulties;
    }

    internal void LogTimings() => Logger.Log(FormattableString.Invariant(
        $"O2Jam difficulty timings: phase={phase}, projection_mode=native-mania-v2, cache_mode={(store is IO2JamLibraryDifficultyBatchStore ? "batch-v1" : "single-v1")}, batches={processedBatches}, attempted_sources={attemptedSources}, batch_limit={BatchSize}, cache_read_ms={cacheReadTime.Elapsed.TotalMilliseconds:F1}, source_read_hash_ms={sourceReadTime.Elapsed.TotalMilliseconds:F1}, decode_ms={decodeTime.Elapsed.TotalMilliseconds:F1}, calculate_ms={calculateTime.Elapsed.TotalMilliseconds:F1}, cache_write_ms={cacheWriteTime.Elapsed.TotalMilliseconds:F1}."));
}
