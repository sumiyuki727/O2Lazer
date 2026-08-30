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

## Playback rate

The gameplay position clock integrates effective BPM. For a constant rate modifier:

```text
effective BPM = authored BPM * playback rate
```

This naturally changes the real-time width of the judgement window. No independent hit-window
rate multiplier is applied.

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
