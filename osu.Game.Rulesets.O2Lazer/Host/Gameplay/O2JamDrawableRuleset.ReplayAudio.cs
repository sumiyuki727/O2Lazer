using System;
using osu.Game.Rulesets.Judgements;
using osu.Game.Rulesets.O2Lazer.Audio;

namespace osu.Game.Rulesets.O2Lazer.UI;

public partial class O2JamDrawableRuleset
{
    private readonly O2JamReplayKeySoundHistory replayKeySounds = new();
    private bool replayAudioSeekPending;

    private void bindReplayAudio()
    {
        NewResult += collectReplayKeySound;
        RevertResult += replayKeySounds.Revert;
        if (gameplayClock != null)
            gameplayClock.OnSeek += markReplayAudioSeek;
        hitSoundRateAdjustments.ReplaceRestoredSample = sampleId =>
        {
            if (HasReplayLoaded.Value)
                gameplayTrack?.ReplaceRestoredReplaySample(sampleId);
        };
    }

    private void markReplayAudioSeek() => replayAudioSeekPending = true;

    private void collectReplayKeySound(JudgementResult result)
    {
        if (HasReplayLoaded.Value && !AutomaticallyPlayKeySounds)
            replayKeySounds.Apply(result);
    }

    protected override void UpdateAfterChildren()
    {
        base.UpdateAfterChildren();
        updateReplayAudio();
        sampleSyncDiagnostics();
    }

    partial void sampleSyncDiagnostics();

    private void updateReplayAudio()
    {
        if (!replayAudioSeekPending || !HasReplayLoaded.Value || gameplayClock == null || gameplayTrack == null
            || AutomaticallyPlayKeySounds || FrameStableClock.IsCatchingUp.Value || FrameStableClock.WaitingOnFrames.Value)
            return;
        // OnSeek precedes replay catch-up. Wait for the native replay clock, including its final
        // sub-200 ms catch-up frames, before selecting historical hits for the destination.
        if (Math.Abs(FrameStableClock.CurrentTime - gameplayClock.CurrentTime) > 0.001)
            return;
        replayAudioSeekPending = false;
        // Native catch-up may already have played recent hits with a late attack. Reposition
        // those voices too, rather than mixing a second stream over the catch-up channel.
        hitSoundRateAdjustments.StopActiveChannels();
        gameplayTrack.RestoreReplayKeySounds(replayKeySounds.At(FrameStableClock.CurrentTime), FrameStableClock.CurrentTime);
    }

    private void unbindReplayAudio()
    {
        if (gameplayClock != null)
            gameplayClock.OnSeek -= markReplayAudioSeek;
        NewResult -= collectReplayKeySound;
        RevertResult -= replayKeySounds.Revert;
        hitSoundRateAdjustments.ReplaceRestoredSample = null;
        replayKeySounds.Clear();
    }
}
