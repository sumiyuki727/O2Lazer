#if O2JAM_SYNC_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.Globalization;
using System.Text;
using System.Threading;
using osu.Game.Rulesets.O2Lazer.Diagnostics;

namespace osu.Game.Rulesets.O2Lazer.Audio;

public sealed partial class O2JamPreviewTrack
{
    private O2JamSyncSession? syncSession;
    private int syncEpoch;

    internal int SyncEpoch => Volatile.Read(ref syncEpoch);

    internal void AttachSyncDiagnostics(O2JamSyncSession session) => EnqueueAction(() =>
    {
        syncSession = session;
        traceSyncControl("attach", null);
    });

    internal void DetachSyncDiagnostics(O2JamSyncSession session) => EnqueueAction(() =>
    {
        if (syncSession != session)
            return;
        traceSyncControl("detach", null);
        syncSession = null;
    });

    internal void RequestSyncSample(O2JamSyncSession session, O2JamSyncGameplaySample sample)
    {
        if (IsDisposed)
        {
            session.CompleteSample();
            return;
        }

        _ = EnqueueAction(() =>
        {
            try
            {
                if (syncSession != session || IsDisposed)
                    return;

                var now = Stopwatch.GetTimestamp();
                var virtualTime = CurrentTime;
                var queueTime = Stopwatch.GetElapsedTime(sample.Timestamp, now).TotalMilliseconds;
                var state = sample.State;
                var validPair = sample.Epoch == syncEpoch && queueTime <= 100
                                && !state.Rewinding && !state.CatchingUp && !state.Replay
                                && state.Running == IsRunning && Math.Abs(state.Rate - Rate) < 0.000001;
                var message = new StringBuilder(FormattableString.Invariant(
                    $"[O2SYNC/v1] session={session.Id} action=sample mono_ticks={now} request_ticks={sample.Timestamp} queue_ms={queueTime:F3} epoch={syncEpoch} request_epoch={sample.Epoch} pair_stable={validPair} mode={playbackMode} virtual_ms={virtualTime:F3} virtual_rate={clock.Rate:F6} event_rate={Rate:F6} running={IsRunning} start_pending={startRequested}"));
                message.Append(FormattableString.Invariant(
                    $" judgement_ms={sample.JudgementTime:F3} parent_ms={number(sample.ParentTime)} request_virtual_ms={sample.VirtualTime:F3} total_offset_ms={number(sample.TotalOffset)} frame_elapsed_ms={sample.FrameElapsed:F3} request_rate={state.Rate:F6} request_running={state.Running} paused={state.Paused} rewinding={state.Rewinding} catching_up={state.CatchingUp} replay={state.Replay}"));
                message.Append(FormattableString.Invariant(
                    $" hits={sample.Hits.Count} hit_mean_ms={number(sample.Hits.Mean)} hit_min_ms={number(sample.Hits.Minimum)} hit_max_ms={number(sample.Hits.Maximum)}"));
                appendSyncBackgrounds(message, virtualTime);
                session.WriteLog(message.ToString());
            }
            finally
            {
                session.CompleteSample();
            }
        });
    }

    partial void traceSyncControl(string action, double? requestedTime)
    {
        if (syncSession is not { } session)
            return;

        // Any intervening control operation makes cross-thread extrapolation ambiguous, even if
        // its event log was throttled. Keep the epoch independent of the logging limit.
        Interlocked.Increment(ref syncEpoch);
        var now = Stopwatch.GetTimestamp();
        if (!session.TryLogEvent(now, out var suppressed))
            return;

        var virtualTime = CurrentTime;
        var message = new StringBuilder(FormattableString.Invariant(
            $"[O2SYNC/v1] session={session.Id} action={action} mono_ticks={now} epoch={syncEpoch} requested_ms={number(requestedTime)} mode={playbackMode} virtual_ms={virtualTime:F3} virtual_rate={clock.Rate:F6} event_rate={Rate:F6} running={IsRunning} start_pending={startRequested} suppressed_events={suppressed}"));
        appendSyncBackgrounds(message, virtualTime);
        session.WriteLog(message.ToString());
    }

    private void appendSyncBackgrounds(StringBuilder message, double virtualTime)
    {
        message.Append(FormattableString.Invariant($" bgm_count={activeBackgroundTracks.Count} bgm_cached=true bgm=["));
        // Multi-layer songs must not turn a diagnostic sample into a large allocation or a log flood.
        // Report truncation explicitly instead of presenting the first layer as the only one.
        var count = Math.Min(4, activeBackgroundTracks.Count);
        for (var index = 0; index < count; index++)
        {
            if (index > 0)
                message.Append(';');
            var active = activeBackgroundTracks[index];
            if (active.Track.IsDisposed)
            {
                message.Append(FormattableString.Invariant($"id={active.SampleId},disposed=true"));
                continue;
            }

            var sample = new O2JamSyncBackgroundSample(active.SampleId, active.EventTime,
                active.Track.CurrentTime, active.Track.Rate, active.Track.IsRunning);
            message.Append(FormattableString.Invariant(
                $"id={sample.SampleId},origin_ms={sample.EventTime:F3},position_ms={sample.Position:F3},chart_ms={sample.ChartTime:F3},lead_virtual_ms={sample.LeadOver(virtualTime):F3},rate={sample.Rate:F6},running={sample.Running}"));
        }
        message.Append(FormattableString.Invariant($"] bgm_omitted={activeBackgroundTracks.Count - count}"));
    }

    private static string number(double? value) => value?.ToString("F3", CultureInfo.InvariantCulture) ?? "unknown";
}
#endif
