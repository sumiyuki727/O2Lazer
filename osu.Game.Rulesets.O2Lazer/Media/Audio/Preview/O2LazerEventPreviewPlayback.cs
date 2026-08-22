using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using osu.Framework.Audio;
using osu.Framework.Bindables;
using osu.Game.Rulesets.O2Lazer.Media.Audio.Samples;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Preview;

internal enum O2LazerPreviewPlaybackStartState
{
    Waiting,
    Ready,
    InitialAudioUnavailable,
}

internal sealed class O2LazerEventPreviewPlayback : IDisposable
{
    private readonly O2LazerPreviewTrack owner;
    private readonly AudioManager audioManager;
    private readonly List<O2LazerPreviewTimelineEntry> sortedEvents = [];
    private readonly bool deriveLengthFromSamples;
    private readonly bool extendLengthFromSamples;
    private readonly BindableDouble masterGain = new(1);
    private readonly CancellationTokenSource cancellation = new();
    private readonly Dictionary<ushort, double> sampleEndTimes = [];
    private readonly HashSet<ushort> pendingSampleLengths = [];

    private O2LazerPcmPlaybackSession? playbackSession;
    private int nextEventIndex;
    private bool eventResyncRequired;
    private double derivedLength;
    private bool derivedLengthResolutionComplete;
    private bool disposed;

    public double Length { get; private set; }

    public bool IsLengthFinal => !deriveLengthFromSamples || derivedLengthResolutionComplete;

    internal int ActiveVoiceCount => playbackSession?.ActiveVoiceCount ?? 0;

    internal O2LazerEventPreviewPlayback(
        O2LazerPreviewTrack owner,
        O2LazerEventPreviewTimeline timeline,
        string? basePath,
        AudioManager audioManager)
    {
        this.owner = owner;
        this.audioManager = audioManager;
        sortedEvents.AddRange(timeline.Entries);
        Length = timeline.Length;
        deriveLengthFromSamples = timeline.DeriveLengthFromSamples;
        extendLengthFromSamples = timeline.ExtendLengthFromSamples;

        if (deriveLengthFromSamples || extendLengthFromSamples)
        {
            foreach (var group in sortedEvents.GroupBy(evt => evt.SampleKey))
                sampleEndTimes[group.Key] = group.Max(evt => evt.Time);
        }

        if (basePath == null)
            return;

        var definitions = sortedEvents
            .GroupBy(evt => evt.SampleKey)
            .ToDictionary(group => group.Key, group => group.First().SamplePath);
        var sampleUsages = sortedEvents
            .Select(evt => new O2LazerSampleUsage(evt.SampleKey, evt.Time, ResumeAfterSeek: evt.ResumeAfterSeek))
            .ToArray();
        var previewRate = Math.Abs(owner.AggregateTempo.Value);
        var rate = double.IsFinite(previewRate) && previewRate is >= 0.05 and <= 2 ? previewRate : 1;

        playbackSession = new O2LazerPcmPlaybackSession(
            definitions,
            basePath,
            rate,
            sampleUsages,
            audioManager,
            () => owner.CurrentTime,
            masterGain);
        playbackSession.Initialise(cancellation.Token, owner.CurrentTime, waitForInitialAssets: false);
    }

    public void Dispose()
    {
        if (disposed)
            return;

        disposed = true;
        cancellation.Cancel();
        playbackSession?.Dispose();
        playbackSession = null;
        cancellation.Dispose();
    }

    public void EnterPreview(double currentTime)
    {
        nextEventIndex = currentTime <= 0 ? 0 : findFirstEventAfter(currentTime);
        eventResyncRequired = currentTime > 0;
    }

    public void ExitPreview()
    {
        eventResyncRequired = false;
        stopPlayback();
    }

    public void Start(double currentTime)
    {
        if (currentTime <= 0)
        {
            nextEventIndex = 0;
            eventResyncRequired = false;
            return;
        }

        eventResyncRequired = ActiveVoiceCount == 0;

        if (eventResyncRequired)
            nextEventIndex = findFirstEventAfter(currentTime);
    }

    public void Stop() => stopPlayback();

    public void Seek(double seek, bool previewMode)
    {
        stopPlayback();

        if (!previewMode)
            return;

        nextEventIndex = seek == 0 ? 0 : findFirstEventAfter(seek);
        eventResyncRequired = seek > 0;
    }

    public void Reset()
    {
        nextEventIndex = 0;
        eventResyncRequired = false;
        stopPlayback();
    }

    public O2LazerPreviewPlaybackStartState Update(double currentTime, bool requireDueAudioReady)
    {
        updateMasterGain();
        playbackSession?.Update(currentTime);
        updateLengthFromPreparedSamples();

        if (eventResyncRequired)
        {
            var state = resumeBackgroundSamples(currentTime);
            if (state != O2LazerPreviewPlaybackStartState.Ready)
                return state;

            eventResyncRequired = false;
            return O2LazerPreviewPlaybackStartState.Ready;
        }

        if (requireDueAudioReady && !areDueSamplesResolved(currentTime))
            return O2LazerPreviewPlaybackStartState.Waiting;

        var controller = playbackSession?.Controller;
        var initialAudioUnavailable = false;

        while (nextEventIndex < sortedEvents.Count && sortedEvents[nextEventIndex].Time <= currentTime)
        {
            var evt = sortedEvents[nextEventIndex];

            if (controller == null || !controller.HasSampleDefinition(evt.SampleKey))
            {
                if (nextEventIndex == 0)
                {
                    initialAudioUnavailable = true;
                    derivedLengthResolutionComplete = true;
                }

                nextEventIndex++;
                continue;
            }

            if (!controller.IsSampleReady(evt.SampleKey))
                break;

            controller.QueuePlay(evt.SampleKey, evt.Volume, 0);
            nextEventIndex++;
        }

        controller?.SubmitLivePlayBatch();
        return initialAudioUnavailable
            ? O2LazerPreviewPlaybackStartState.InitialAudioUnavailable
            : O2LazerPreviewPlaybackStartState.Ready;
    }

    private bool areDueSamplesResolved(double currentTime)
    {
        var controller = playbackSession?.Controller;
        if (controller == null)
            return true;

        for (var i = nextEventIndex; i < sortedEvents.Count && sortedEvents[i].Time <= currentTime; i++)
        {
            var key = sortedEvents[i].SampleKey;
            if (controller.HasSampleDefinition(key) && !controller.IsSampleReady(key))
                return false;
        }

        return true;
    }

    private O2LazerPreviewPlaybackStartState resumeBackgroundSamples(double currentTime)
    {
        var controller = playbackSession?.Controller;
        if (controller == null)
            return nextEventIndex > 0 && sortedEvents[0].ResumeAfterSeek
                ? O2LazerPreviewPlaybackStartState.InitialAudioUnavailable
                : O2LazerPreviewPlaybackStartState.Ready;

        var resumeEvents = new List<O2LazerPreviewTimelineEntry>();
        var seenKeys = new HashSet<ushort>();

        for (var i = nextEventIndex - 1; i >= 0; i--)
        {
            var evt = sortedEvents[i];
            if (!evt.ResumeAfterSeek || !seenKeys.Add(evt.SampleKey))
                continue;

            controller.PrepareSample(evt.SampleKey);
            resumeEvents.Add(evt);
        }

        if (resumeEvents.Any(evt => controller.HasSampleDefinition(evt.SampleKey)
                                    && !controller.IsSampleReady(evt.SampleKey, currentTime - evt.Time)))
            return O2LazerPreviewPlaybackStartState.Waiting;

        var initialAudioUnavailable = false;

        foreach (var evt in resumeEvents)
        {
            if (!controller.HasSampleDefinition(evt.SampleKey))
            {
                if (evt.Equals(sortedEvents[0]))
                    initialAudioUnavailable = true;

                continue;
            }

            var offset = currentTime - evt.Time;
            var sampleLength = controller.GetSampleLength(evt.SampleKey);
            updateLength(evt.SampleKey, sampleLength);

            if (offset < sampleLength)
                controller.QueuePlay(evt.SampleKey, evt.Volume, offset);
        }

        controller.SubmitLivePlayBatch();
        return initialAudioUnavailable
            ? O2LazerPreviewPlaybackStartState.InitialAudioUnavailable
            : O2LazerPreviewPlaybackStartState.Ready;
    }

    private void updateLengthFromPreparedSamples()
    {
        if (!deriveLengthFromSamples && !extendLengthFromSamples)
            return;

        var controller = playbackSession?.Controller;
        if (controller == null)
            return;

        foreach (var sampleKey in controller.PreparedSampleKeys)
        {
            if (sampleEndTimes.ContainsKey(sampleKey))
                pendingSampleLengths.Add(sampleKey);
        }

        foreach (var sampleKey in pendingSampleLengths.ToArray())
        {
            if (!controller.IsSampleReady(sampleKey))
                continue;

            var sampleLength = controller.GetSampleLength(sampleKey);
            updateLength(sampleKey, sampleLength);
        }

        if (derivedLength <= 0)
            return;

        Length = deriveLengthFromSamples ? derivedLength : Math.Max(Length, derivedLength);

        if (deriveLengthFromSamples)
            derivedLengthResolutionComplete = true;
    }

    private void updateMasterGain()
    {
        var global = audioManager.AggregateVolume.Value;
        var gain = owner.PreviewPlaybackGain * (double.IsFinite(global) ? Math.Max(0, global) : 0);
        masterGain.Value = double.IsFinite(gain) ? Math.Max(0, gain) : 0;
    }

    private void stopPlayback() => playbackSession?.Controller?.StopAll();

    private void updateLength(ushort sampleKey, double sampleLength)
    {
        if (sampleLength <= 0 || !sampleEndTimes.Remove(sampleKey, out var eventTime))
            return;

        pendingSampleLengths.Remove(sampleKey);
        derivedLength = Math.Max(derivedLength, eventTime + sampleLength);
    }

    private int findFirstEventAfter(double time)
    {
        var low = 0;
        var high = sortedEvents.Count;

        while (low < high)
        {
            var middle = low + (high - low) / 2;

            if (sortedEvents[middle].Time <= time)
                low = middle + 1;
            else
                high = middle;
        }

        return low;
    }
}
