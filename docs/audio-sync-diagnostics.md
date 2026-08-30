# O2Jam audio sync diagnostic build

This build observes timing without changing hit windows, chart timestamps, clock sources,
audio offsets, preloading, volume routing, or playback control. It is not a sync fix.

Enable only when building a diagnostic DLL:

```powershell
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
dotnet build ./osu.Game.Rulesets.O2Lazer/osu.Game.Rulesets.O2Lazer.csproj -c Release `
  "-p:OsuBinaryDirectory=$lazerBinaries" -p:O2JamSyncDiagnostics=true
```

The flag defaults to off. Audio tracing calls are unimplemented partial methods in ordinary
builds and are removed by the compiler; the UI probe and its optional reflection are excluded.
Ruleset name, assembly name/version, variant, beatmap identity and replay format are unchanged.

## User test

1. Close osu!lazer. Back up the currently installed O2Lazer DLL outside the `rulesets` directory.
2. Replace only `osu.Game.Rulesets.O2Lazer.dll` with the diagnostic DLL. Do not keep two versions
   under different filenames inside `rulesets`, or copy bundled osu!/mania/Harmony assemblies there.
3. Keep existing audio settings and offsets unchanged during the comparison.
4. Play a chart which reproduces the issue at the usual difficulty and rate. The original report
   used `[荣誉]战争的艺术` / Mephisto, `SongC/o2ma3033.ojn`.
   Play for about 40 seconds, pause for a few seconds, resume for about 20 seconds, then retry once.
   Report whether the early bias is present immediately, grows during play, or changes after resume.
5. Exit normally to flush logs. Supply the newest `*.runtime.log` from the lazer data directory's
   `logs` folder. Review unrelated native log entries for private information before sharing.

No reimport, metadata refresh, database reset, or offset calibration is required.
The trace is enabled in this DLL without changing a user setting. No input keys or account data
are recorded by this probe, and nothing is transmitted. Other native runtime log entries are unchanged.

## Trace interpretation

Search for `[O2SYNC/v1]`. Each loaded gameplay drawable has a unique process-local `session`.
The `gameplay-attach` header identifies the beatmap/difficulty, module MVID, WASAPI setting,
and whether the actual native clock source matches the track found by the preview coordinator.
Retries that reuse the drawable/track are still identified by control events and epochs.

- `judgement_ms`: frame-stable time actually supplied to judged drawables, sampled after children.
- `parent_ms`: native offset-aware FramedBeatmapClock time, not the outer drawable's application clock.
- `request_virtual_ms`: event track time read on the update thread alongside the judgement clock.
- `virtual_ms`: event track time read when the audio thread handles the observation.
- `virtual_rate` / `event_rate`: internal virtual-clock rate and event-track rate respectively.
- `total_offset_ms`: actual native total applied offset, or `unknown` if unavailable. A guarded,
  read-only lookup obtains the protected native clock once; no field is modified.
- `origin_ms + position_ms = chart_ms`: BGM source event time plus its native track position.
  Native positions are already in track/chart milliseconds: do not multiply them by rate again.
- `lead_virtual_ms = chart_ms - virtual_ms`: positive means the BGM's reported chart position
  is ahead of the event clock. This is **not** a direct measurement of sound arriving at the ear.
- `hit_mean_ms`, `hit_min_ms`, `hit_max_ms`, `hits`: summary of timed O2Jam hit results since the
  previous sample. Negative means early. Misses and parent/body bookkeeping are excluded.
  Reverted results clear the interval summary; replay/catch-up samples are flagged separately.
- `mono_ticks`, `request_ticks`, `tick_frequency`, `queue_ms`: one Stopwatch time base for both
  threads. Raw cross-thread positions must not be subtracted without considering sample age.
- `epoch`, `request_epoch`, `pair_stable`: controls invalidate simple cross-thread extrapolation.
  A stable pair also requires a short queue, matching running state/rate, and no replay/rewind/catch-up.

For a stable running pair only, `judgement_ms + queue_ms * request_rate` is an approximate
judgement time at the audio observation. Frame interpolation, frame age, and cached TrackBass
position updates still limit its precision. Do not infer a fixed global compensation from a
single sample; compare sustained trends and the native applied offset.

Lifecycle events include `attach`, `mode`, `start-request`, `started`, `stopped`, `seek`,
`rebuild`, `bgm-created`, `schedule`, and `detach`. Seek/rebuild can describe the same operation.
The event epoch advances even if a burst log is suppressed.

## Performance and safety bounds

- Steady sampling is once per wall-clock second, including while paused.
- State transitions may be sampled after 100 ms; only one audio observation may be pending.
- Lifecycle logging is capped at eight records per session per second, with suppression counts.
- Each record includes at most four BGM layers and explicitly reports the omitted count.
- Only scalar snapshots cross from update to audio thread. The audio-thread observer reads
  existing cached TrackBass properties; it never forces an update/seek/stop or queries amplitude.
- No file I/O or synchronous cross-thread waits are introduced by the observer. Framework logging
  handles output asynchronously, with console/UI listeners disabled for these trace records.

## Validation and historical observations

Tests cover wall-clock throttling, bounded pending work, transition/event bursts, sign conventions,
rate-independent BGM timeline mapping, hit-statistic reset, invariant-culture trace fields,
and unchanged audio position/start/stop behaviour during observation.

During development a combined test run twice aborted in Realm's native synchronization-context
callback. The Realm groups passed in isolation and the ordinary build passed all 310 normal tests.
After isolating the diagnostic test's log sink from global host logging, the diagnostic build
passed all 311 normal tests. This is a test-run observation, not a claimed fix to Realm internals
or evidence that an in-game Realm issue has been resolved.

At the time of that investigation, the final diagnostic Release build passed 311 filtered normal tests, and its
exported DLL was inspected to confirm the diagnostic hooks are present. Neither the installed
DLL nor the existing ordinary export was overwritten. Runtime audio-device behaviour still
requires the user's gameplay test above. These historical counts are not a claim about the current
commit. Normal source builds keep diagnostics disabled; current verification instructions are in
[development.md](development.md).
