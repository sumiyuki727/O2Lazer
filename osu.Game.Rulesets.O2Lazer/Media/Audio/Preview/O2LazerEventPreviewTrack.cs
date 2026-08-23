using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using osu.Framework.Audio;

namespace osu.Game.Rulesets.O2Lazer.Media.Audio.Preview;

internal sealed class O2LazerEventPreviewTrack : O2LazerPreviewTrack
{
    private readonly string? basePath;
    private readonly AudioManager audioManager;
    private readonly CancellationTokenSource timelineCancellation = new();
    private readonly IReadOnlyList<Func<CancellationToken, O2LazerEventPreviewTimeline>> timelineSources;

    private Task<O2LazerEventPreviewTimeline>? timelineTask;
    private O2LazerEventPreviewPlayback? playback;
    private double? pendingPreviewPosition;
    private bool previewStartPending;
    private int timelineSourceIndex;

    protected override bool CanComplete => playback != null;

    protected override bool StartClockImmediately => PlaybackMode != O2LazerPreviewTrackPlaybackMode.Preview;

    protected override bool IsLengthFinal => playback?.IsLengthFinal == true;

    /// <summary>
    /// Gameplay uses this track only as a clock; O2LAZER audio is driven by chart events elsewhere.
    /// </summary>
    protected override void OnPlaybackModeChanged(O2LazerPreviewTrackPlaybackMode mode)
    {
        EnqueueAction(() =>
        {
            if (mode == O2LazerPreviewTrackPlaybackMode.Preview)
                playback?.EnterPreview(CurrentTime);
            else
            {
                var startClock = previewStartPending;
                previewStartPending = false;
                pendingPreviewPosition = null;
                playback?.ExitPreview();

                if (startClock)
                    StartClock();
            }
        });
    }

    internal O2LazerEventPreviewTrack(
        Func<CancellationToken, O2LazerEventPreviewTimeline> timelineFactory,
        string? basePath,
        AudioManager audioManager)
        : this([timelineFactory], basePath, audioManager)
    {
    }

    internal O2LazerEventPreviewTrack(
        IReadOnlyList<Func<CancellationToken, O2LazerEventPreviewTimeline>> timelineSources,
        string? basePath,
        AudioManager audioManager)
        : base(audioManager)
    {
        if (timelineSources.Count == 0)
            throw new ArgumentException(@"At least one preview timeline source is required.", nameof(timelineSources));

        this.basePath = basePath;
        this.audioManager = audioManager;
        this.timelineSources = timelineSources;
        Length = O2LazerEventPreviewTimeline.DEFAULT_LENGTH;
    }

    internal override void RestorePreview(double? gameplayTime)
    {
        // The preview timeline is normalised to its first event, while gameplay time is absolute
        // chart time. Convert before seeking so the resumed BGM/keysound positions line up.
        if (gameplayTime is { } time && playback is { } existing)
            gameplayTime = Math.Max(0, time - existing.TimeOffset);

        base.RestorePreview(gameplayTime);
    }

    protected override void PrepareStart() => consumeTimeline();

    protected override void StartPlayback()
    {
        if (PlaybackMode != O2LazerPreviewTrackPlaybackMode.Preview)
            return;

        if (!IsRunning)
            previewStartPending = true;

        if (playback != null)
            playback.Start(CurrentTime);
        else
            pendingPreviewPosition = CurrentTime;
    }

    protected override void StopPlayback()
    {
        previewStartPending = false;
        pendingPreviewPosition = null;
        playback?.Stop();
    }

    protected override void SeekPlayback(double seek, bool wasRunning)
    {
        if (playback != null)
            playback.Seek(seek, PlaybackMode == O2LazerPreviewTrackPlaybackMode.Preview);
        else if ((wasRunning || previewStartPending) && PlaybackMode == O2LazerPreviewTrackPlaybackMode.Preview)
            pendingPreviewPosition = seek;
    }

    protected override void ResetPlayback()
    {
        previewStartPending = false;
        pendingPreviewPosition = null;
        playback?.Reset();
    }

    protected override void UpdateState()
    {
        consumeTimeline();
        base.UpdateState();

        var previewMode = PlaybackMode == O2LazerPreviewTrackPlaybackMode.Preview;
        var shouldUpdatePlayback = previewMode && (IsRunning || previewStartPending);
        var requireDueAudioReady = previewStartPending || IsRestoreFadePending || IsSeekFadePending;
        var startState = shouldUpdatePlayback
            ? playback?.Update(CurrentTime, requireDueAudioReady) ?? O2LazerPreviewPlaybackStartState.Waiting
            : O2LazerPreviewPlaybackStartState.Waiting;

        if (startState == O2LazerPreviewPlaybackStartState.InitialAudioUnavailable && advanceToNextTimelineSource())
            return;

        if (playback != null)
        {
            Length = playback.Length;

            if (TryResolvePendingRestorePosition())
                startState = playback.Update(CurrentTime, requireDueAudioReady);
        }

        if (startState != O2LazerPreviewPlaybackStartState.Waiting)
        {
            BeginPendingRestoreFade();
            BeginPendingSeekFade();
        }

        if (previewStartPending && startState != O2LazerPreviewPlaybackStartState.Waiting)
        {
            previewStartPending = false;
            // Starting the preview clock earlier would collapse every event elapsed during the first sample load.
            StartClock();
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (!IsDisposed)
        {
            timelineCancellation.Cancel();

            if (timelineTask != null)
            {
                _ = timelineTask.ContinueWith(
                task => _ = task.Exception,
                CancellationToken.None,
                TaskContinuationOptions.OnlyOnFaulted | TaskContinuationOptions.ExecuteSynchronously,
                TaskScheduler.Default);
            }

            timelineCancellation.Dispose();
            playback?.Dispose();
        }

        base.Dispose(disposing);
    }

    private void consumeTimeline()
    {
        if (playback != null)
            return;

        if (PlaybackMode != O2LazerPreviewTrackPlaybackMode.Preview || (!IsRunning && !previewStartPending))
            return;

        var task = timelineTask ??= prepareTimelineSource();
        if (!task.IsCompleted)
            return;

        O2LazerEventPreviewTimeline timeline;

        try
        {
            timeline = task.GetAwaiter().GetResult();
        }
        catch (OperationCanceledException) when (IsDisposed)
        {
            return;
        }
        catch (Exception exception)
        {
            O2LazerLogger.Error(exception, "Failed to prepare O2LAZER event preview timeline.");
            timeline = new O2LazerEventPreviewTimeline([], O2LazerEventPreviewTimeline.DEFAULT_LENGTH);
        }

        activateTimeline(timeline);
    }

    private bool advanceToNextTimelineSource()
    {
        if (timelineSourceIndex + 1 >= timelineSources.Count)
            return false;

        playback?.Dispose();
        playback = null;
        timelineSourceIndex++;
        Length = O2LazerEventPreviewTimeline.DEFAULT_LENGTH;
        pendingPreviewPosition = CurrentTime;
        timelineTask = null;
        return true;
    }

    private Task<O2LazerEventPreviewTimeline> prepareTimelineSource() => Task.Run(
        () => timelineSources[timelineSourceIndex](timelineCancellation.Token),
        timelineCancellation.Token);

    private void activateTimeline(O2LazerEventPreviewTimeline timeline)
    {
        playback = new O2LazerEventPreviewPlayback(this, timeline, basePath, audioManager);
        Length = playback.Length;
        var restorePositionResolved = TryResolvePendingRestorePosition();
        var activationPosition = restorePositionResolved ? CurrentTime : pendingPreviewPosition ?? CurrentTime;
        pendingPreviewPosition = null;

        if (!restorePositionResolved)
            playback.Seek(activationPosition, PlaybackMode == O2LazerPreviewTrackPlaybackMode.Preview);

        if ((IsRunning || previewStartPending) && PlaybackMode == O2LazerPreviewTrackPlaybackMode.Preview)
            playback.Start(activationPosition);
    }
}



