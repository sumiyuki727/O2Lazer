using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Import;

namespace osu.Game.Rulesets.O2Lazer.Difficulty;

internal sealed class O2JamLibraryDifficultyPipeline : IDisposable
{
    private readonly Channel<O2JamLibraryDifficultySource> queue = Channel.CreateBounded<O2JamLibraryDifficultySource>(32);
    private readonly HashSet<Guid> scheduled = [];
    private readonly CancellationTokenSource cancellation;
    private readonly Task worker;
    private readonly Action<O2JamLibraryProgress>? progress;
    private readonly Func<IReadOnlyList<O2JamLibraryDifficultySource>, CancellationToken, IReadOnlyList<O2JamLibraryDifficultyOutcome>> processBatch;
    private readonly O2JamLibraryDifficultyProcessor? processor;
    private readonly Stopwatch elapsed = Stopwatch.StartNew();
    private int disposed;
    private bool completed;
    internal HashSet<Guid> FailedSources { get; } = [];
    internal int Completed { get; private set; }

    internal O2JamLibraryDifficultyPipeline(IO2JamLibraryDifficultyStore store, Action<O2JamLibraryProgress>? progress, CancellationToken token)
        : this(new O2JamLibraryDifficultyProcessor(store, "concurrent"), progress, token)
    {
    }

    private O2JamLibraryDifficultyPipeline(O2JamLibraryDifficultyProcessor processor, Action<O2JamLibraryProgress>? progress, CancellationToken token)
        : this(processor.ProcessBatch, progress, token)
    {
        this.processor = processor;
    }

    internal O2JamLibraryDifficultyPipeline(Action<O2JamLibraryDifficultySource, CancellationToken> calculate,
                                          Action<O2JamLibraryProgress>? progress, CancellationToken token)
        : this((sources, cancellation) => processIndividually(sources, calculate, cancellation), progress, token)
    {
    }

    private O2JamLibraryDifficultyPipeline(Func<IReadOnlyList<O2JamLibraryDifficultySource>, CancellationToken, IReadOnlyList<O2JamLibraryDifficultyOutcome>> processBatch,
                                          Action<O2JamLibraryProgress>? progress, CancellationToken token)
    {
        this.processBatch = processBatch;
        this.progress = progress;
        cancellation = CancellationTokenSource.CreateLinkedTokenSource(token);
        worker = Task.Run(run);
    }

    internal void Offer(O2JamLibraryDifficultySource source)
    {
        // A full queue leaves this source for the final pending scan; imports never wait
        // for star calculation and no prepared bytes or Realm models enter the queue.
        if (scheduled.Contains(source.Source.SetId) || !queue.Writer.TryWrite(source))
            return;
        scheduled.Add(source.Source.SetId);
    }

    private async Task run()
    {
        await foreach (var source in queue.Reader.ReadAllAsync(cancellation.Token).ConfigureAwait(false))
        {
            var batch = new List<O2JamLibraryDifficultySource> { source };
            // Drain what is ready without waiting to fill a batch. Only plain identities
            // and numeric results survive until the bounded native cache transaction.
            while (batch.Count < O2JamLibraryDifficultyProcessor.BatchSize && queue.Reader.TryRead(out var next))
                batch.Add(next);
            new O2JamLibraryProgress(Completed, 0, O2JamLibraryStage.CalculatingDifficulties).Publish(progress);
            IReadOnlyList<O2JamLibraryDifficultyOutcome> results;
            try { results = processBatch(batch, cancellation.Token); }
            catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { throw; }
            catch (Exception exception)
            {
                results = batch.ConvertAll(item => new O2JamLibraryDifficultyOutcome(item, Failure: exception));
            }
            foreach (var result in results)
            {
                if (result.Failure != null)
                {
                    FailedSources.Add(result.Source.Source.SetId);
                    Logger.Error(result.Failure, $"O2Jam concurrent difficulty calculation failed for '{result.Source.Path}'.");
                }
                Completed++;
            }
        }
    }

    private static IReadOnlyList<O2JamLibraryDifficultyOutcome> processIndividually(IReadOnlyList<O2JamLibraryDifficultySource> sources,
        Action<O2JamLibraryDifficultySource, CancellationToken> calculate, CancellationToken token)
    {
        var results = new List<O2JamLibraryDifficultyOutcome>();
        foreach (var source in sources)
        {
            token.ThrowIfCancellationRequested();
            try
            {
                calculate(source, token);
                results.Add(new O2JamLibraryDifficultyOutcome(source));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested) { throw; }
            catch (Exception exception) { results.Add(new O2JamLibraryDifficultyOutcome(source, Failure: exception)); }
        }
        return results;
    }

    internal void Complete()
    {
        queue.Writer.TryComplete();
        worker.GetAwaiter().GetResult();
        processor?.LogTimings();
        completed = true;
        Logger.Log($"O2Jam refresh: concurrent difficulty finished; sources={Completed}, failed_sources={FailedSources.Count}, elapsed_ms={elapsed.Elapsed.TotalMilliseconds:F1}.");
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        cancellation.Cancel();
        queue.Writer.TryComplete();
        try { worker.GetAwaiter().GetResult(); }
        catch (OperationCanceledException) when (cancellation.IsCancellationRequested) { }
        if (!completed)
            processor?.LogTimings();
        cancellation.Dispose();
    }
}
