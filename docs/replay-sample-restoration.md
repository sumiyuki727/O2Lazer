# Replay sample restoration

## Native gap and scope

Native SampleChannel cannot seek into a long OJM stem. Stopping gameplay channels on replay seek prevents stale tails, but loses samples triggered before the destination that should still be audible. Replay restoration reuses the bounded native TrackStore/Track.Seek pipeline added for preview, with replay-specific eligibility. It adds no replay parser, judgement algorithm, archive fields or Core dependencies.

## Eligibility and clocks

The host ruleset observes native NewResult and RevertResult. Successful results snapshot resolved O2JamHitSampleInfo and actual judgement time; misses do not create playback intent. Reverted results are removed. Native nested LN heads contribute their authored samples; silent tails/body bookkeeping contribute none. Per-sample selection retains the latest valid hit at or before the destination, matching gameplay's existing same-sample voice replacement. Zero/missing sample mapping remains upstream and unchanged.

OnSeek marks restoration pending. After children process native replay input and judgement updates, the frame-stable clock must reach the offset-aware gameplay clock (within 0.001 ms), with neither catch-up nor missing frames. The final backward frame is allowed once it reaches the destination, so paused rewind can restore without waiting for a positive frame. Musical sample channels started during native catch-up are stopped to avoid doubling late attacks with restored streams.

The song/gameplay clock offset is captured before posting the immutable hit snapshot to the audio thread. Stream offsets advance with the live song clock while resources prepare. Automatic sample events use song time, skip active exact occurrences and remain independent from judgement voice replacement. Streamed background layers continue using the existing native seek path.

## Lifecycle and boundaries

Seek cancels pending reconstruction and disposes restored voices. New native musical-channel requests supersede both pending and active restored playable voices for that sample, without stopping automatic stems. Pause/resume and rate/pitch adjustments reuse the same native track lifecycle as preview restoration. Leaving replay releases restored voices and unsubscribes native result/seek observers. Live gameplay does not accumulate replay history or invoke historical restoration; GameplayAutomatic retains its existing policy.

Cold duration/stream preparation is asynchronous and bounded to four probes/attempts per audio update. It can leave a stem briefly absent; no deliberate global audio wait or numerical performance promise is added. The history models successful judgement-triggered samples, not reconstruction of unjudged empty-key presses. Replay timing/mod fidelity continues to come from native replay processing.

## Validation

2026-10-08: 85 filtered audio/gameplay/replay tests passed; semantic architecture checks reported zero compilation errors, violations and stale exceptions. The test DLL was installed and the user confirmed replay seek listening without problems. This is acceptance of the tested cases, not a guarantee across every chart/device. Native inactive-window volume was separately accepted by the user.

Filtered tests cover supplied-hit eligibility, gameplay/song offsets, pause/resume, pending and active cancellation on seek, native hit replacement, automatic voice deduplication, misses, future hits and reverted history. Architecture checks must remain clean. Unit state assertions do not prove audible output.

For listening acceptance, play a replay with long KS, seek forward into a known sustained sample, rewind across its trigger, and repeat while paused before resuming. Verify missed notes stay silent, later hits replace the same KS without doubled tails, and HT/DT plus background volume retain their policies. Search runtime logs for `O2Lazer replay sample restored` to inspect event time, sample offset and native duration. The local test bridge currently controls song selection, not replay gameplay, so client replay listening remains user-driven.
