# O2Lazer 1.0.0-test

This prerelease targets osu!lazer 2026.804.2 and packages the clean, independently structured
O2Lazer ruleset for wider testing. The Git tag carries the `-test` suffix; the persisted assembly
version remains `1.0.0` to preserve ruleset identity compatibility.

## Highlights

- Adds mania-compatible No Fail, Half Time, Daycore, No Release, Sudden Death, Perfect,
  Double Time, Nightcore, Fade In, Hidden, Cover, Flashlight, Accuracy Challenge, Random,
  Mirror, Invert, Constant Speed, Wind Up, Wind Down, Muted and Adaptive Speed mods.
- Preserves O2Jam chart-position judgement, exact note/hold types and OJM audio routing while
  reusing native mania mod presentation and behaviour where compatible.
- Applies HT/DT pitch settings to BGM and player keysounds, applies DC/NC pitch policy to both,
  and keeps dynamic rate-mod audio and visual scrolling on the same live speed.
- Makes all O2Jam score combinations ineligible for PP without Mania Score. With Mania Score,
  eligibility follows each selected mania mod's native ranking state. Mania Score remains hidden
  until its scoring and performance calculation are implemented.
- Adds the O2Lazer No Mod unranked-badge position and transition paths while retaining osu!'s
  native animations for all native destinations.
- Stores native mania stars separately from O2Jam level stars. Song select gains O2Jam Level sort
  and group options, while native star search, sorting and grouping continue to use mania stars.
- Removes the fixed-scroll setting; Constant Speed now exclusively owns that visual behaviour.
- Hides unsupported player settings, protects imported O2Jam charts from the native beatmap editor,
  and keeps OJM musical samples independent of the global effect-volume and beatmap-hitsound toggles.
- Preserves coexistence with the current BMS ruleset across both ruleset load orders.

## Installation and upgrade

1. Close osu!lazer.
2. Place `osu.Game.Rulesets.O2Lazer.dll` in the lazer data directory's `rulesets` folder, replacing
   the previous O2Lazer DLL. Keep backups outside that folder.
3. Start lazer and run **Settings -> O2Jam -> Refresh beatmaps** once. This populates the independent
   O2Jam/mania star metadata without replacing beatmap IDs or score associations.

The original `.ojn` and matching `.ojm`, `.omc` or `.m30` files must remain available. Replay data
from the pre-rewrite format is not supported, but existing score records are retained.

## Known limitations

- Mania Score is a hidden placeholder; mania scoring and PP calculation are not implemented.
- Easy, Hard Rock and Classic are deferred until Mania Score is implemented.
- The sample backend exposes Tempo but cannot time-stretch small sample channels. Pitch-preserving
  rate mods therefore keep small keysounds at their original pitch and duration while their trigger
  timing follows the gameplay clock.
- This is a test release. Back up the lazer database before testing library migration or refresh
  behaviour with irreplaceable data.

See the [behaviour specification](https://github.com/sumiyuki727/O2Lazer/blob/1.0.0-test/docs/o2jam-behaviour-spec.md),
[architecture notes](https://github.com/sumiyuki727/O2Lazer/blob/1.0.0-test/docs/clean-rewrite-architecture.md) and
[rate-mod audio notes](https://github.com/sumiyuki727/O2Lazer/blob/1.0.0-test/docs/rate-mod-audio-readiness.md)
for implementation boundaries and evidence.
