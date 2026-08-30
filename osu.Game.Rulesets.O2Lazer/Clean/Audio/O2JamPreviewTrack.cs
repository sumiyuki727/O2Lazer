using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using osu.Framework.Audio;
using osu.Framework.Audio.Sample;
using osu.Framework.Audio.Track;
using osu.Framework.Extensions;
using osu.Framework.Logging;
using osu.Game.Rulesets.O2Lazer.Beatmaps;

namespace osu.Game.Rulesets.O2Lazer.Audio;

public enum O2JamPreviewPlaybackMode
{
    Preview,
    Gameplay,
}

internal interface IO2JamPlaybackLeaseSource
{
    IDisposable AcquirePlaybackLease();
}

/// <summary>
/// Uses the same clock for song-select preview and gameplay while making playable keysounds automatic only in preview.
/// </summary>
public sealed partial class O2JamPreviewTrack : Track
{
    private const double allowable_late_start = 100;

    private readonly Track clock;
    private readonly IO2JamPlaybackResource resources;
    private readonly IDisposable? resourceLease;
    private O2JamPreviewSchedule currentSchedule;
    private IReadOnlyList<O2JamPreviewEvent> backgroundEvents;
    private IReadOnlyList<O2JamPreviewEvent> automaticKeySoundEvents;
    private IReadOnlyList<O2JamPreviewEvent> playableKeySoundEvents;
    private readonly List<ActiveBackgroundTrack> activeBackgroundTracks = [];
    private readonly List<SampleChannel> activeKeyChannels = [];
    private readonly Dictionary<int, double> backgroundTrackLengths = [];
    private readonly List<O2JamPreviewEvent> pendingBackgroundRestores = [];
    private readonly long createdAt = Stopwatch.GetTimestamp();
    private bool startupLogged;
    private bool skipEmptyIntro = true;

    private int nextBackgroundEventIndex;
    private int nextAutomaticKeySoundEventIndex;
    private int nextPlayableKeySoundEventIndex;
    private int prefetchBackgroundIndex;
    private int prefetchAutomaticIndex;
    private int prefetchPlayableIndex;
    private double lastTime;
    private O2JamPreviewPlaybackMode playbackMode;
    private bool startRequested;
    private bool scheduleClassified;

    public O2JamPreviewPlaybackMode PlaybackMode
    {
        get => playbackMode;
        set => EnqueueAction(() =>
        {
            if (playbackMode == value)
                return;

            playbackMode = value;
            nextPlayableKeySoundEventIndex = lowerBound(playableKeySoundEvents, CurrentTime);
            if (value == O2JamPreviewPlaybackMode.Gameplay)
                stopKeySounds();
            traceSyncControl("mode", null);
        });
    }

    public override double CurrentTime => clock.CurrentTime;

    public override bool IsRunning => clock.IsRunning;

    public override bool IsDummyDevice => false;

    public O2JamPreviewTrack(O2JamBeatmap beatmap, O2JamBeatmapSkin skin, AudioManager audioManager)
        : this(beatmap, skin, audioManager.Tracks.GetVirtual(1, "O2Jam event clock"))
    {
        audioManager.AddItem(new O2JamDetachedAudioHost(this));
    }

    internal O2JamPreviewTrack(O2JamBeatmap beatmap, IO2JamPlaybackResource resources, Track clock)
        : base("O2Jam event track")
    {
        var lease = (resources as IO2JamPlaybackLeaseSource)?.AcquirePlaybackLease();

        try
        {
            this.resources = resources;
            this.clock = clock;
            resourceLease = lease;

            var schedule = O2JamPreviewSchedule.Create(beatmap, true);
            currentSchedule = schedule;
            scheduleClassified = tryCreateEventLists(
                schedule,
                out backgroundEvents,
                out automaticKeySoundEvents,
                out playableKeySoundEvents);

            Length = calculateLength(beatmap, schedule);
            clock.Length = Length;
        }
        catch
        {
            lease?.Dispose();
            throw;
        }
    }

    internal bool CanTransferSchedule(O2JamBeatmap beatmap)
    {
        if (!resources.IsReadyForScheduling || !scheduleClassified && !tryClassifyCurrentSchedule())
            return false;

        var schedule = O2JamPreviewSchedule.Create(beatmap, true);
        if (!tryCreateEventLists(schedule, out var targetBackgroundEvents, out _, out _))
            return false;

        // WorkingBeatmap indexes the full OJM lazily, so a transfer does not need to wait for
        // difficulty-specific keysound payloads before retaining the live music clock.
        return hasSameBackgroundIdentity(backgroundEvents, targetBackgroundEvents);
    }

    internal string DescribeBackgroundIdentity() => describeBackgroundIdentity(backgroundEvents);

    internal string DescribeBackgroundIdentity(O2JamBeatmap beatmap)
    {
        var schedule = O2JamPreviewSchedule.Create(beatmap, true);
        return tryCreateEventLists(schedule, out var targetBackgroundEvents, out _, out _)
            ? describeBackgroundIdentity(targetBackgroundEvents)
            : "[pending]";
    }

    /// <summary>
    /// Replaces difficulty-specific events while retaining the currently playing OJM background layers.
    /// </summary>
    public void ReplaceSchedule(O2JamBeatmap beatmap)
    {
        var schedule = O2JamPreviewSchedule.Create(beatmap, true);
        if (!tryCreateEventLists(
                schedule,
                out var newBackgroundEvents,
                out var newAutomaticKeySoundEvents,
                out var newPlayableKeySoundEvents))
            return;

        var newLength = calculateLength(beatmap, schedule);

        EnqueueAction(() =>
        {
            var currentTime = CurrentTime;

            currentSchedule = schedule;
            backgroundEvents = newBackgroundEvents;
            automaticKeySoundEvents = newAutomaticKeySoundEvents;
            playableKeySoundEvents = newPlayableKeySoundEvents;
            scheduleClassified = true;

            // Events at the exact transfer instant may already be playing from the previous schedule.
            nextBackgroundEventIndex = upperBound(backgroundEvents, currentTime);
            nextAutomaticKeySoundEventIndex = upperBound(automaticKeySoundEvents, currentTime);
            nextPlayableKeySoundEventIndex = upperBound(playableKeySoundEvents, currentTime);
            resetPrefetchCursors();
            stopKeySounds();

            Length = Math.Max(Length, newLength);
            clock.Length = Length;
            lastTime = currentTime;
            traceSyncControl("schedule", null);
        });
    }

    public override bool Seek(double seek)
    {
        var success = clock.Seek(seek);
        EnqueueAction(() =>
        {
            skipEmptyIntro = seek == 0;
            rebuildCursor(clock.CurrentTime);
            traceSyncControl("seek", seek);
        });
        return success;
    }

    public override Task<bool> SeekAsync(double seek) => Task.FromResult(Seek(seek));

    public override void Start() => StartAsync().WaitSafely();

    public override Task StartAsync() => EnqueueAction(requestStart);

    public override void Stop() => StopAsync().WaitSafely();

    public override Task StopAsync() => EnqueueAction(stopInternal);

    public override void Reset()
    {
        base.Reset();
        EnqueueAction(() => rebuildCursor(0));
    }

    protected override void UpdateState()
    {
        base.UpdateState();
        pruneCompletedSamples();

        clock.Tempo.Value = Rate;

        var currentTime = CurrentTime;
        if (!resources.IsReadyForScheduling || !scheduleClassified && !tryClassifyCurrentSchedule())
        {
            lastTime = currentTime;
            return;
        }

        if (currentTime + 1 < lastTime || currentTime - lastTime > 500)
            rebuildCursor(currentTime);

        if (skipEmptyIntro && startRequested && PlaybackMode == O2JamPreviewPlaybackMode.Preview)
        {
            skipEmptyIntro = false;
            // OJN has no preview point. Skip charted lead-in silence, not time inside audio assets,
            // and keep all layers on the original chart timeline. Gameplay still starts at zero.
            var firstEvent = currentSchedule.PreviewEvents.FirstOrDefault(evt => evt.Volume > 0 && resources.ContainsSample(evt.SampleId));
            if (currentTime == 0 && firstEvent.Time > 0)
            {
                clock.Seek(firstEvent.Time);
                currentTime = CurrentTime;
                rebuildCursor(currentTime);
            }
        }

        prefetchUpcomingAudio(currentTime);

        if (!clock.IsRunning && !startRequested)
        {
            lastTime = currentTime;
            return;
        }

        if (startRequested && !clock.IsRunning)
        {
            if (!restoreReadyBackgroundLayers(currentTime) || !isDueAudioReady(currentTime))
            {
                lastTime = currentTime;
                return;
            }

            playDue(backgroundEvents, ref nextBackgroundEventIndex, currentTime);
            playDue(automaticKeySoundEvents, ref nextAutomaticKeySoundEventIndex, currentTime);

            if (PlaybackMode == O2JamPreviewPlaybackMode.Preview)
                playDue(playableKeySoundEvents, ref nextPlayableKeySoundEventIndex, currentTime);

            startRequested = false;
            clock.Start();
            resumeBackgroundTracks();
            traceSyncControl("started", null);
            if (!startupLogged)
            {
                startupLogged = true;
                Logger.Log($"O2Lazer preview clock ready in {Stopwatch.GetElapsedTime(createdAt).TotalMilliseconds:N1} ms at chart time {currentTime:N1} ms.",
                    level: LogLevel.Verbose);
            }
            lastTime = currentTime;
            return;
        }

        playDue(backgroundEvents, ref nextBackgroundEventIndex, currentTime);
        playDue(automaticKeySoundEvents, ref nextAutomaticKeySoundEventIndex, currentTime);

        if (PlaybackMode == O2JamPreviewPlaybackMode.Preview)
            playDue(playableKeySoundEvents, ref nextPlayableKeySoundEventIndex, currentTime);

        lastTime = currentTime;
    }

    protected override void Dispose(bool disposing)
    {
        if (IsDisposed)
            return;

        try
        {
            // AudioManager may already be tearing down TrackBass' mixer during application exit.
            // Disposing delegates stream shutdown to TrackBass without querying the dead mixer via Stop().
            disposeAllAudio(false);
            clock.Dispose();
            base.Dispose(disposing);
        }
        finally
        {
            // The OJM TrackStore must remain alive until its last child TrackBass has stopped.
            resourceLease?.Dispose();
        }
    }

    private void play(O2JamPreviewEvent evt, double offset)
    {
        if (!evt.IsKeySound)
        {
            playBackground(evt, offset);
            return;
        }

        var sample = resources.GetSample(new O2JamHitSampleInfo(evt.SampleId, evt.Volume, evt.Pan));
        if (sample == null)
            return;

        var channel = sample.GetChannel();
        channel.Volume.Value = evt.Volume / 100d;
        channel.Balance.Value = evt.Pan;
        channel.BindAdjustments(this);
        channel.Play();
        activeKeyChannels.Add(channel);
    }

    private bool tryClassifyCurrentSchedule()
    {
        if (!tryCreateEventLists(
                currentSchedule,
                out var classifiedBackgroundEvents,
                out var classifiedAutomaticKeySoundEvents,
                out var classifiedPlayableKeySoundEvents))
            return false;

        backgroundEvents = classifiedBackgroundEvents;
        automaticKeySoundEvents = classifiedAutomaticKeySoundEvents;
        playableKeySoundEvents = classifiedPlayableKeySoundEvents;
        scheduleClassified = true;

        var currentTime = CurrentTime;
        nextBackgroundEventIndex = lowerBound(backgroundEvents, currentTime);
        nextAutomaticKeySoundEventIndex = lowerBound(automaticKeySoundEvents, currentTime);
        nextPlayableKeySoundEventIndex = lowerBound(playableKeySoundEvents, currentTime);
        resetPrefetchCursors();
        restoreBackgroundLayers(currentTime, nextBackgroundEventIndex);
        return true;
    }

    private bool tryCreateEventLists(
        O2JamPreviewSchedule schedule,
        out IReadOnlyList<O2JamPreviewEvent> classifiedBackgroundEvents,
        out IReadOnlyList<O2JamPreviewEvent> classifiedAutomaticKeySoundEvents,
        out IReadOnlyList<O2JamPreviewEvent> classifiedPlayableKeySoundEvents)
    {
        var automaticCandidates = schedule.PreviewEvents
                                          .Where(evt => evt.IsKeySound && evt.IsAutomatic)
                                          .ToArray();
        classifiedBackgroundEvents = schedule.BackgroundEvents;
        classifiedAutomaticKeySoundEvents = automaticCandidates;
        classifiedPlayableKeySoundEvents = schedule.PreviewEvents
                                                    .Where(evt => evt.IsKeySound && !evt.IsAutomatic)
                                                    .ToArray();

        var streamedEvents = new List<O2JamPreviewEvent>();
        var sampleEvents = new List<O2JamPreviewEvent>();

        foreach (var evt in automaticCandidates)
        {
            if (!resources.TryGetAutomaticSampleStreaming(evt.SampleId, out var streamed))
                return false;

            if (streamed)
                streamedEvents.Add(evt with { IsKeySound = false });
            else
                sampleEvents.Add(evt);
        }

        classifiedBackgroundEvents = schedule.BackgroundEvents
                                             .Concat(streamedEvents)
                                             .OrderBy(evt => evt.Time)
                                             .ToArray();
        classifiedAutomaticKeySoundEvents = sampleEvents;
        return true;
    }

    private void rebuildCursor(double time)
    {
        if (clock.IsRunning)
        {
            clock.Stop();
            startRequested = true;
        }

        disposeAllAudio();
        pendingBackgroundRestores.Clear();
        nextBackgroundEventIndex = lowerBound(backgroundEvents, time);
        nextAutomaticKeySoundEventIndex = lowerBound(automaticKeySoundEvents, time);
        nextPlayableKeySoundEventIndex = lowerBound(playableKeySoundEvents, time);
        resetPrefetchCursors();
        restoreBackgroundLayers(time, nextBackgroundEventIndex);
        lastTime = time;
        traceSyncControl("rebuild", time);
    }

    private void playDue(IReadOnlyList<O2JamPreviewEvent> events, ref int nextIndex, double currentTime)
    {
        while (nextIndex < events.Count && events[nextIndex].Time <= currentTime)
        {
            var evt = events[nextIndex];
            if (evt.IsKeySound && currentTime - evt.Time > allowable_late_start)
            {
                nextIndex++;
                continue;
            }

            // A seek or repeated BGM reference can request a decoder outside the lookahead.
            // Leave the event pending rather than blocking the audio thread or losing the event.
            if (!isEventReady(evt))
                break;

            nextIndex++;
            play(evt, evt.IsKeySound ? 0 : currentTime - evt.Time);
        }
    }

    private void requestStart()
    {
        if (clock.IsRunning)
            return;

        // Starting the virtual clock before the first native OJM decoder is ready causes the
        // opening events to elapse in silence. UpdateState starts it once due audio is available.
        startRequested = true;
        traceSyncControl("start-request", null);
    }

    private void stopInternal()
    {
        startRequested = false;
        clock.Stop();
        pauseAudio();
        traceSyncControl("stopped", null);
    }

    private bool isDueAudioReady(double currentTime)
    {
        for (var index = nextBackgroundEventIndex; index < backgroundEvents.Count && backgroundEvents[index].Time <= currentTime; index++)
        {
            if (!resources.IsBackgroundTrackReady(backgroundEvents[index].SampleId))
                return false;
        }

        for (var index = nextAutomaticKeySoundEventIndex;
             index < automaticKeySoundEvents.Count && automaticKeySoundEvents[index].Time <= currentTime;
             index++)
        {
            if (!resources.IsSampleReady(automaticKeySoundEvents[index].SampleId))
                return false;
        }

        if (PlaybackMode != O2JamPreviewPlaybackMode.Preview)
            return true;

        for (var index = nextPlayableKeySoundEventIndex;
             index < playableKeySoundEvents.Count && playableKeySoundEvents[index].Time <= currentTime;
             index++)
        {
            if (!resources.IsSampleReady(playableKeySoundEvents[index].SampleId))
                return false;
        }

        return true;
    }

    private void prefetchUpcomingAudio(double currentTime)
    {
        // Ten seconds matches the former playback pipeline's preload horizon. It is long enough
        // to absorb OJM extraction and decoder spikes without decoding the whole chart at once.
        const double look_ahead = 10_000;
        var endTime = currentTime + look_ahead;

        while (prefetchBackgroundIndex < backgroundEvents.Count && backgroundEvents[prefetchBackgroundIndex].Time <= endTime)
            resources.PrefetchBackgroundTrack(backgroundEvents[prefetchBackgroundIndex++].SampleId);

        while (prefetchAutomaticIndex < automaticKeySoundEvents.Count && automaticKeySoundEvents[prefetchAutomaticIndex].Time <= endTime)
            resources.PrefetchSample(automaticKeySoundEvents[prefetchAutomaticIndex++].SampleId);

        // Gameplay needs the same warm keysound cache even though judgements, not the preview,
        // trigger those samples. Only preloading in preview would miss later gameplay notes.
        while (prefetchPlayableIndex < playableKeySoundEvents.Count && playableKeySoundEvents[prefetchPlayableIndex].Time <= endTime)
            resources.PrefetchSample(playableKeySoundEvents[prefetchPlayableIndex++].SampleId);
    }

    private void resetPrefetchCursors()
    {
        prefetchBackgroundIndex = nextBackgroundEventIndex;
        prefetchAutomaticIndex = nextAutomaticKeySoundEventIndex;
        prefetchPlayableIndex = nextPlayableKeySoundEventIndex;
    }

    private bool isEventReady(O2JamPreviewEvent evt) => evt.IsKeySound
        ? resources.IsSampleReady(evt.SampleId)
        : resources.IsBackgroundTrackReady(evt.SampleId);

    private void stopKeySounds()
    {
        foreach (var channel in activeKeyChannels)
            channel.Stop();

        activeKeyChannels.Clear();
    }

    private void pauseAudio()
    {
        stopKeySounds();

        foreach (var active in activeBackgroundTracks)
            active.Track.Stop();
    }

    private void pruneCompletedSamples()
    {
        for (var index = activeKeyChannels.Count - 1; index >= 0; index--)
        {
            if (!activeKeyChannels[index].Playing)
                activeKeyChannels.RemoveAt(index);
        }

        for (var index = activeBackgroundTracks.Count - 1; index >= 0; index--)
        {
            var track = activeBackgroundTracks[index].Track;
            if (track.Length > 0)
                backgroundTrackLengths[activeBackgroundTracks[index].SampleId] = track.Length;

            if (!track.IsDisposed && !track.HasCompleted && (track.Length <= 0 || track.CurrentTime < track.Length - 0.01))
                continue;

            if (!track.IsDisposed)
                track.Dispose();

            activeBackgroundTracks.RemoveAt(index);
        }
    }

    private void playBackground(O2JamPreviewEvent evt, double offset)
    {
        var track = resources.GetBackgroundTrack(evt.SampleId);
        if (track == null)
            return;

        track.Volume.Value = evt.Volume / 100d;
        track.Balance.Value = evt.Pan;
        track.BindAdjustments(this);

        if (offset > 0 && !track.Seek(offset))
        {
            track.Dispose();
            return;
        }

        if (clock.IsRunning)
            track.Start();

        activeBackgroundTracks.Add(new ActiveBackgroundTrack(track, evt.SampleId, evt.Time));
        traceSyncControl("bgm-created", evt.Time);
    }

    private void restoreBackgroundLayers(double time, int eventIndex)
    {
        // OJM background events may contain a single song-length layer. Recreate every layer that
        // still contains the seek point; Track.Seek() rejects layers which have already ended.
        for (var index = 0; index < eventIndex; index++)
        {
            var evt = backgroundEvents[index];
            var offset = time - evt.Time;
            if (backgroundTrackLengths.TryGetValue(evt.SampleId, out var length) && offset >= length)
                continue;

            pendingBackgroundRestores.Add(evt);
            resources.PrefetchBackgroundTrack(evt.SampleId);
        }
    }

    private bool restoreReadyBackgroundLayers(double time)
    {
        while (pendingBackgroundRestores.Count > 0)
        {
            var evt = pendingBackgroundRestores[0];
            if (!resources.IsBackgroundTrackReady(evt.SampleId))
                return false;

            pendingBackgroundRestores.RemoveAt(0);
            playBackground(evt, time - evt.Time);
        }

        return true;
    }

    private void resumeBackgroundTracks()
    {
        foreach (var active in activeBackgroundTracks)
        {
            if (!active.Track.IsDisposed && !active.Track.HasCompleted)
                active.Track.Start();
        }
    }

    private void disposeAllAudio(bool stopBeforeDisposal = true)
    {
        if (stopBeforeDisposal)
            stopKeySounds();
        else
            activeKeyChannels.Clear();

        foreach (var active in activeBackgroundTracks)
        {
            if (active.Track.IsDisposed)
                continue;

            if (stopBeforeDisposal)
                active.Track.Stop();

            active.Track.Dispose();
        }

        activeBackgroundTracks.Clear();
    }

    private static int lowerBound(IReadOnlyList<O2JamPreviewEvent> events, double time)
    {
        var low = 0;
        var high = events.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (events[middle].Time < time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static int upperBound(IReadOnlyList<O2JamPreviewEvent> events, double time)
    {
        var low = 0;
        var high = events.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;
            if (events[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }

    private static double calculateLength(O2JamBeatmap beatmap, O2JamPreviewSchedule schedule)
    {
        var lastEventTime = schedule.PreviewEvents.Count == 0 ? 0 : schedule.PreviewEvents[^1].Time;
        return Math.Max(1000, Math.Max(beatmap.BeatmapInfo.Length, lastEventTime + 5000));
    }

    private static bool hasSameBackgroundIdentity(
        IReadOnlyList<O2JamPreviewEvent> source,
        IReadOnlyList<O2JamPreviewEvent> target)
    {
        // Difficulties of one song may add or replace backing stems, while multi-song OJNs use
        // disjoint BGM banks. One shared authored stem is enough to retain the currently audible mix.
        var sourceLayers = source.Select(evt => evt.SampleId).Distinct().ToArray();
        var targetLayers = target.Select(evt => evt.SampleId).Distinct().ToArray();

        if (sourceLayers.Length == 0 || targetLayers.Length == 0)
            return sourceLayers.Length == targetLayers.Length;

        return sourceLayers.Intersect(targetLayers).Any();
    }

    private static string describeBackgroundIdentity(IReadOnlyList<O2JamPreviewEvent> events) =>
        events.Count == 0 ? "[]" : $"[{string.Join(',', events.Select(evt => evt.SampleId).Distinct())}]";

    partial void traceSyncControl(string action, double? requestedTime);

    private readonly record struct ActiveBackgroundTrack(Track Track, int SampleId, double EventTime);
}
