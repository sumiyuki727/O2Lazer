# O2Jam gameplay behaviour specification

This specification is the behavioural authority for the clean rewrite. It separates confirmed
reverse-engineered behaviour from compatibility choices. The reference implementations are not
compiled into or copied by O2Lazer.

## Evidence

- [CXO2 commit e976ac2](https://github.com/SirusDoma/CXO2/tree/e976ac25e74cb45b537e9817dcb24bda7801f80a),
  inspected at the rewrite start, implements a render-position judgement strategy documented as
  O2Jam's native judgement and is based on reverse engineering of the original client. Its
  [position strategy](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/Judgements/RenderPositionJudgementStrategy.cpp)
  explicitly records the 6/18/25/24 boundaries used below. Its independent
  [life implementation](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/LifeSystem.cpp)
  contains the exact 1000-point EX/NX/HX table, while its
  [score state](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/ScoreTracker.cpp)
  confirms first-hit-zero combo, Jam, pill and post-depletion state transitions.
- [Open2Jam commit 11384b3](https://github.com/open2jamorg/open2jam/tree/11384b3ca957828ae66a72c9e28edd42c97952d5)
  is an independent older implementation. Its authors explicitly marked parts of scoring
  and judgement as uncertain, so it is supporting evidence rather than the authority.
- A [contemporary Chinese mission guide](https://o2jam.17173.com/renwu/renwu.htm) describes COOL
  200, GOOD 100, a 5000-point Jam fill and the 15-COOL pill rule. Later
  [Chinese](https://moegirl.uk/O2Jam) and [English](https://o2jam.fandom.com/wiki/Jam_combo)
  O2Jam references independently give the per-current-Jam bonuses of +10 for COOL and +5 for GOOD.
- The existing O2Lazer and BmsRuleset implementations are functional references only.
- Player observation confirms that the first COOL/GOOD displays `0` combo and the second displays
  `1` combo.

## Position and judgement

One full measure is 192 O2Jam ticks. Judgement compares the current integrated chart position with
the endpoint's chart position; it is not a fixed millisecond window stored on the note.

| Result | Tap/hold head | Hold release | Boundary |
|---|---:|---:|---|
| COOL | +/- 6 ticks | +/- 6 ticks | inclusive |
| GOOD | +/- 18 ticks | +/- 18 ticks | inclusive |
| BAD | +/- 25 ticks | +/- 24 ticks | inclusive |
| MISS | later than the BAD boundary | later than the BAD boundary | exclusive after BAD |

An input earlier than the fast BAD boundary is ignored. A release before the release window is a
MISS because releasing an active hold is an explicit attempt.

At a constant BPM, the tap/head windows are equivalent to:

```text
COOL =  7500 / BPM ms
GOOD = 22500 / BPM ms
BAD  = 31250 / BPM ms
```

The hold-release BAD window is `30000 / BPM ms`.

At a BPM boundary the window can be asymmetric in milliseconds. The position comparison remains
symmetric and integrates each BPM segment traversed by the early or late side. This is required
for charts which change BPM during a judgement window.

### Timing discretisation in reference clients

Static inspection of the locally supplied DPJAM `O2Hook2.dll` found a separate compatibility
behaviour which must not be conflated with the native tick constants. Its timing hook calculates a
floating-point chart position, converts that position to an integer with a truncating conversion,
then compares the integer distance against 6, 18 and 25 ticks. For non-negative chart positions,
this conversion is equivalent to `floor`.

For an endpoint at integer tick `T`, the resulting DPJAM COOL interval in continuous position is:

```text
T - 6 <= position < T + 7
```

The continuous interpretation implemented by O2Lazer is instead:

```text
T - 6 <= position <= T + 6
```

DPJAM can therefore classify almost one additional late tick as COOL. One tick is `1250 / BPM`
milliseconds (approximately 10.42 ms at BPM 120, 8.33 ms at BPM 150 and 6.25 ms at BPM 200), so
this difference can materially move clustered late inputs across the COOL/GOOD boundary even
though the displayed constants are identical.

This integer-tick truncation is not common to the inspected open-source clients:

| Client | Judgement domain | Explicit integer-tick truncation | Remaining discretisation |
|---|---|---:|---|
| DPJAM / O2Hook2 | chart ticks | yes | truncated positive chart position |
| [Open2Jam](https://github.com/open2jamorg/open2jam/blob/11384b3ca957828ae66a72c9e28edd42c97952d5/src/org/open2jam/game/judgment/BeatJudgment.java) | beat distance (`double`) | no | update/input sampling |
| [O2Game](https://github.com/Estrol/O2Game/blob/7cf8f5b52ebff2a7e46e02b51122271a1c182304/Game/src/Engine/Judgements/BeatBasedJudge.cpp) | milliseconds (`double`) | no | frame-delta clock and input sampling |
| [CXO2](https://github.com/SirusDoma/CXO2/blob/e976ac25e74cb45b537e9817dcb24bda7801f80a/src/CXO2/Core/Judgements/RenderPositionJudgementStrategy.cpp) | render position (`double`) | no | integer-millisecond clock and frame input polling |
| [raindrop](https://github.com/zardoru/raindrop/blob/662dd11f05994f6f36493575b04ecb64b04dcd7b/src/VSRGMechanics.cpp) | beat distance (`double`) | no | update/input sampling |

Integer milliseconds and frame sampling can still quantise observations, add jitter or create an
offset, but they do not create DPJAM's fixed late-side one-tick expansion. The DPJAM hook is also
not direct evidence that every official OTwo version used the same conversion: it replaces the
client timing routine rather than exposing the original implementation. O2Lazer consequently
retains continuous integrated-position judgement as the behavioural default. A future DPJAM
compatibility option may isolate integer-tick truncation at the position-clock/judgement boundary
without changing the authored 6/18/25/24 constants.

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

The judgement which reduces life to zero is applied before scoring is disabled. NX/HX fail and
stop gameplay. EX continues to accept/display judgements but score, life, Jam and pill state are
frozen after reaching zero. Its live combo still changes, while maximum combo is also frozen.

With No Fail enabled, all three difficulties continue at zero life without freezing score, maximum
combo, Jam or pills, and subsequent judgements can restore life. The native failure override keeps
the player running. The native score pipeline applies mania's 0.5 multiplier to the raw O2Jam score;
the unmultiplied score remains available as `TotalScoreWithoutMods`. Disabling No Fail restores the
depletion rules above.

## Jam and pills

- Jam progress uses 100 internal units: COOL +4 and GOOD +2.
- BAD and MISS reset progress and the current Jam Combo.
- reaching 100 wraps the progress and increases current Jam Combo by one.
- fifteen consecutive COOL judgements award one pill, up to five.
- GOOD, BAD and MISS reset the consecutive-COOL pill progress.
- when scoring is enabled, one pill converts one BAD endpoint into COOL before score, life, combo
  and Jam are updated.
- the rescued result is COOL in every projection: score, statistics, replay, endpoint data and HUD.

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

O2Jam scoring has no planned PP calculator. Mod selection and score displays use the following
eligibility policy independently of the gameplay score calculation:

| Selected mods | PP eligibility |
|---|---|
| Any selection without Mania Score, including No Mod | Ineligible |
| Mania Score alone or with compatible ranked configurations of No Fail, HT, DC, Mirror, Sudden Death, Perfect, DT, NC, Fade In, Hidden, Cover, Flashlight, Accuracy Challenge or Muted | Eligible |
| Mania Score with No Release, Random, Invert, Constant Speed, Wind Up, Wind Down, Adaptive Speed or Autoplay | Ineligible |

With Mania Score selected, any mod whose native mania `Ranked` property is false makes the
combination ineligible. The selection is re-evaluated on mod/settings changes; stored scores use
their own mod lists. Neither individual mod properties nor other rulesets' eligibility are changed.
Mania Score remains registered for eligibility presentation and stored scores, but its selection
UI is currently hidden using the native unimplemented-mod filter. Mania scoring and PP calculation
are not implemented, and gameplay continues to use the O2Jam score model above.

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

- head and release are independently judged endpoints;
- an unpressed long note produces two MISS endpoints;
- a BAD head without a pill also terminates the hold and produces a MISS release endpoint;
- a pill-rescued BAD head becomes COOL and permits the hold to continue;
- releasing an active hold before the fast release window produces MISS immediately;
- a release which passes the late release boundary produces MISS;
- the release BAD boundary is 24 ticks rather than the head's 25 ticks.

The presentation may use mania's hold hierarchy, but these domain endpoint events remain the sole
source of scoring and replay truth.

The head-continuation decision is also a domain rule rather than drawable policy: resolved COOL
and GOOD begin the hold, while resolved BAD and MISS terminate it with a forced MISS release. This
ordering is why a pill-rescued BAD may continue but an ordinary BAD may not.

### Long-note presentation

When the O2Jam long-note visual option is enabled, endpoint resolution and drawable lifetime are
separate. An early release (COOL, GOOD, BAD, pill-rescued COOL, or MISS) stops the holding effect and
resolves scoring immediately, but the remaining body and tail retain their colour and keep
scrolling. Per the user's subsequent refinement, only resolved COOL/GOOD (including pill-rescued
COOL) continue clipping at the judgement line. Unrescued BAD and MISS freeze the clipping bounds
at release and let the remainder fall past the line; a rejected BAD/MISS head never starts
clipping. Use the resolved accuracy, not mania's IsHit (which also includes BAD). Successfully
released heads must not stay pinned after the body ends; dropped heads scroll out naturally.
The retained object is recycled after the charted tail passes; visual retention never extends the
logical hold, sound playback, or judgement window. With the option disabled, successful tails
retain mania's immediate hiding and missed holds retain the existing grey dropped-note visual.

This policy was requested on 2026-08-31 based on CXO2's `EventState.IsRenderable()` (LN visibility
depends on chart position, not endpoint accuracy) and Open2Jam's `TO_KILL` handling (judged LNs
scroll out of the window rather than being removed immediately). It is reference-supported
behaviour with a user-selected BAD/MISS clipping policy, not an original-client golden-test confirmation. No reference implementation code is
copied into the ruleset.
