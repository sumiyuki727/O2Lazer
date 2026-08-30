#if O2JAM_SYNC_DIAGNOSTICS
using System;
using System.Diagnostics;
using System.Reflection;
using System.Text.Json;
using osu.Framework.Allocation;
using osu.Framework.Audio;
using osu.Framework.Logging;
using osu.Game.Beatmaps;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Audio;
using osu.Game.Rulesets.O2Lazer.Diagnostics;
using osu.Game.Rulesets.O2Lazer.Scoring;
using osu.Game.Screens.Play;

namespace osu.Game.Rulesets.O2Lazer.UI;

public partial class O2JamDrawableRuleset
{
    [Resolved(canBeNull: true)]
    private AudioManager? syncAudioManager { get; set; }

    private O2JamSyncSession? syncSession;
    private O2JamPreviewTrack? syncTrack;
    private FramedBeatmapClock? syncNativeClock;
    private readonly O2JamSyncHitAccumulator syncHits = new();

    protected override void LoadComplete()
    {
        base.LoadComplete();

        // The native container does not expose its offset-aware clock publicly. This optional,
        // read-only lookup is isolated to diagnostic builds; a changed host API disables that
        // field in the trace, not gameplay. No clock is replaced, processed, sought or adjusted.
        try
        {
            for (var parent = Parent; parent != null; parent = parent.Parent)
            {
                if (parent is not GameplayClockContainer container)
                    continue;
                syncNativeClock = typeof(GameplayClockContainer)
                                  .GetField("GameplayClock", BindingFlags.Instance | BindingFlags.NonPublic)?
                                  .GetValue(container) as FramedBeatmapClock;
                break;
            }
        }
        catch (Exception exception)
        {
            Logger.Log($"[O2SYNC/v1] action=clock-inspection-unavailable error={exception.GetType().Name}", outputToListeners: false);
        }

        syncTrack = syncNativeClock?.Source as O2JamPreviewTrack ?? gameplayTrack;
        syncSession = new O2JamSyncSession();
        var sourceMatches = syncNativeClock == null ? "unknown" : ReferenceEquals(syncNativeClock.Source, syncTrack).ToString();
        Logger.Log(FormattableString.Invariant(
            $"[O2SYNC/v1] session={syncSession.Id} action=gameplay-attach mono_ticks={Stopwatch.GetTimestamp()} tick_frequency={Stopwatch.Frequency} build_mvid={typeof(O2JamDrawableRuleset).Module.ModuleVersionId} beatmap_id={Beatmap.BeatmapInfo.ID} title={JsonSerializer.Serialize(Beatmap.Metadata.Title)} difficulty={JsonSerializer.Serialize(Beatmap.BeatmapInfo.DifficultyName)} native_source_matches={sourceMatches} coordinator_matches={ReferenceEquals(syncTrack, gameplayTrack)} wasapi={syncAudioManager?.UseExperimentalWasapi.Value.ToString() ?? "unknown"} diagnostic_track={syncTrack != null}"),
            outputToListeners: false);
        syncTrack?.AttachSyncDiagnostics(syncSession);
        NewResult += collectSyncHit;
        RevertResult += revertSyncHit;
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        if (syncSession == null || syncTrack == null)
            return;

        var now = Stopwatch.GetTimestamp();
        var stable = FrameStableClock;
        var state = new O2JamSyncState(stable.IsRunning, IsPaused.Value, stable.IsRewinding,
            stable.IsCatchingUp.Value, HasReplayLoaded.Value, stable.Rate);
        if (!syncSession.TryBeginSample(now, state))
            return;

        // Sample after children have processed the frame-stable clock used by the judged objects.
        // Send immutable numbers to the audio thread instead of accessing drawable clocks there.
        var sample = new O2JamSyncGameplaySample(now, syncTrack.SyncEpoch, state, stable.CurrentTime,
            syncNativeClock?.CurrentTime, syncTrack.CurrentTime, syncNativeClock?.TotalAppliedOffset, stable.ElapsedFrameTime, syncHits.Take());
        syncTrack.RequestSyncSample(syncSession, sample);
    }

    private void collectSyncHit(JudgementResult result)
    {
        // Parent LN/body bookkeeping is not a timed user hit. Only include the results that feed
        // the O2Jam judgement/UR pipeline, without recording keys or changing score state.
        if (result is O2JamJudgementResult && result.IsHit)
            syncHits.Add(result.TimeOffset);
    }

    private void revertSyncHit(JudgementResult _) => syncHits.Take();

    partial void disposeSyncDiagnostics()
    {
        NewResult -= collectSyncHit;
        RevertResult -= revertSyncHit;
        if (syncSession != null)
            syncTrack?.DetachSyncDiagnostics(syncSession);
        syncSession = null;
    }
}
#endif
