using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Bindables;
using osu.Framework.Graphics;
using osu.Framework.Graphics.Textures;
using osu.Framework.Logging;
using osu.Game.Audio;
using osu.Game.Rulesets.O2Lazer.Formats.Ojm;
using osu.Game.Skinning;

namespace osu.Game.Rulesets.O2Lazer.Audio;

public interface IO2JamPlaybackResource
{
    bool IsReadyForScheduling => true;

    bool ContainsSample(int sampleId);

    bool TryGetAutomaticSampleStreaming(int sampleId, out bool streamed)
    {
        streamed = false;
        return true;
    }

    bool IsSampleReady(int sampleId) => true;

    bool IsBackgroundTrackReady(int sampleId) => true;

    void PrefetchBackgroundTrack(int sampleId) => _ = IsBackgroundTrackReady(sampleId);

    void PrefetchSample(int sampleId) => _ = IsSampleReady(sampleId);

    ISample? GetSample(ISampleInfo sampleInfo);

    Track? GetBackgroundTrack(int sampleId);
}

/// <summary>
/// A sample-only beatmap skin. Visual and configuration lookups deliberately fall through to the selected mania skin.
/// </summary>
public sealed class O2JamBeatmapSkin : ISkin, IO2JamPlaybackResource, IO2JamPlaybackLeaseSource, IDisposable
{
    private const long streamed_automatic_sample_threshold = 512 * 1024;
    private const int concurrent_preloads = 4;

    private static readonly O2JamPreloadScheduler preloads = new(concurrent_preloads);

    private readonly AudioManager audioManager;
    private readonly Task<OjmArchive> archiveTask;
    private readonly object leaseLock = new();
    private readonly object resourceLock = new();
    private readonly CancellationTokenSource preloadCancellation = new();
    private readonly Dictionary<int, O2JamPreloadScheduler.Preparation<Track?>> prefetchedBackgroundTracks = [];
    private readonly Dictionary<int, O2JamPreloadScheduler.Preparation<Sample>> prefetchedSamples = [];
    private readonly HashSet<int> requestedBackgroundTracks = [];
    private readonly HashSet<int> requestedSamples = [];
    private readonly HashSet<int> requestedAutomaticSamples = [];
    private readonly HashSet<int> urgentBackgroundTracks = [];
    private readonly HashSet<int> urgentSamples = [];

    private OjmArchive? archive;
    private ISampleStore? samples;
    private ITrackStore? backgroundTracks;
    private O2JamDetachedAudioHost? audioHost;

    private int playbackLeases;
    private bool disposeRequested;
    private bool resourcesDisposed;
    private bool archiveCompletionObserved;

    public O2JamBeatmapSkin(OjmArchive archive, AudioManager audioManager)
        : this(Task.FromResult(archive), audioManager)
    {
    }

    internal O2JamBeatmapSkin(Task<OjmArchive> archiveTask, AudioManager audioManager)
    {
        this.audioManager = audioManager;
        this.archiveTask = archiveTask;
    }

    public Drawable? GetDrawableComponent(ISkinComponentLookup lookup) => null;

    public Texture? GetTexture(string componentName, WrapMode wrapModeS, WrapMode wrapModeT) => null;

    public bool ContainsSample(int sampleId) => tryEnsureResources() && archive!.Samples.ContainsKey(sampleId);

    public bool IsReadyForScheduling => tryEnsureResources();

    public bool TryGetAutomaticSampleStreaming(int sampleId, out bool streamed)
    {
        streamed = false;
        lock (resourceLock)
        {
            if (!tryEnsureResources())
                return false;

            if (archive!.TryGetSample(sampleId, out var sample))
                streamed = sample.ByteLength >= streamed_automatic_sample_threshold;

            return true;
        }
    }

    public bool IsSampleReady(int sampleId)
    {
        PrefetchSamples([sampleId], true);

        lock (resourceLock)
        {
            if (!tryEnsureResources())
                return false;

            return !archive!.Samples.ContainsKey(sampleId)
                   || prefetchedSamples.TryGetValue(sampleId, out var preparation)
                   && preparation.Task.IsCompleted
                   && (!preparation.Task.IsCompletedSuccessfully || preparation.Task.Result?.IsLoaded != false);
        }
    }

    public bool IsBackgroundTrackReady(int sampleId)
    {
        PrefetchBackgroundTracks([sampleId], true);

        lock (resourceLock)
        {
            if (!tryEnsureResources())
                return false;

            return !archive!.Samples.ContainsKey(sampleId)
                   || prefetchedBackgroundTracks.TryGetValue(sampleId, out var preparation) && preparation.Task.IsCompleted;
        }
    }

    public void PrefetchBackgroundTrack(int sampleId) => PrefetchBackgroundTracks([sampleId]);

    public void PrefetchSample(int sampleId) => PrefetchSamples([sampleId]);

    public ISample? GetSample(ISampleInfo sampleInfo)
    {
        ensureResources();

        if (sampleInfo is O2JamHitSampleInfo o2Sample)
        {
            // Gameplay skin lookup must retain the sample even if native decoding is still queued.
            // Returning null here would cache permanent silence in the drawable hit object.
            PrefetchSamples([o2Sample.SampleId], true);

            O2JamPreloadScheduler.Preparation<Sample>? prefetched;
            lock (resourceLock)
                prefetchedSamples.TryGetValue(o2Sample.SampleId, out prefetched);

            if (prefetched != null)
            {
                try
                {
                    var prepared = prefetched.Task.GetAwaiter().GetResult();
                    // Native DrawableSample owns and disposes the sample returned by skin lookup.
                    // Keep the readiness probe private; SampleStore.Get creates an independent
                    // instance backed by the already-preloaded factory, without decoding again.
                    return prepared == null ? null : samples!.Get(prepared.Name);
                }
                catch (Exception exception)
                {
                    Logger.Error(exception, $"O2Lazer could not prepare OJM sample {o2Sample.SampleId}.");
                    return null;
                }
            }
        }

        foreach (var lookup in sampleInfo.LookupNames)
        {
            var sample = samples!.Get(lookup);
            if (sample != null)
                return sample;
        }

        return null;
    }

    public Track? GetBackgroundTrack(int sampleId)
    {
        if (!IsBackgroundTrackReady(sampleId))
            return null;

        O2JamPreloadScheduler.Preparation<Track?>? prefetched;
        lock (resourceLock)
        {
            prefetchedBackgroundTracks.Remove(sampleId, out prefetched);
        }

        try
        {
            return prefetched?.Task.GetAwaiter().GetResult();
        }
        catch (Exception exception)
        {
            Logger.Error(exception, $"O2Lazer could not prepare OJM background track {sampleId}.");
            return null;
        }
    }

    internal void PrefetchBackgroundTracks(IEnumerable<int> sampleIds, bool prioritise = false)
    {
        lock (resourceLock)
        {
            foreach (var sampleId in sampleIds)
            {
                requestedBackgroundTracks.Add(sampleId);
                if (prioritise)
                    urgentBackgroundTracks.Add(sampleId);
            }

            if (tryEnsureResources())
                startRequestedPrefetches();
        }
    }

    internal void PrefetchSamples(IEnumerable<int> sampleIds, bool prioritise = false)
    {
        lock (resourceLock)
        {
            foreach (var sampleId in sampleIds)
            {
                requestedSamples.Add(sampleId);
                if (prioritise)
                    urgentSamples.Add(sampleId);
            }

            if (tryEnsureResources())
                startRequestedPrefetches();
        }
    }

    internal void PrefetchPreview(O2JamPreviewSchedule schedule, double endTime)
    {
        lock (resourceLock)
        {
            var startupEnd = schedule.PreviewEvents.FirstOrDefault(evt => evt.Volume > 0).Time + 500;
            foreach (var evt in schedule.BackgroundEvents.TakeWhile(evt => evt.Time <= endTime))
            {
                requestedBackgroundTracks.Add(evt.SampleId);
                if (evt.Time <= startupEnd)
                    urgentBackgroundTracks.Add(evt.SampleId);
            }

            foreach (var evt in schedule.PreviewEvents.TakeWhile(evt => evt.Time <= endTime).Where(evt => evt.IsKeySound))
            {
                if (evt.IsAutomatic)
                    requestedAutomaticSamples.Add(evt.SampleId);
                else
                    requestedSamples.Add(evt.SampleId);

                if (evt.Time <= startupEnd)
                {
                    urgentSamples.Add(evt.SampleId);
                    if (evt.IsAutomatic)
                        urgentBackgroundTracks.Add(evt.SampleId);
                }
            }

            observeArchiveCompletion();

            if (tryEnsureResources())
                startRequestedPrefetches();
        }
    }

    public IBindable<TValue>? GetConfig<TLookup, TValue>(TLookup lookup)
        where TLookup : notnull
        where TValue : notnull => null;

    IDisposable IO2JamPlaybackLeaseSource.AcquirePlaybackLease()
    {
        lock (leaseLock)
        {
            ObjectDisposedException.ThrowIf(disposeRequested || resourcesDisposed, this);
            playbackLeases++;
        }

        return new PlaybackLease(this);
    }

    public void Dispose()
    {
        lock (leaseLock)
            disposeRequested = true;

        tryDisposeResources();
    }

    private void releasePlaybackLease()
    {
        lock (leaseLock)
            playbackLeases--;

        tryDisposeResources();
    }

    private void tryDisposeResources()
    {
        lock (resourceLock)
        {
            lock (leaseLock)
            {
                if (!disposeRequested || playbackLeases > 0 || resourcesDisposed)
                    return;

                resourcesDisposed = true;
            }

            preloadCancellation.Cancel();
            backgroundTracks?.Dispose();
            audioHost?.Dispose();
            preloadCancellation.Dispose();
        }

        GC.KeepAlive(audioManager);
    }

    private bool tryEnsureResources()
    {
        lock (resourceLock)
        {
            lock (leaseLock)
            {
                if (resourcesDisposed)
                    return false;
            }

            if (backgroundTracks != null)
                return true;

            if (!archiveTask.IsCompleted)
                return false;

            try
            {
                archive = archiveTask.GetAwaiter().GetResult();
            }
            catch (Exception exception)
            {
                Logger.Error(exception, "O2Lazer could not index the selected OJM archive.");
                archive = new OjmArchive(new Dictionary<int, OjmSample>());
            }

            initialiseResourceStores();
            startRequestedPrefetches();
            return true;
        }
    }

    private void ensureResources()
    {
        if (tryEnsureResources())
            return;

        // This fallback is for gameplay skin lookups. Preview playback always checks readiness
        // first, so it never waits for archive I/O on the audio thread.
        try
        {
            archiveTask.GetAwaiter().GetResult();
        }
        catch
        {
            // tryEnsureResources logs the failure and supplies the empty archive.
        }

        if (!tryEnsureResources())
            throw new ObjectDisposedException(nameof(O2JamBeatmapSkin));
    }

    private void initialiseResourceStores()
    {
        if (backgroundTracks != null)
            return;

        var resources = new O2JamArchiveResourceStore(archive!);
        samples = audioManager.GetSampleStore(resources, audioManager.TrackMixer);
        samples.AddExtension("ogg");
        samples.PlaybackConcurrency = 32;
        backgroundTracks = audioManager.GetTrackStore(resources, audioManager.TrackMixer);

        // GetSampleStore() normally nests the store below the global sample store. Move it to a
        // non-adjustable host so O2Jam music layers never inherit FrameworkSetting.VolumeEffect.
        var parent = (AudioCollectionManager<AdjustableAudioComponent>)audioManager.Samples;
        parent.RemoveItem((AdjustableAudioComponent)samples);
        samples.AddAdjustment(AdjustableProperty.Volume, audioManager.Volume);
        samples.AddAdjustment(AdjustableProperty.Volume, audioManager.VolumeTrack);
        audioManager.AddItem(audioHost = new O2JamDetachedAudioHost((AudioComponent)samples));
    }

    private void startRequestedPrefetches()
    {
        if (backgroundTracks == null || samples == null)
            return;

        classifyAutomaticSamples();

        foreach (var sampleId in requestedBackgroundTracks)
        {
            if (!archive!.Samples.ContainsKey(sampleId))
                continue;

            if (prefetchedBackgroundTracks.TryGetValue(sampleId, out var existing))
            {
                if (urgentBackgroundTracks.Contains(sampleId))
                    existing.Prioritise();
                continue;
            }

            var started = Stopwatch.GetTimestamp();
            var preparation = preloads.Schedule(
                token => O2JamTrackPreparation.LoadAsync(backgroundTracks, $"o2jam/{sampleId}", token),
                preloadCancellation.Token, urgentBackgroundTracks.Contains(sampleId));
            prefetchedBackgroundTracks[sampleId] = preparation;
            observeFailure(preparation.Task);
            logPreparation(preparation.Task, "background ready", sampleId, started);
        }
        requestedBackgroundTracks.Clear();
        urgentBackgroundTracks.Clear();

        foreach (var sampleId in requestedSamples)
        {
            if (!archive!.Samples.ContainsKey(sampleId))
                continue;

            if (prefetchedSamples.TryGetValue(sampleId, out var existing))
            {
                if (urgentSamples.Contains(sampleId))
                    existing.Prioritise();
                continue;
            }

            var started = Stopwatch.GetTimestamp();
            var preparation = preloads.Schedule(token => samples.GetAsync($"o2jam/{sampleId}", token),
                preloadCancellation.Token, urgentSamples.Contains(sampleId));
            prefetchedSamples[sampleId] = preparation;
            observeFailure(preparation.Task);
            logPreparation(preparation.Task, "keysound", sampleId, started);
        }
        requestedSamples.Clear();
        urgentSamples.Clear();
    }

    private void classifyAutomaticSamples()
    {
        foreach (var sampleId in requestedAutomaticSamples)
        {
            if (!archive!.TryGetSample(sampleId, out var sample))
                continue;

            if (sample.ByteLength >= streamed_automatic_sample_threshold)
                requestedBackgroundTracks.Add(sampleId);
            else
                requestedSamples.Add(sampleId);
        }

        requestedAutomaticSamples.Clear();
    }

    private void observeArchiveCompletion()
    {
        if (archiveCompletionObserved)
            return;

        archiveCompletionObserved = true;
        _ = archiveTask.ContinueWith(
            completedTask =>
            {
                lock (leaseLock)
                {
                    if (disposeRequested || resourcesDisposed)
                        return;
                }

                _ = tryEnsureResources();
            },
            CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously,
            TaskScheduler.Default);
    }

    private static void observeFailure<T>(Task<T> task) => _ = task.ContinueWith(
        completed => _ = completed.Exception,
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private static void logPreparation<T>(Task<T> task, string kind, int sampleId, long started) => _ = task.ContinueWith(
        _ => Logger.Log(
            $"O2Lazer prepared OJM {kind} {sampleId} in {Stopwatch.GetElapsedTime(started).TotalMilliseconds:N1} ms.",
            level: LogLevel.Verbose),
        CancellationToken.None,
        TaskContinuationOptions.OnlyOnRanToCompletion | TaskContinuationOptions.ExecuteSynchronously,
        TaskScheduler.Default);

    private sealed class PlaybackLease(O2JamBeatmapSkin owner) : IDisposable
    {
        private O2JamBeatmapSkin? owner = owner;

        public void Dispose() => Interlocked.Exchange(ref owner, null)?.releasePlaybackLease();
    }
}
