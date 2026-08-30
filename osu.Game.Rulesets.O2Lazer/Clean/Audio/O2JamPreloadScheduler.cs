using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace osu.Game.Rulesets.O2Lazer.Audio;

/// <summary>
/// Keeps imminent audio ahead of speculative lookahead without starting duplicate decoders.
/// </summary>
internal sealed class O2JamPreloadScheduler(int concurrency)
{
    private readonly object sync = new();
    private readonly Queue<Job> urgent = new();
    private readonly Queue<Job> normal = new();
    private int running;

    internal Preparation<T> Schedule<T>(Func<CancellationToken, Task<T>> load, CancellationToken token, bool prioritise)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new Job(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                completion.TrySetResult(await load(token).ConfigureAwait(false));
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                completion.TrySetCanceled(token);
            }
            catch (Exception exception)
            {
                completion.TrySetException(exception);
            }
        });

        lock (sync)
        {
            job.Urgent = prioritise;
            (prioritise ? urgent : normal).Enqueue(job);
            dispatch();
        }

        return new Preparation<T>(completion.Task, () => promote(job));
    }

    private void promote(Job job)
    {
        lock (sync)
        {
            if (job.Started || job.Urgent)
                return;

            job.Urgent = true;
            urgent.Enqueue(job);
            dispatch();
        }
    }

    private void dispatch()
    {
        while (running < concurrency && (urgent.Count > 0 || normal.Count > 0))
        {
            var job = (urgent.Count > 0 ? urgent : normal).Dequeue();
            if (job.Started)
                continue;

            job.Started = true;
            running++;
            // Native Track operations must be queued from a worker, never run inline on the audio thread.
            _ = Task.Run(async () =>
            {
                try
                {
                    await job.Load().ConfigureAwait(false);
                }
                finally
                {
                    lock (sync)
                    {
                        running--;
                        dispatch();
                    }
                }
            });
        }
    }

    private sealed class Job(Func<Task> load)
    {
        public Func<Task> Load { get; } = load;
        public bool Started { get; set; }
        public bool Urgent { get; set; }
    }

    internal sealed class Preparation<T>(Task<T> task, Action prioritise)
    {
        public Task<T> Task { get; } = task;
        public void Prioritise() => prioritise();
    }
}
