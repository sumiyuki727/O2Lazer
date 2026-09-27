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
    private readonly LinkedList<Job> urgent = new();
    private readonly LinkedList<Job> normal = new();
    private int running;

    internal Preparation<T> Schedule<T>(Func<CancellationToken, Task<T>> load, CancellationToken token, bool prioritise)
    {
        var completion = new TaskCompletionSource<T>(TaskCreationOptions.RunContinuationsAsynchronously);
        var job = new Job(async () =>
        {
            try
            {
                token.ThrowIfCancellationRequested();
                var result = await load(token).ConfigureAwait(false);
                if (!completion.TrySetResult(result) && result is IDisposable disposable)
                {
                    // A selection can be cancelled after native decoding starts; its result
                    // no longer has a consumer to own and release it.
                    disposable.Dispose();
                }
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
            job.Node = (prioritise ? urgent : normal).AddLast(job);
            dispatch();
        }

        var cancellation = token.Register(() =>
        {
            lock (sync)
            {
                job.Node?.List?.Remove(job.Node);
                job.Node = null;
            }

            completion.TrySetCanceled(token);
        });
        _ = completion.Task.ContinueWith(
            _ => cancellation.Dispose(),
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);

        return new Preparation<T>(completion.Task, () => promote(job));
    }

    private void promote(Job job)
    {
        lock (sync)
        {
            if (job.Started || job.Node?.List != normal)
                return;

            normal.Remove(job.Node);
            job.Node = urgent.AddLast(job);
            dispatch();
        }
    }

    private void dispatch()
    {
        while (running < concurrency && (urgent.Count > 0 || normal.Count > 0))
        {
            var queue = urgent.Count > 0 ? urgent : normal;
            var job = queue.First!.Value;
            queue.RemoveFirst();
            job.Node = null;

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
        public LinkedListNode<Job>? Node { get; set; }
    }

    internal sealed class Preparation<T>(Task<T> task, Action prioritise)
    {
        public Task<T> Task { get; } = task;
        public void Prioritise() => prioritise();
    }
}
