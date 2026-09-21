# O2Lazer 1.0.0-test

This prerelease targets osu!lazer 2026.804.2 and packages the clean, independently structured
O2Lazer ruleset for wider testing. The Git tag carries the `-test` suffix; the persisted assembly
version remains `1.0.0` to preserve ruleset identity compatibility.

## Highlights

- Adds mania-compatible No Fail, Easy, Half Time, Daycore, No Release, Sudden Death, Perfect,
  Hard Rock, Double Time, Nightcore, Fade In, Hidden, Cover, Flashlight, Accuracy Challenge,
  Random, Mirror, Mania Score, Classic, Invert, Constant Speed, Wind Up, Wind Down, Muted and
  Adaptive Speed mods.
- Preserves O2Jam chart-position judgement, exact note/hold types and OJM audio routing while
  reusing native mania mod presentation and behaviour where compatible.
- Applies HT/DT pitch settings to BGM and player keysounds, applies DC/NC pitch policy to both,
  and keeps dynamic rate-mod audio and visual scrolling on the same live speed.
- Makes all O2Jam score combinations ineligible for PP without Mania Score. With Mania Score,
  eligibility follows each selected mania mod's native ranking state. Mania Score is a visible
  Conversion mod which delegates judgement, scoring, combo, health, rank, difficulty, result
  statistics and PP to native mania. Its integrated OD/HP settings default to 7; Easy, Hard Rock
  and Classic require MS and participate in the same selection policy.
- Defines non-MS accuracy from O2Jam base judgement values and presents the result with osu!'s
  common letter-grade boundaries, providing native score/result fields without replacing O2Jam
  raw score, Jam, pills or life rules.
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
from the pre-rewrite test format is not supported, but existing score records are retained. Those
test builds used an immature replay model; omitting speculative compatibility readers keeps the
release implementation small. Clean replay schema v5 is now frozen and has no planned format change.

## Known limitations

- Dedicated Jam and pill HUD components are not yet implemented; the gameplay state and score
  projections already retain their complete values.
- The native beatmap editor is intentionally unavailable. Lossless OJN/OJM editing would require a
  much broader private patch surface than the maintained playback and library integration.
- Imported charts continue to reference their external OJN/OJM audio archive, so the source library
  must remain available.
- This is a test release. Back up the lazer database before testing library migration or refresh
  behaviour with irreplaceable data.

See the [behaviour specification](o2jam-behaviour-spec.md),
[architecture notes](clean-rewrite-architecture.md) and
[rate-mod audio notes](rate-mod-audio-readiness.md)
for implementation boundaries and evidence.
