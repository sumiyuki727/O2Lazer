# Local lazer test bridge

This opt-in instrument follows the named-pipe/update-thread approach of the read-only `D:\vibe mapping` reference. It does not depend on that project, its editor, or legacy `.osu` encoding. All instrumentation lives in `TestSupport`, outside Core, Formats and production Host code.

## Build isolation

Normal Debug and Release builds exclude the entire source directory and bootstrap call. Enable explicitly with `-p:O2LazerTestBridge=true`; this is a local test artifact, not a release asset. No extra package dependencies are added. Build production again with `-p:O2LazerTestBridge=false` before packaging. Do not distribute the instrumented DLL as a release.

Example (matching installed lazer binaries):

```powershell
& .artifacts/dotnet/dotnet.exe build osu.Game.Rulesets.O2Lazer/osu.Game.Rulesets.O2Lazer.csproj -c Release -p:O2LazerTestBridge=true "-p:OsuBinaryDirectory=$env:LOCALAPPDATA\osulazer\current"
```

Restart lazer with this DLL installed. Song select must load after bridge installation. Client commands use `TestSupport/Invoke-LazerTest.ps1 -LazerProcessId <pid> -Command status`.

## Protocol 1

One JSON line per connection, response `{success, protocol, data}` or `{success:false, protocol, error}`. Pipe `o2lazer-test-<pid>` is restricted to the current OS user and does not share the editor bridge endpoint. Requests are bounded to 4096 characters and ten seconds. Scheduled commands that expire before execution cannot later change the client.

- `capabilities`: available commands, process identity; does not require song select.
- Client `start`: launch the explicitly selected installed lazer executable, or report its existing PID. It checks adjacent host assemblies to avoid launching stable. Launch acknowledgement does not mean the bridge or song select has loaded.
- `open-song-select`: invoke native main-menu song-select navigation; already being in solo song select is accepted. Poll status for readiness.
- `exit`: native `OsuGame.AttemptExit`, accepted only at main menu or solo song select. The client script polls `exit-state`, confirms only an ordinary ready exit dialog without ongoing operations, and waits up to twenty seconds for process closure. Ongoing operations return a manual-confirmation requirement. Native acknowledgement is a request, not proof of closure. Never force-terminate the client.
- `exit-state`: reports native confirmation visibility, ongoing operations and whether ordinary confirmation is ready.
- `confirm-exit`: calls native `PerformOkAction` only for `ConfirmExitDialog` with a `PopupDialogOkButton` and no ongoing operations. Other dialogs and dangerous task-abort buttons are rejected.
- `rulesets`: installed ruleset short names and display names.
- `set-ruleset`: set the same global ruleset bindable used by the native selector; requires `ruleset` and `expectedRuleset`. Filtering, conversion and resulting selection remain native and asynchronous.
- `status`: current selected beatmap ID, native music time/length, running and loaded state.
- `difficulties`: IDs and metadata for the selected set, including hidden markers.
- `select`: visible available difficulty in the local library and same ruleset, via native `BeatmapManager.QueryBeatmap` and `SelectAndRun`. This also permits restoring the test selection after a restart; protected/deletion-pending sets are rejected.
- `play`, `pause`, `seek`: native `MusicController` methods, including native user pause semantics.

Audio/selection commands require `expectedBeatmapId` matching the current selection. `select` also requires `beatmapId`; `seek` requires finite `timeMs` within the loaded track. The PowerShell client exposes these as `-ExpectedBeatmapId`, `-BeatmapId`, and `-TimeMs`. Editing, mods, gameplay entry and player replay control are not provided.

## Non-gameplay acceptance commands

The following opt-in commands use native services in the running client. They are excluded from production along with the rest of `TestSupport`; no Core/Formats dependencies on instrumentation are introduced. Database instrumentation lives in `TestSupport/Persistence/Realm`; the storage source check permits only this test boundary after verifying the opt-in compilation guard.

- `inspect-data`: queue a read-only Realm snapshot. Returns counts and deterministic fingerprints of score metadata/associations/attachments, beatmap identities and collections, plus at most 32 recent O2Lazer scores with attachments. Native star-cache and last-played updates are intentionally outside the identity fingerprint. Managed models never cross the pipe.
- `library-state`: report the configured source path and refresh availability. Acquires the same config-owned settings session as the settings UI; initial preference/collection synchronisation may run normally.
- `refresh-library`: queue the existing settings session's refresh, including its progress/completion notification. Uses the existing application queue, backend and difficulty pipeline. No source path, clear or delete command is exposed. Job completion alone is insufficient to assert successful refresh: the UI session handles its exceptions internally, so also inspect native completion/error logs and data fingerprints.
- `read-replay -ScoreId <guid>`: queue native `ScoreManager.GetScore` for an available local O2Lazer score. Reports frame count, endpoints and a fingerprint of times/actions. This validates decoding, not playback judgement or audible continuity.
- `export-replay -ScoreId <guid>`: queue native `ScoreManager.Export`; verify the resulting export exists and its bytes against the saved attachment. An acknowledgement alone does not prove an export was written.
- `import-replay -ReplayPath <path>`: queue native score import of an existing local `.osr`. Reports accepted model count, native notification state and source retention. The test invocation does not post its batch progress notification; individual native importer notifications remain native. This can write scores/attachments, so use known valid controls or an isolated database for malformed inputs.
- `job-status -JobId <guid>`: report the queued operation's running state, result and error. Only one acceptance job may run at a time; its identity must match. Starting a new job replaces the last completed result, so save results first.

Long jobs run off the update thread and return a job ID before the ten-second request deadline. A request which expires before dispatch still cannot start a job later. Once acknowledged, the native operation continues independently of the pipe connection. Wait for completion before exit, DLL replacement or another acceptance job.

Read/audio/ruleset commands require current solo song select. Navigation requires main menu or solo song select and runs on the always-active game scheduler rather than a suspended screen scheduler. The bridge uses weak references captured after native song-select and main-menu `LoadComplete`. It adds only postfixes under its own Harmony owner and uses lifecycle targets different from the editor bridge's entry/resume hooks. Private host methods are version-sensitive; startup failure rolls back only its own patches and leaves gameplay available.

Examples:

```powershell
& TestSupport/Invoke-LazerTest.ps1 -Command start
& TestSupport/Invoke-LazerTest.ps1 -LazerProcessId <pid> -Command open-song-select
& TestSupport/Invoke-LazerTest.ps1 -LazerProcessId <pid> -Command rulesets
& TestSupport/Invoke-LazerTest.ps1 -LazerProcessId <pid> -Command set-ruleset -ExpectedRuleset o2lazer -Ruleset mania
& TestSupport/Invoke-LazerTest.ps1 -LazerProcessId <pid> -Command exit
```

Control acknowledgements mean accepted/queued, not that audio-thread work completed. Poll status afterwards. Selection acknowledgement verifies the selected identity, not completion of every asynchronous resource load. Keep lazer foreground for native integration testing. Metadata and playback state cannot prove audible sample continuity or visual correctness; listening, observation and audio diagnostics remain necessary.

Before automatic exit, read `exit-state` and wait for native ongoing operations to
finish. Native `ConfirmExitDialog` chooses its buttons when loaded: a task-abort
warning remains dangerous after tasks finish. The bridge deliberately refuses that
button; cancel/exit manually instead of repeatedly requesting exit. The 2026-10-11
monthly user-tag maintenance exposed this case. Do not equate absence of notified
operations with absence of all native background load.

Capture the selected mode and actual source ruleset from `difficulties`, not just
the mode label and a real ID. A startup conversion/filtering interval may expose a
source that cannot be restored from a later selection through this bridge. The
revised isolated comparison waits for stable mode-owned selection before replacing
any DLL and excludes converted initial snapshots; this restriction belongs to the
test executor and does not change native conversion. Restore installation, selection
and initial closed/running state independently, preserving any failures. See the
[executor preparation](ojm-source-and-resource-plan.md#原生对照执行器修订准备2026-10-11未运行);
private-state checks do not substitute for actual client restoration acceptance.

## Preview regression procedure

Read status and difficulties, record the original ID and playback state. Seek/play on one O2Lazer difficulty, then select another difficulty in the same set using the expected current ID. Read status after native audio work settles and compare preview transfer logs. Restore the original difficulty and playback state when finished. Never interpret an unloaded/changed selection as a successful sample continuity test.

Future gameplay/replay instrumentation should use native player boundaries here, without adding test-driver dependencies to the judgement kernel or bypassing the ruleset's editor restrictions.

## Observed control boundaries (2026-10-13)

The listener can respond before MainMenu.LoadComplete captures a navigable game.
Capabilities readiness is therefore not menu readiness; wait for the native menu
before requesting navigation. A successful ruleset acknowledgement also precedes
native filtering. Before controlling a selected chart, verify its source ruleset in
difficulties, actual ID and stable loaded state rather than only the new mode label.

The bridge captures only native SoloSongSelect instances and ordinary navigation
uses the same type boundary. The installed BMS ruleset replaces that screen with
BmsSoloSongSelect. Its global mode switch was accepted, but subsequent controls and
automatic restoration were rejected. Do not include custom song-select screens in
an unattended round trip; obtain manual return to native Solo before continuing.
No BMS-specific type patch or global gameplay/editor bypass was added. This is a
test-instrument limitation, not a claim that BMS gameplay failed.

The R12 run completed O2 preview state/seek/difficulty controls and existing NoMS/MS
replay reads, then used manual BMS return, native shutdown/restart and data/replay
fingerprint checks. The current client remains open for human Player/audio/visual
acceptance. Full results and package identities: [current validation](current-build-validation.md).
