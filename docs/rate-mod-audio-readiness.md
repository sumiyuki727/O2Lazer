# Rate-mod audio routing

Inspected against the locally referenced osu!lazer 2026.804.2 and framework 2026.731.0 APIs.
Half Time, Daycore, Double Time and Nightcore are exposed with mania-compatible presentation,
grouping, settings, ranking state and score multipliers. Routing tests validate native adjustment
values and propagation. Manual gameplay testing has not found a rate-related OJM onset, pitch or
pause/resume defect.

## Native primitives

`IAdjustableAudioComponent` distinguishes two multiplicative adjustments:

- `Frequency` changes pitch and playback speed together.
- `Tempo` changes speed while preserving pitch, where the backend implements it.

Track rate is `AggregateFrequency * AggregateTempo`. There is no separate public pitch-only
property. A native track can combine the two adjustments to obtain an independent pitch/rate pair.

| O2Lazer path | Native primitive | Frequency | Tempo | Current playback contract |
|---|---|---|---|---|
| BGM and automatic samples at least 512 KiB | `Track` / `TrackBass` | Supported | Supported via BASS FX | Each active track binds adjustments from `O2JamPreviewTrack`; seek restoration binds newly created tracks too. |
| Smaller automatic keysounds; playable keysounds in Song Select | `ISample.GetChannel()` / `SampleChannelBass` | Supported | Accepted by the interface but not applied by this backend | Onsets follow the event clock. Without pitch adjustment, each discrete sample retains its authored pitch and envelope. |
| Player-triggered tap/LN-head keysounds | `PausableSkinnableSound` -> `DrawableSample` -> `SampleChannelBass` | Supported by the underlying component | Accepted by the interface but not applied by this backend | Judgement triggers the event at the rate-adjusted chart time; Frequency applies when a pitch change is requested. |

`O2JamPreviewTrack.UpdateState()` already drives its virtual clock at the aggregate rate. Its
`play()` and `playBackground()` methods bind channel/track adjustments rather than capturing a
one-time numeric value. `SampleChannelBass.OnStateChanged()` applies volume, balance and frequency,
while `TrackBass.OnStateChanged()` also sets BASS FX Tempo. This difference is intentional at the
playback-contract level: a continuous track changes its timeline, while a discrete keysound changes
tempo through its scheduled onset and keeps its authored envelope unless Frequency is requested.
Binding the Tempo value to a sample channel keeps the routing policy explicit, but does not claim
that the backend time-stretches the sample body.

The gameplay adapter binds only O2Jam endpoint `SkinnableSound` instances, rather than the complete
`DrawableRuleset.Audio` tree. This keeps global UI/effect sounds and Nightcore's native percussion
independent while allowing live HT/DT and WU/WD/AS Adjust Pitch changes and custom rates to reach
future event onsets. Frequency changes also reach voices which are already active.
Visual scroll compensation binds directly to `SpeedChange` and does not claim a second native audio
helper target.

## Implemented native mod semantics

| Mod | Default song speed | Default song Frequency | Default song Tempo |
|---|---:|---:|---:|
| Half Time | 0.75 | 1 | 0.75 |
| Daycore | 0.75 | 0.75 | 1 |
| Double Time | 1.5 | 1 | 1.5 |
| Nightcore | 1.5 | 1.5 | 1 |

Half Time and Double Time use `RateAdjustModHelper`; their native Adjust Pitch setting switches
between Tempo and Frequency. Daycore and Nightcore hold Frequency at their default 0.75/1.5 and
set Tempo to `selected speed / default speed`, retaining their pitch when custom speed changes.
Native mania Nightcore also adds a beat-synchronised percussion overlay.

Wind Up, Wind Down and Adaptive Speed expose the same live `SpeedChange` and Adjust Pitch model.
Their native `IApplicableToTrack` path drives the O2Jam event clock and all background/automatic
audio. The scoped endpoint adapter binds Frequency to `SpeedChange` when Adjust Pitch is enabled,
or records Tempo when it is disabled. In the latter case, the changed event clock schedules future
keysound onsets while existing sample envelopes remain authored. Visual scroll compensation observes
the same speed bindable, so it does not introduce a second rate calculation.

`ModRateAdjust.ApplyToSample()` applies Frequency equal to the selected speed, including for Half
Time and Double Time. This differs from pitch-preserving song playback and from Daycore/Nightcore
at custom speeds. Native ordinary `DrawableHitObject` sample playback does not call this method;
the inspected native caller is storyboard-sample playback. Inheriting the mod or setting
`SamplesMatchPlaybackRate` alone does not connect O2Jam gameplay keysounds.

## Discrete-event sample semantics

`SampleChannelBass` does not implement Tempo time-stretching. This does not establish a current
desynchronisation: OJM keysounds are one-shot events whose musical time is defined by their onset on
the O2Jam chart clock. Under default HT/DT they retain their authored pitch and envelope while those
onsets move with the selected rate. Enabling Adjust Pitch applies Frequency as requested. DC/NC apply
their native fixed pitch ratio, while a custom-speed remainder changes event spacing.

This differs from a long continuous BGM layer, whose internal timeline must be rate-adjusted and
seekable. Automatic samples of at least 512 KiB therefore use `TrackBass` and its Tempo DSP. Treating
the two primitives differently is part of the current audio model and agrees with manual tests. A
future requirement to time-stretch the body of every melodic key sample would be a policy change,
not a repair implied by the existing Tempo bindable; it would also have to preserve overlap, latency,
preloading, seek and disposal behaviour.

## Evidence and regression coverage

Relevant O2Lazer files:

- `Clean/Audio/O2JamPreviewTrack.cs`: event clock, sample/track bindings and seek restoration.
- `Clean/Audio/O2JamBeatmapSkin.cs`: stream classification, native resource factories and volume routing.
- `Clean/UI/O2JamDrawableRuleset.cs` and `Clean/Audio/O2JamHitSoundRateAdjustments.cs`: visual rate and scoped gameplay keysound policy.
- `Normal/Clean/O2JamPreviewTrackLifecycleTest.cs`: the four local rate mods reach the clock,
  background and automatic-sample aggregate adjustments; active changes and recreated tracks
  retain adjustments. Fake channels validate routing, while manual tests cover audible behaviour.
- `Normal/Clean/O2JamRateModTest.cs`: gameplay keysound defaults, live Adjust Pitch, custom DC/NC speed,
  playable beatmap conversion, standard and dynamic gameplay keysound routing, and rate values.

Read-only upstream references: `osu.Game/Rulesets/Mods/ModRateAdjust.cs`, `RateAdjustModHelper.cs`,
`ModDaycore.cs`, `ModNightcore.cs`, `osu.Game/Skinning/SkinnableSound.cs`,
`osu.Game/Rulesets/Objects/Drawables/DrawableHitObject.cs`, and framework
`Audio/Track/TrackBass.cs`, `Audio/Sample/SampleChannelBass.cs`.
