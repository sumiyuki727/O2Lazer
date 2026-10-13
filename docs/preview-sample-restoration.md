# Preview sample restoration

Reviewed against master `6bcc05e` on 2026-10-08. The user accepted the current preview/background-volume and replay-seek work within the tested scope. The earlier common-voice retention baseline is `ab07d019a80faf69878184ae8c66776b91044236`; its DLL remains in `.artifacts/preview-transfer-baseline` for historical comparison.

## Native boundary and scope

Native `SampleChannel` supports playback but no position seek. Existing live channels are retained by full event identity and occurrence count. Missing past events are reconstructed through the existing native OJM `TrackStore`, using `Track.Seek`, native gain/pan/adjustments and the same resource lease. These tracks are separate from BGM identity and do not alter note-to-sample mapping, missing-zero handling, global effect-volume independence or score/replay inputs.

Chart-driven restoration applies only to Preview: same-song difficulty transfer, preview seek, late schedule classification and returning from gameplay/replay. Replay now has a separate judgement-driven entry described in [replay sample restoration](replay-sample-restoration.md); live Gameplay and GameplayAutomatic do not reconstruct past playable chart events.

## Preparation and lifecycle

`IO2JamPlaybackResource.TryGetSampleLength` queries native cached sample decoder metadata. The real skin submits missing preparation to the existing bounded asynchronous preload scheduler and returns pending without waiting. Missing/failed/unsupported samples resolve to zero duration. No new codec or direct BASS calls are used.

2026-10-13 preparation update: resource acquisition and completion of the native preparation
attempt are separate. The ruleset's four scheduler slots remain occupied through the latter;
sample readiness/length waits for a complete native store update, while drawable lookup may
clone an acquired sample earlier. Urgent preview requests are collected before speculative
requests dispatch. This is a ruleset preparation bound, not a global native decoder or PCM
limit, and does not trim intrinsic low-signal intros or alter event time. See the
[source merge and limited acceptance](ojm-source-and-resource-plan.md#音频准备调度源码合入2026-10-13有界听感通过).

Past events are grouped by sample ID, most recent first. At most four duration groups are probed concurrently. Only events whose native duration covers the current chart time are queued for a seekable stream. At most four restore candidates are advanced per audio update. Late stream preparation seeks to the then-current offset rather than the original transfer offset. Ready BGM and KS continue while this work proceeds; missing tails can remain absent until preparation completes.

Identical native sample voices and previously reconstructed tracks consume the target occurrence count before missing voices are queued. Simultaneous duplicate events remain independent. New schedule, seek, mode change and disposal invalidate pending playback intent. Shared resource preparation may finish into the existing cache after invalidation but cannot play stale events. Paused restoration creates stopped tracks; native resume starts them. Completed and unselected restored streams are disposed, and lease release follows child-track disposal.

## Cost and limitations

There is no added deliberate wait for preview to become playable. Cold metadata discovery can still read/decode historical samples asynchronously, in bounded batches; grouping does not eliminate that CPU/I/O or native cache memory cost. Only still-active events create playback streams. Each reconstructed voice adds native decoder/mixer work, and its first preparation may leave that layer temporarily absent. No benchmark or numerical performance promise has been made.

Grouping and sorting occur when a schedule/cursor changes, not on every frame. Unknown metadata is queried from recent events towards old history. Future cheap format duration metadata could reduce cold-cache work, but must not replace native codec handling with speculative byte-length estimates.

## Validation

Current acceptance (2026-10-08): the user reported no obvious missing preview audio; retimed handoff addresses the separately reported intermittent switching interruption within its stated bounds. Native inactive-window volume and the subsequent replay seek restoration were accepted. The phase-specific observations below remain historical evidence rather than outstanding requests to repeat the same tests.

Filtered audio tests cover delayed metadata without holding the clock, gain/pan and offset, pause/resume, expired samples, superseding schedules, seek boundaries, duplicate retention and gameplay exclusion. Host architecture checks must remain clean.

Use the local test bridge to seek and switch same-set difficulties, then search runtime logs for `O2Lazer preview sample restored`. Each entry reports sample ID, event time, resolved offset and native duration. This proves stream creation intent; it cannot prove audible quality. Listen for restored long tails, duplicated attacks and pause/resume desynchronisation. Also verify regular gameplay and formal replay still honour judgement-triggered KS.

Initial 2026-10-08 validation: 61 filtered audio/resource tests passed; architecture checks reported zero errors/violations. Live bridge testing on `o2ma3952` (326 OJM samples) covered HX→EX→HX, preview seek and pause/resume. Logs show native sample 22 (18941.746 ms) restored at offset 13468.865 ms; the installed code also restored sample 5 (14991.565 ms) after a seek near 67 seconds. Original difficulty and preview position were restored. Listening acceptance was pending at that point and subsequently supplied as recorded above; these logs alone do not constitute an audio-output assertion.

## Native inactive-window volume

Detached KS sample stores bypass the native global effect store intentionally, but must inherit `AudioManager.AggregateVolume` plus music volume. Binding only raw `AudioManager.Volume` omitted native inactive-window adjustments and made KS louder relative to BGM/restored tracks in the background. The corrected binding uses the native aggregate; no independent focus timer or fade is added. Native lazer fades to configured inactive volume over 4000 ms and returns over 400 ms. Regression coverage checks inactive gain, newly created samples, foreground recovery and independence from effect volume; 63 filtered resource/preview tests passed. The user subsequently confirmed the focus-transition fix. This does not cover every audio device or configuration.

## Retimed-voice handoff

User listening confirmed no obvious missing audio but reported intermittent interruption during difficulty switching. Runtime logs retained the live track and showed sub-millisecond schedule application, yet HX/NX transitions retained zero voices and stopped dozens. Slightly different event starts prevented exact identity matches.

Full identity matching stays unchanged. After exact matches consume their occurrences, unmatched preview voices can temporarily bridge a target event with the same sample, gain and pan, within 100 ms of the source event start and already in the past. Preview plays both automatic and playable events, so transient audio handoff may bridge those roles; the correctly authored target role belongs to the replacement. Incoming occurrences are reserved independently, without stealing exact matches or merging simultaneous duplicates.

The outgoing voice continues until its correctly positioned replacement starts, then is stopped/disposed. A 250 ms wall-clock limit bounds failed preparation. New schedule, seek, mode policy and disposal also release stale outgoing voices. This is a finite asynchronous handoff, not a permanent tolerance-based reinterpretation of note timing. No promise is made that every interruption is resolved by this cause alone. Transfer diagnostics now report `handoff` separately from immediately `stopped` voices.

Candidate lookup is indexed by sample ID for each transfer; reconstructed voices reuse their own native track length without requesting sample metadata again. The delayed-replacement test verifies outgoing audio remains until the incoming track is ready and seeks to its authored offset. Final validation: 60 filtered audio tests passed, architecture checks were clean. Live HX→NX→HX testing reported 48 and 38 handoff voices with zero immediately stopped voices in those two transitions. Schedule application was 7.362/1.012 ms in that run; these are observations, not a performance guarantee or an audible continuity assertion. The prior paused difficulty and position were restored for user listening acceptance.

## Returning from gameplay/replay (2026-10-13 candidate)

R12 user listening found missing audio after leaving Reason HX replay, with later recovery.
The shared event track resumes at the retained song position. Changing its policy previously
only reset the future playable-event cursor and discarded replay restoration intent; charted
sample attacks before that position were never reconstructed. Future attacks can make the
missing layers appear to recover later.

The Preview policy transition now invokes the existing bounded chart restoration queue.
Existing automatic channels and restored voices consume matching event occurrences, so
live stems are retained without duplicate reconstruction. Replay-restored voices are first
removed because recorded judgement timing must not persist into a chart-driven preview.
Stopped preview restoration remains stopped until native resume; expired events create
no stream, pending work is invalidated when the policy changes again, and repeated assignment
of Preview does not restart restoration. BGM clocks, note/KS mapping and volume routes are unchanged.

Six new policy-transition cases join the existing audio regression group, including recorded
late hits, paused/running return, live automatic retention, and cancellation of delayed metadata.
The combined audio/library filtered group passed 245 cases. After installing this build,
the user confirmed Reason HX replay-exit preview sounded normal. Two exits in the native
runtime log show preview restoration, including sample 5 resumed at its current chart offset.
This is limited listening acceptance for that scenario; it does not establish all-device or
all-chart coverage. See [current build validation](current-build-validation.md).
This does not remove cold native decoder preparation cost or promise zero audible latency.
