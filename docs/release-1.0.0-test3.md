# O2Lazer 1.0.0-test3

The third 1.0.0 test release targets **osu!lazer 2026.1005.0-lazer**.
The assembly version remains **1.0.0** to preserve ruleset identity.

## Changes since test2

- Restores still-active sample tails during compatible preview difficulty changes and replay seeks.
  Replay restoration follows recorded successful hits; missed notes remain silent.
- Restores preview sample tails when returning from gameplay or replay without restarting the song
  or duplicating already-active voices.
- Applies native master/music volume and inactive-window dimming to keysounds and background audio,
  independently of global effect volume and the beatmap hitsound toggle.
- Detects changed or replaced OJM sources, reuses unchanged OJN documents and directory context,
  and uses native audio-preparation completion scheduling.
- Adds bounded OJN/OJM and replay input/materialisation handling, validates imported score metadata,
  and preserves valid score data if saving a replay attachment fails.
- Separates shared patch coordination and read-only compatibility diagnostics, retaining
  ruleset-scoped ownership and the validated BMS coexistence boundaries.
- Makes core timing snapshots read-only, improves judgement-history restoration, removes unused
  localisation resources while retaining saved setting identities, and adds a narrow native
  selection fallback for stale pending charts when entering O2Lazer.

## Packaging and installation

The public DLL is built with **O2LazerTestBridge=false** and **O2JamSyncDiagnostics=false**.
It contains no local test bridge or NUnit test types; optional audio/gameplay sync-tracing hooks are not compiled.
The test bridge remains available only for explicitly instrumented local builds.

1. Close osu!lazer.
2. Replace the previous DLL in the data directory's rulesets folder with the attached
   osu.Game.Rulesets.O2Lazer.dll. Keep backups outside rulesets and install only one O2Lazer DLL.
3. Restart lazer. Keep the configured library and original OJN/OJM sources; clearing imports is
   not required for this upgrade.

SHA256SUMS.txt verifies the attached DLL. Marked v5 replays and existing saved scores remain supported;
unmarked pre-rewrite test replays remain unsupported.

## Validation and remaining limits

- Matching production sources passed 1,941 distinct filtered cases locally and in full GitHub host CI;
  native Realm, replay and old-settings cases run in separate processes. Source-boundary analysis is clean.
- User acceptance passed NoMS/MS gameplay, pause/resume, results/saving, replay seeking in both directions,
  and MS/HR/EZ colour transitions and SR presentation. Reason HX replay-to-preview restoration also passed.
- The two new accepted scores and replay attachments remained readable after a normal restart.
- Intermittent Mania-to-O2Lazer selection retention remains an observation; normal switches do not prove
  that every trigger has been eliminated.
- An isolated diagnostic confirms a native Realm cancellation/exception counter gap. The exact cause
  of the observed shutdown timeout remains unconfirmed; no global database patch is added.
- Cold asynchronous preparation may temporarily omit a sample tail. Global native PCM/copy peaks,
  host dependency advisories and broader device/library/ruleset combinations retain their documented limits.

This is a prerelease. The published test2 assets remain unchanged.
See the [current acceptance record](current-build-validation.md),
[maintenance delivery](maintenance-closeout-2026-10-14.md),
[audio contract](rate-mod-audio-readiness.md), and
[remaining work](documentation-audit-2026-10-08.md).
