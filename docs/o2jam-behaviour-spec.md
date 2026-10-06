# O2Jam gameplay behaviour specification

Maintenance status (2026-10-06): current gameplay contract; remaining original-client features are deferred.
See the [documentation index](README.md) for current scope and evidence ownership.

Updated 2026-10-06 (alignment scope closed, EX full recording/zero-life lock/F and current native integrations). This specification records
the current implemented O2Jam gameplay policy and evidence for its revision; the
[architecture audit](architecture-audit.md) tracks implementation risks and acceptance gaps. It separates confirmed
reverse-engineered behaviour from compatibility choices. The reference implementations are not
compiled into or copied by O2Lazer.

The user selected the supplied Korean O2JamO2 and Classic clients as the primary behavioural
references on 2026-10-04. Their inspected binaries agree on integer-position judgement, a
25-tick release BAD limit, strict cumulative Jam thresholds and pill/hold ordering. C02–C05 are now
implemented. They also agree on single-buffer sample retriggering and
DirectSound attenuation parameters. See the [combined comparison](korean-client-comparison.md)
for versioned evidence, verified format coverage, the four aligned core rules and the remaining policy differences.
Remix is supporting evidence; DPJAM replacements and open-source interpretations do not override
the two selected binaries where their rules conflict. Selecting a reference does not itself change
the implementation. Position truncation and volume/pan changes remain deferred. After playing the
local O2JamO2 client, the user selected a scoped same-sample restart policy for gameplay; resolved
objects must still remain ineligible for later presses.

## Evidence

- The supplied O2JamO2 and Classic OTwo binaries are the primary target references. Their initial
  static inspection used in-memory decoding without running or extracting executable members.
  O2JamO2 was later deployed in an authorised local comparison environment and confirmed playable;
  Classic has not received that runtime verification. Hashes, addresses and limits are in the
  [combined report](korean-client-comparison.md) and [local setup](o2jamo2-local-play.md). Current
  Formats/Core source also read 163 OJN, 489 chart slots and 177 audio archives from these packages
  with no reader exceptions; sample-header checks are not audio playback or full lazer acceptance.
- [CXO2 commit e976ac2](https://github.com/SirusDoma/CXO2/tree/e976ac25e74cb45b537e9817dcb24bda7801f80a),
  inspected at the rewrite start, informed the current policy. It implements a render-position
  judgement strategy documented as
  O2Jam's native judgement and is based on reverse engineering of the original client. Its
  [position strategy](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/Judgements/RenderPositionJudgementStrategy.cpp)
  records its historical 6/18/25/24 boundaries; the selected current continuous windows differ as described below. Its independent
  [life implementation](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/LifeSystem.cpp)
  contains the exact 1000-point EX/NX/HX table, while its
  [score state](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/ScoreTracker.cpp)
  describes first-hit-zero combo, Jam, pill and post-depletion state transitions. Its continuous
  position, release-24 and pill/hold interpretation are not the authority for conflicts with the
  selected client binaries.
- [Open2Jam commit 11384b3](https://github.com/open2jamorg/open2jam/tree/11384b3ca957828ae66a72c9e28edd42c97952d5)
  is an independent older implementation. Its authors explicitly marked parts of scoring
  and judgement as uncertain, so it is supporting evidence rather than the authority.
- A [contemporary Chinese mission guide](https://o2jam.17173.com/renwu/renwu.htm) describes COOL
  200, GOOD 100, a 5000-point Jam fill and the 15-COOL pill rule. Later
  [Chinese](https://moegirl.uk/O2Jam) and [English](https://o2jam.fandom.com/wiki/Jam_combo)
  O2Jam references independently give the per-current-Jam bonuses of +10 for COOL and +5 for GOOD.
- Older O2Lazer and BmsRuleset implementations are historical context only; current rules are justified by the evidence and tests recorded here.
- Player observation confirms that the first COOL/GOOD displays `0` combo and the second displays
  `1` combo.
- The locally supplied Remix `OTwo.exe` is a supporting direct binary reference for one specific build.
  Its hash, inspected addresses and differences from the current policies are recorded in the
  [client comparison](remix-client-comparison.md). Runtime launcher modifications have not been checked.
- The supplied `DPJAM.7z` was inspected without running its programs or writing its executable
  members to disk. Its versioned `O2Hook2` timing replacement confirms integer-position judgement
  and a release BAD limit of 24 integer ticks; its pill/hold and Jam ordering differ from the
  current project policy. See the [DPJAM comparison](dpjam-client-comparison.md).
- The supplied `O2JamO2.zip` installer was read statically in memory. Its different OTwo build
  (PE timestamp 2008-06-17) confirms integer-position judgement, release BAD through 25 ticks,
  and the same strict Jam threshold and pill/hold ordering seen in Remix. No DPJAM hook components
  are listed in its payload. These findings apply to that binary, not all official clients;
  see the [O2JamO2 comparison](o2jamo2-client-comparison.md). Classic's different OTwo build
  (PE timestamp 2009-10-29) independently confirms these paths; see the combined report above.
  The initial comparison did not change defaults; the subsequent authorised implementation aligns
  release-25, strict Jam thresholds, raw-COOL pill progress and raw-BAD hold rejection.

## Compatibility choices

O2Lazer is an osu!lazer ruleset rather than a standalone O2Jam client. Confirmed O2Jam behaviour
remains authoritative for the mechanics which identify the game. When O2Jam has no equivalent for
a concept required by osu!, O2Lazer uses the closest mania/lazer contract and defines an O2-language
projection. When O2Jam clients and loaders do not agree on application lifecycle behaviour, the
native lazer behaviour is authoritative; pause and resume are the primary example.

Compatibility patches may correct a generic host assumption when it would hide or misrepresent an
O2Jam distinction. They remain scoped to O2Lazer and reuse native controls, animation, layout and
localisation so the ruleset keeps an independent identity without becoming a separate application
inside lazer. The detailed precedence and patch constraints are recorded in the
[architecture document](clean-rewrite-architecture.md#design-intent-and-decision-precedence).

## Position and judgement

One full measure is 192 O2Jam ticks. Judgement compares the current integrated chart position with
the endpoint's chart position; it is not a fixed millisecond window stored on the note.

| Result | Tap/hold head | Hold release | Boundary |
|---|---|---|---|
| COOL | `[-6, 7)` ticks | `[-6, 7)` ticks | early inclusive, late exclusive |
| GOOD | `[-18, 19)` ticks | `[-18, 19)` ticks | early inclusive, late exclusive |
| BAD | `[-25, 26)` ticks | `[-25, 26)` ticks | early inclusive, late exclusive |
| MISS | at or after +26 ticks | at or after +26 ticks | excluded late BAD edge |

Evaluate COOL, then GOOD, then BAD. An earlier tap/head outside BAD is ignored;
an explicit release outside BAD is a MISS. Passive inspection only commits MISS.
The rule compares continuous offsets, including negative positions and fractional-tick
targets; it never truncates the position clock or subtracts a synthetic timing offset.
TimingMap inversion uses a `1e-7` tick boundary tolerance so numeric round-off cannot
turn an exact excluded edge into a hit. This is not integer position quantisation.

At a constant BPM and 1×, the intervals in milliseconds are:

```text
COOL = [ -7500 / BPM,  +8750 / BPM )
GOOD = [-22500 / BPM, +23750 / BPM )
BAD  = [-31250 / BPM, +32500 / BPM )
```

Heads and releases share the same bounds. Around BPM changes, convert each endpoint
through the full TimingMap instead of multiplying by the note's single BPM. Native
HitWindows and MaximumJudgementOffset expose the larger early/late millisecond envelope
for input/lifetime infrastructure; Core decides the actual asymmetric result and timeout.
Native Mania note-lock ordering remains unchanged. MS continues to use native Mania.

### Timing discretisation in reference clients

Both selected Korean OTwo binaries calculate a floating-point chart position, convert it to an
integer with truncation towards zero, then compare the integer distance against 6, 18 and 25 ticks.
For non-negative chart positions, this conversion is equivalent to `floor`. The supplied DPJAM
`O2Hook2.dll` replacement also truncates position, but uses 24 for release BAD. Position quantisation
and the release constant are separate compatibility decisions.

For an endpoint at integer tick `T` in non-negative chart position, the selected clients' COOL
interval in continuous position is:

```text
T - 6 <= position < T + 7
```

The earlier O2Lazer symmetric continuous policy was:

```text
T - 6 <= position <= T + 6
```

These clients can therefore classify almost one additional late tick as COOL. One tick is `1250 / BPM`
milliseconds (approximately 10.42 ms at BPM 120, 8.33 ms at BPM 150 and 6.25 ms at BPM 200), so
this difference can materially move clustered late inputs across the COOL/GOOD boundary even
though the displayed constants are identical.

This integer-tick truncation is not common to the inspected open-source clients:

| Client | Judgement domain | Explicit integer-tick truncation | Remaining discretisation |
|---|---|---:|---|
| DPJAM / O2Hook2 | chart ticks | yes | truncated positive chart position |
| Supplied Remix OTwo binary | chart ticks | yes | truncated positive input position; integer timing nodes |
| Supplied O2JamO2 OTwo binary | chart ticks | yes | truncated positive input position; integer timing nodes |
| Supplied Classic OTwo binary | chart ticks | yes | truncated positive input position; integer timing nodes |
| [Open2Jam](https://github.com/open2jamorg/open2jam/blob/11384b3ca957828ae66a72c9e28edd42c97952d5/src/org/open2jam/game/judgment/BeatJudgment.java) | beat distance (`double`) | no | update/input sampling |
| [O2Game](https://github.com/Estrol/O2Game/blob/7cf8f5b52ebff2a7e46e02b51122271a1c182304/Game/src/Engine/Judgements/BeatBasedJudge.cpp) | milliseconds (`double`) | no | frame-delta clock and input sampling |
| [CXO2](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/Judgements/RenderPositionJudgementStrategy.cpp) | render position (`double`) | no | integer-millisecond clock and frame input polling |
| [raindrop](https://github.com/zardoru/raindrop/blob/662dd11f05994f6f36493575b04ecb64b04dcd7b/src/VSRGMechanics.cpp) | beat distance (`double`) | no | update/input sampling |

Integer milliseconds and frame sampling can still quantise observations, add jitter or create an
offset, but they do not create this fixed late-side one-tick expansion. The two selected binaries
provide direct evidence in their own main programs; this does not establish behaviour for all OTwo
versions. On 2026-10-05 the user selected explicit continuous asymmetric intervals
`[-6,7)`, `[-18,19)` and `[-25,26)` rather than position truncation. They reproduce the
selected clients' bounds for non-negative positions and integer-tick endpoints. Negative
positions and fractional endpoints follow the same explicit intervals; bit-for-bit equivalence
to the original clients is not claimed for those cases.

The Core rule version is `20261005`. Newly exported v5 replay envelopes include
`judgement_version`; unknown nonzero rule versions are rejected. Existing untagged v5
recordings are test data and are rejudged by the current rule at the user's request. Their
saved scores are not rewritten, but replay playback totals can differ at changed boundaries.
No historical judgement branch or position-quantised clock is retained.

### Current audio policy versus selected clients

Player-triggered gameplay KeySounds share one active voice per OJM sample identity within the
current gameplay. An early empty hit on an unresolved tap/LN head remains allowed. Another eligible
trigger stops the previous voice and starts a new native SampleChannel at the sample beginning;
the first judgement-triggered sound also replaces any early voice. Eligibility belongs to the
object: after the tap or LN head is resolved, later presses cannot select it again. A different
unresolved object using that sample remains eligible. This also applies to O2Lazer's MS profile;
ordinary Mania and other rulesets retain their native selector and playback behaviour.

Native SkinnableSound restarts its own instance but Mania's column pool and judgement drawables
use separate instances. Host.Audio coordinates only OJM channels under the current drawable
ruleset, using native Stop and new-channel playback. It does not implement judgement or change
note/sample mapping, zero/missing samples, volume or pan. Weak voice references are cleared on
seek and gameplay disposal. Pausing freezes the latest active voice at its position; resume does
not restart it or revive a replaced voice. Replay seek discards old tails and native lifetime
rewind restores object eligibility.

This is the user's selected C06 policy, not full original-client equivalence: the original client
can retrigger judged objects, while O2Lazer intentionally rejects them. Scheduled preview,
background and future automatic-KS paths keep their existing concurrency and synchronisation.
O2Lazer still translates authored volume/pan into linear gain/Balance; the selected binaries use
DirectSound attenuation (deferred C07). Effect-volume independence and faithful replay input
remain separate host contracts. Native small samples still cannot restore an already-started
tail when seeking into its middle; that accepted limitation is unchanged.

## Hit error display

In the O2Jam profile, the coloured range is **−25 to +26 O2Jam ticks**.
Early/late COOL, GOOD and BAD extents are **6/7, 18/19 and 25/26 ticks**. The
early COOL half is anchored to half the native Mania OD7 Great (yellow 300) colour
bar at 1×. The extra late tick uses the same scale; COOL's total width therefore
grows by 1/12 rather than being rescaled to its former total width.

One tick remains 7.25 projection units. The native symmetric axis envelope is ±26
ticks (188.5 units), with one transparent spare tick on the early side so zero remains
at the native centre. Legacy's axis is 301.6 local units; its coloured extent is 295.8,
and COOL extends −34.8 to +40.6 around zero. These are native local drawable units,
not physical screen pixels. Argon's root length is multiplied by 188.5/130.5; native
early/late colour drawables get separate extents. Native gradients, thickness, settings,
marker pooling and animations remain in use. BPM, rate and OD do not alter the tick scale;
skin/HUD scaling can still change the on-screen size.

The Integration projection converts each endpoint's chart-clock input time through its
integrated timing map and subtracts the endpoint's chart position. This preserves early/late
direction, BPM changes inside a window and playback-rate behaviour without multiplying the
already-rate-adjusted native offset again. The native bar draws the final judgement colour,
so a pill-rescued BAD can appear as a COOL-coloured marker in the physical BAD region.
BAD maps to HitResult.Meh for judgements and statistics, reusing native 50 yellow in
meters and results without a colour patch. Its O2Jam raw score remains 4; Mania Score
retains native judgement keys. Old Ok statistics are not aliased or migrated.

The display reuses native Legacy/Argon bars, pools, fading, moving-average animation and
seek clearing. Only those exact native component types with O2Jam object windows receive
the projection. Mania Score and ordinary Mania continue to use native millisecond bars;
non-positional colour meters and custom derived components retain their original contract.
In Mania Score, HT/DC/DT/NC delegate window compensation to native `IManiaRateAdjustmentMod`
after conversion. Both actual endpoint judgement and the native bar use those compensated
windows; the O2Jam tick display never overrides MS windows. Argon follows its own native
normalised layout in MS, while Legacy changes width with the native millisecond windows.
There are no visible numeric tick labels added by this change.

The native **UR number remains a millisecond statistic** (rate-corrected standard deviation
times ten). `JudgementResult.TimeOffset`, score HitEvents, replay frame times and the
gameplay clock preserve their raw timing precision. Tick bar geometry changes only
the display; the asymmetric rule itself belongs to Core, and its millisecond envelopes
belong to Integration. No display windows are assigned to playable objects.

The temporary client hit-error probe was removed after user acceptance. Current validation
and historical observations are recorded in the [display report](hit-error-display-validation.md).

## Playback rate

The gameplay position clock integrates effective BPM. For a constant rate modifier:

```text
effective BPM = authored BPM * playback rate
```

This naturally changes the real-time width of the judgement window. No independent hit-window
rate multiplier is applied.

HT/DC use a default rate of 0.75 and DT/NC use 1.5. HT and DT route the rate through Tempo by
default, preserving BGM and keysound pitch; enabling Adjust Pitch moves the same value to
Frequency for both. Daycore and Nightcore keep Frequency at their native default (0.75/1.5) and
use Tempo for any custom-speed difference. Nightcore also retains mania's beat-synchronised
percussion overlay. Gameplay-triggered OJM keysounds receive these adjustments through a scoped
drawable-ruleset dependency, independently of global effect volume and unrelated UI samples.

Continuous BGM layers and discrete keysound events do not need the same DSP operation to implement
this policy. Tracks apply Tempo to their continuous timeline. A keysound's onset follows the
rate-adjusted O2Jam event clock; without Adjust Pitch its authored pitch and sample envelope are
preserved, while Frequency changes both when pitch adjustment is requested. This is the intended
event-sample contract and is not an audio desynchronisation by itself.

## Combo

- Internal combo starts at `-1`; COOL and GOOD increment it by one.
- BAD and MISS reset it to `-1` (the break sentinel).
- Displayed and persisted combo is `max(internal combo, 0)`.
- therefore the first COOL/GOOD displays 0 and the second displays 1.
- maximum combo tracks the maximum displayed combo.

The live processor bindable retains the sentinel so a successful `-1 -> 0` is distinguishable from
a break. Scoped presentation adapters clamp native counters to zero and preserve mania's increment
animations. The combo-break effect receives only actual breaks, not the first successful endpoint.
Persisted `ScoreInfo.Combo`/`MaxCombo` are nonnegative. The native theoretical maximum-combo display
subtracts one for O2Lazer only; stored endpoint counts and earned MaxCombo are not altered.

The framework-internal `JudgementResult.ComboAfterJudgement` snapshot is not the authority for
O2Jam gameplay or HUD state. No patch is installed solely to rewrite that diagnostic field.

## Life

Life starts at 1000 and is clamped to `[0, 1000]`.

| Difficulty | COOL | GOOD | BAD | MISS |
|---|---:|---:|---:|---:|
| EX | +3 | +2 | -10 | -50 |
| NX | +2 | +1 | -7 | -40 |
| HX | +1 | 0 | -5 | -30 |

The following is the implemented depletion contract. The [zero-life comparison](zero-life-and-failure-analysis.md)
records confirmed static differences and the remaining runtime/server boundaries for the selected binaries.
It does not claim complete equivalence with either client or with special modes.

On 2026-10-05 the user selected complete recording rather than the original clients' partial freezes.
Without No Fail, the judgement which reduces life to zero is fully applied and locks life at zero.
EX keeps scoring, combo/maximum combo, Jam/maximum Jam, pill progress/consumption, native judgement
counts and accuracy active until the chart finishes. Life cannot recover. Pills still convert BAD
to COOL and affect score/combo/Jam, while their resolved life increase remains zero.

EX depletion does not trigger the native fail animation or stop gameplay. When populating its score,
the host uses native `ScoreRank.F` and `Passed=false`, independently of the accuracy-derived live
rank. This preserves complete results and replay without latching the native live rank; seeking back
before depletion restores an unlocked state and an accuracy-derived passing grade.

NX/HX still disable domain scoring and request the native failure sequence at zero life; the gameplay
clock stops when the native fail animation completes. The core snapshot's `LifeLockedAtZero` describes
an unsuccessful zero-life attempt, while `HasFailed` requests ending playback. The resolved core flag
is the health adapter's authority; untyped fallback results retain the native EX exemption.

Failed EX scores remain historical records. Future local/server personal-record aggregates must
exclude `Rank == F`, not erase these scores or infer eligibility from accuracy/total score. Native
`ScoreInfo.Passed` is transient; its persisted F grade is the durable marker. Formal v5 replay loaders
restore the transient passed flag from that grade. No new Realm field, replay envelope version or
personal-record UI is introduced. Existing scores are not rewritten; playback uses the current
scoring policy, and timing judgement version remains unchanged.

With No Fail enabled, all three difficulties continue at zero life without freezing score, maximum
combo, Jam or pills, and subsequent judgements can restore life. The native failure override keeps
the player running. The native score pipeline applies mania's 0.5 multiplier to the raw O2Jam score;
the unmultiplied score remains available as `TotalScoreWithoutMods`. Disabling No Fail restores the
depletion rules above.

## Jam and pills

Jam promotion and pill progress follow the selected binaries. The exposed meter retains its
normalised 100-unit scale: COOL +4 / GOOD +2 is twice the clients' +2 / +1. Strictly exceeding 100
and subtracting 100, preserving the remainder, is equivalent to comparing cumulative client
progress against `50 * (current Jam + 1)` without storing host-specific presentation state.

- Jam progress uses 100 internal units: COOL +4 and GOOD +2.
- BAD and MISS reset progress and the current Jam Combo.
- reaching exactly 100 does not promote; exceeding 100 subtracts 100 and increases Jam Combo by one.
- uninterrupted COOLs promote after hits 26, 51, 76, etc.; the next hit uses the new bonus.
- fifteen consecutive raw COOL judgements award one pill, up to five.
- GOOD, BAD and MISS reset the consecutive-COOL pill progress.
- when scoring is enabled, one pill converts one BAD endpoint into COOL before score, life, combo
  and Jam are updated.
- a rescued BAD resets the raw-COOL streak to zero and does not count as its first hit.
- the scored result is COOL for score, life, combo, Jam, statistics, endpoint display and HUD;
  the original BAD remains available for pill-streak and long-note continuation decisions.

The domain snapshot permanently exposes Jam progress, Jam Combo, maximum Jam Combo, consecutive
COOL progress and pill count even before a dedicated HUD is implemented.

## Score

Score is accumulated per endpoint using the Jam Combo active when the endpoint is resolved:

```text
COOL = 200 + 10 * current Jam Combo
GOOD = 100 +  5 * current Jam Combo
BAD  = 4
MISS = -10, with total score clamped to zero
```

The note which fills the Jam meter is scored using the previous Jam Combo, then advances the
meter. This agrees with contemporary and later player documentation and the independent Open2Jam
event order. CXO2's current aggregate score getter instead recomputes all prior COOL/GOOD points
using total completed Jams; that conflicts with its own current-Jam callback/state and descriptions
that a broken Jam resets COOL value to 200. The rewrite therefore treats that aggregate getter as a
CXO2 discrepancy rather than native behaviour. Score policy remains isolated so an original-client
golden replay can still override it without changing judgement, HUD or presentation code.

## Accuracy and rank

Non-MS play supplies accuracy and letter rank because they are part of the common osu! score and
results contract, even though the inspected O2Jam evidence does not define equivalent values.
O2Lazer deliberately defines them in O2Jam terms rather than copying mania's judgement weights:

```text
accuracy = (200 * COOL + 100 * GOOD + 4 * BAD) / (200 * judged endpoints)
```

MISS contributes zero. Every tap, long-note head and long-note release is one endpoint. The result
therefore measures the realised proportion of O2Jam's base judgement value after pill conversion,
independently of Jam bonuses, combo, the remaining pill count and the raw score's `-10` MISS penalty.
For example, one COOL and one GOOD produce 75% accuracy.

The resulting value uses osu!'s common grade boundaries: X at 100%, S from 95%, A from 90%, B from
80%, C from 70% and D below 70%; a failed play is F. These are intentionally O2Lazer compatibility
metrics: osu! supplies the concepts and result presentation, while O2Jam supplies the values being
measured. With Mania Score selected, both accuracy weights and rank calculation are instead delegated
to the native mania score processor. Native rank-adjusting mods may decorate a passing grade with
their ordinary osu! variant, such as XH or SH.

### Mod behaviour

No Release disables release timing only while a long note is still held when its tail reaches the
judgement point. That tail resolves as COOL and releases the held state; an early key-up is judged
by the ordinary O2Jam release windows. Fade In, Hidden and Cover alter visibility around each
mania column without replacing O2Jam hit objects. Flashlight and Accuracy Challenge use their
native generic playfield and score-processor paths.

Invert replaces each column's source note locations with O2Jam long notes between successive
locations. Each duration follows mania's rule: the greater of half the gap or the gap minus a
quarter beat. It removes breaks and preserves the source timing map, head sample identity and
silent O2Jam tail. Wind Up, Wind Down and Adaptive Speed update the gameplay clock, visual scroll
compensation and player-triggered keysound rate from one live speed value. Muted uses the native
combo-driven song/hitsound volume and optional metronome behaviour. These mods retain mania's
settings, incompatibilities, score multipliers and intrinsic ranking state.

### Performance eligibility

O2Jam scoring has no independent PP formula. The current performance entry delegates to native Mania only with MS; Mod selection and score displays use the following
eligibility policy independently of the gameplay score calculation:

| Selected mods | PP eligibility |
|---|---|
| Any selection without Mania Score, including No Mod | Ineligible |
| Mania Score alone or with compatible ranked configurations of No Fail, HT, DC, Mirror, Sudden Death, Perfect, DT, NC, Fade In, Hidden, Cover, Flashlight, Accuracy Challenge or Muted | Eligible |
| Mania Score with No Release, Random, Invert, Constant Speed, Wind Up, Wind Down, Adaptive Speed or Autoplay | Ineligible |

With Mania Score selected, any mod whose native mania `Ranked` property is false makes the
combination ineligible. The selection is re-evaluated on mod/settings changes; stored scores use
their own mod lists. Neither individual mod properties nor other rulesets' eligibility are changed.
Mania Score is a visible Conversion mod. It converts O2Jam objects to native mania objects before
difficulty mods run, then delegates judgement, scoring, combo, health, rank, result statistics,
difficulty and performance calculation to mania. Its integrated OD and HP settings default to 7;
unchanged defaults are ranked and edits follow mania's native unranked difficulty-adjust policy.
Easy, Hard Rock and Classic require MS: selecting or carrying one into O2Lazer selects MS, removing
MS removes those dependent mods, and removing only a dependent mod leaves MS selected.

Using a mod for this complete scoring-profile switch is deliberate. It follows lazer's precedent of
using Conversion mods such as Classic to expose a legacy rules interpretation while retaining one
ruleset identity, library and native score/replay representation.

The song-select button reuses osu!'s unranked badge. No Mod places it at the upper left above MODS,
without widening the button; an ineligible nonempty combination places it at the upper right.
These visible positions switch by horizontal movement only, with button width changing at the
same time in both directions. Eligible nonempty combinations retain the native hidden position
under the mod bar (`X=-badge.DrawWidth, Y=-5`). Native No Mod outside O2Lazer hides at `Y=20`
and retains its previous horizontal target. Transitions between these three native destinations
use the original osu! animations, even within O2Lazer.

Only transitions into or out of O2Lazer No Mod use custom badge movement, including ruleset changes:
upper left fades down to lower left before relocating horizontally to native No Mod while hidden;
the reverse relocates to lower left while hidden, then fades upwards. Entering from the hidden
mod-bar position first moves down instantly, then left instantly, then fades upwards. Returning to
that position fades down first, then moves right instantly, then up instantly. Invisible relocation
never precedes fade-out completion. All custom animations last 240 ms with `OutQuint` easing.

Synchronous ruleset/mod notifications are collapsed before custom movement starts. Repeated
refreshes do not restart custom animations. Interruptions replace transforms from current values,
as in osu!: partial fades reverse without resetting alpha or position, and an interrupted horizontal
slide can fade down in its current column. Obsolete fade-completion relocations are cancelled.
The native margin is preserved, and native badge animation resumes after the custom exit completes.

## Long notes

Hold control keeps the original head accuracy, separately from the scored endpoint result, matching
the selected binaries. Judgement, pills and hold policy remain independent Core decisions.

- head and release are independently judged endpoints;
- an unpressed long note produces two MISS endpoints;
- a BAD head without a pill also terminates the hold and produces a MISS release endpoint;
- a pill-rescued BAD head scores COOL but still terminates the hold and forces a MISS release;
- releasing an active hold before the fast release window produces MISS immediately;
- a release which passes the late release boundary produces MISS;
- head and release use the same continuous BAD interval `[-25,26)`; positions are not truncated.

The presentation may use mania's hold hierarchy, but these domain endpoint events remain the sole
source of scoring and replay truth.

The head-continuation decision is also a domain rule rather than drawable policy: raw COOL and GOOD
begin the hold; raw BAD and MISS terminate it with a forced MISS release. O2JamHoldState retains
the scored head accuracy and original head accuracy. Hosts applying pill conversion pass the full
O2JamResolvedJudgement to ResolveHead; the single-accuracy overload is for unconverted results.
The bridge reads both values from the native endpoint resolution, including during rewind.

The forced tail MISS goes through the same score/health/result pipeline exactly once. It cannot be
rescued by a pill, and No Release does not turn a rejected head into an active hold. Drawables keep
consuming the domain outcome for clipping and lifetime rather than deciding this rule themselves.

### Long-note presentation

When the O2Jam long-note visual option is enabled, endpoint resolution and drawable lifetime are
separate. An early release (COOL, GOOD, BAD, pill-rescued COOL, or MISS) stops the holding effect and
resolves scoring immediately, but the remaining body and tail retain their colour and keep
scrolling. Per the user's subsequent refinement, only resolved COOL/GOOD (including pill-rescued
COOL) continue clipping at the judgement line. Unrescued BAD and MISS freeze the clipping bounds
at release and let the remainder fall past the line. Head rejection uses the original accuracy:
a raw BAD/MISS head, including a pill-rescued BAD, never starts clipping. Release clipping uses
the scored tail accuracy, not mania's IsHit (which also includes BAD). Successfully
released heads must not stay pinned after the body ends; dropped heads scroll out naturally.
The retained object is recycled after the charted tail passes; visual retention never extends the
logical hold, sound playback, or judgement window. With the option disabled, successful tails
retain mania's immediate hiding and missed holds retain the existing grey dropped-note visual,
including native clipping of a rescued head whose scored result is COOL.
The head KeySound is eligible for its first judgement only. A later press while the resolved LN
remains visible must not replay it; replay rewind before the head restores that eligibility.

This policy was requested on 2026-08-31 based on CXO2's `EventState.IsRenderable()` (LN visibility
depends on chart position, not endpoint accuracy) and Open2Jam's `TO_KILL` handling (judged LNs
scroll out of the window rather than being removed immediately). It is reference-supported
behaviour with a user-selected BAD/MISS clipping policy, not an original-client golden-test confirmation. No reference implementation code is
copied into the ruleset.

## Seeded column transforms

RD retains native mania random as its default, including for existing replays with no `algorithm`
setting. Its settings offer Random, R-Random (mirrored rotations included) and S-Random, which
uses the existing Panic raw-measure/protected-segment algorithm (`Panic = 2`). Algorithm names
and explanations stay in English. The earlier note-by-note `SRandom = 4` and retired O2Jam
fixed-shuffle identities retain their implementations and replay layouts; the former has no
menu entry even while selected. Its header reads S-Random (legacy), without rewriting its value.
The retained note-shuffle constraints and current menu contract are defined in the
[randomisation contract](column-randomisation.md).
Both supplied clients use the same seeded MSVC recurrence and seven full-range swaps; their
seven-key special random shares raw-measure and LN/boundary protection rules. The algorithms
are pure Integration transforms rather than judgement Core behaviour. See the
[evidence and persistence contract](original-random-and-failure-analysis.md) for addresses,
known seed range, empty-measure consumption, algorithm identities and fallback limits.

Imported raw package positions survive conversion and MS object replacement without quantising
judgement time. All algorithms retain note identity, hold endpoints, samples and automatic audio;
only columns change. Native Seed and ModsJson own configuration/replay persistence. Automatic
checks cover deterministic replay restoration and original vectors. The user closed the selected
alignment work on 2026-10-06 and deferred the remaining differences; this does not retrospectively
confirm every unreported visual or runtime case. EX uses the implemented full-recording, zero-life
lock and native F contract above. Remaining reference-client features are documented observations,
not an active implementation backlog; see the [closure review](recent-changes-review.md).
