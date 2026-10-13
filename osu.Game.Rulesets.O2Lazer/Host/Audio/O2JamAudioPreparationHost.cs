using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;

namespace osu.Game.Rulesets.O2Lazer.Audio;

/// <summary>
/// Keeps preload slots occupied until native preparation has run, without delaying Sample lookup.
/// </summary>
internal sealed class O2JamAudioPreparationHost(ISampleStore samples, ITrackStore tracks) : AudioComponent
{
    private readonly object gate = new();
    private readonly List<Pending> pending = [];
    private bool closing;
    private bool stopping;
    private bool nativeDrainFinished;

    internal Task<Sample?> PrepareSampleAsync(string name, CancellationToken token, TaskCompletionSource<Sample?> acquired)
        => begin<Sample>(async () => await samples.GetAsync(name, token).ConfigureAwait(false), token, acquired, null);

    internal Task<Track?> PrepareTrackAsync(string name, CancellationToken token)
        => begin<Track>(() => Task.Run(() => (Track?)tracks.Get(name), token), token, null, track => track.StopAsync());

    private Task<T?> begin<T>(Func<Task<T?>> acquire, CancellationToken token,
                              TaskCompletionSource<T?>? acquired, Func<T, Task>? fence) where T : AudioComponent
    {
        lock (gate)
        {
            token.ThrowIfCancellationRequested();
            ObjectDisposedException.ThrowIf(closing || IsDisposed, this);
            var request = new Pending<T>(acquire(), token, acquired, fence);
            pending.Add(request);
            _ = request.Getter.ContinueWith(_ =>
            {
                lock (gate)
                {
                    request.Publish(stopping);
                    // A late getter cannot enqueue decoding in a store already disposed by native shutdown.
                    if (stopping ? nativeDrainFinished : request.CanFinishWithoutSampleUpdate)
                        finish(request);
                }
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
            return request.Completion.Task;
        }
    }

    internal void Close()
    {
        lock (gate)
        {
            closing = true;
            if (pending.Count == 0)
                Dispose();
        }
    }

    protected override void UpdateChildren()
    {
        base.UpdateChildren();
        lock (gate)
        {
            // A completed Get queued its factory before publication. Capture publication before
            // updating the store so a getter finishing mid-update cannot pass the fence too early.
            var snapshot = pending.Count == 0 ? Array.Empty<Pending>() : pending.Where(request => request.Published).ToArray();
            var sampleComponent = (AudioComponent)samples;
            if (sampleComponent.IsAlive)
                sampleComponent.Update();

            if (stopping && ((AudioComponent)tracks).IsAlive)
                ((AudioComponent)tracks).Update();

            if (stopping)
                nativeDrainFinished = true;

            foreach (var request in snapshot)
            {
                if (stopping || request.NeedsSampleUpdate || request.CanFinishWithoutSampleUpdate)
                    finish(request);
            }
        }
    }

    private void finish(Pending request)
    {
        if (!pending.Remove(request))
            return;
        request.Finish(stopping);
        if (closing && pending.Count == 0 && !IsDisposed)
            Dispose();
    }

    protected override void Dispose(bool disposing)
    {
        lock (gate)
        {
            closing = stopping = true;
            foreach (var request in pending)
                request.CancelLookup();
            ((AudioComponent)samples).Dispose();
            ((AudioComponent)tracks).Dispose();
            base.Dispose(disposing);
        }
    }

    private abstract class Pending
    {
        internal abstract Task Getter { get; }
        internal abstract bool Published { get; }
        internal abstract bool NeedsSampleUpdate { get; }
        internal abstract bool CanFinishWithoutSampleUpdate { get; }
        internal abstract void Publish(bool stopping);
        internal abstract void Finish(bool stopping);
        internal abstract void CancelLookup();
    }

    private sealed class Pending<T>(Task<T?> getter, CancellationToken token,
                                    TaskCompletionSource<T?>? acquired, Func<T, Task>? createFence) : Pending where T : AudioComponent
    {
        internal readonly TaskCompletionSource<T?> Completion = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private T? value;
        private Task? fence;
        private Exception? failure;
        private bool published;

        internal override Task Getter => getter;
        internal override bool Published => published;
        internal override bool NeedsSampleUpdate => published && failure == null && value != null && createFence == null;
        internal override bool CanFinishWithoutSampleUpdate => published &&
            (failure != null || value == null || fence?.IsCompleted == true);

        internal override void Publish(bool stopping)
        {
            if (published)
                return;
            published = true;
            try
            {
                value = getter.GetAwaiter().GetResult();
                if (stopping)
                    CancelLookup();
                else
                {
                    acquired?.TrySetResult(value);
                    if (value != null && createFence != null)
                        fence = createFence(value);
                }
            }
            catch (Exception exception)
            {
                failure = exception;
                if (exception is OperationCanceledException && token.IsCancellationRequested)
                    acquired?.TrySetCanceled(token);
                else
                    acquired?.TrySetException(exception);
            }
        }

        internal override void Finish(bool stopping)
        {
            if (stopping)
            {
                value?.Dispose();
                Completion.TrySetCanceled(new CancellationToken(true));
                return;
            }
            try
            {
                if (failure != null)
                    System.Runtime.ExceptionServices.ExceptionDispatchInfo.Throw(failure);
                fence?.GetAwaiter().GetResult();
                // Invalid samples have now had a native preparation attempt. Retain their factory
                // for native device recovery; waiting on IsLoaded forever would stall the clock.
                if (value is Track track && (!track.IsLoaded || track.IsDisposed))
                {
                    track.Dispose();
                    value = null;
                }
                Completion.TrySetResult(value);
            }
            catch (OperationCanceledException) when (token.IsCancellationRequested)
            {
                value?.Dispose();
                Completion.TrySetCanceled(token);
            }
            catch (Exception exception)
            {
                value?.Dispose();
                Completion.TrySetException(exception);
            }
        }

        internal override void CancelLookup() => acquired?.TrySetCanceled(new CancellationToken(true));
    }
}
