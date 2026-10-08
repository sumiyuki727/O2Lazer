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

Every control command requires `expectedBeatmapId` matching the current selection. `select` also requires `beatmapId`; `seek` requires finite `timeMs` within the loaded track. The PowerShell client exposes these as `-ExpectedBeatmapId`, `-BeatmapId`, and `-TimeMs`. No database writes, imports, editing, mods, gameplay entry or replay control are provided.

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

## Preview regression procedure

Read status and difficulties, record the original ID and playback state. Seek/play on one O2Lazer difficulty, then select another difficulty in the same set using the expected current ID. Read status after native audio work settles and compare preview transfer logs. Restore the original difficulty and playback state when finished. Never interpret an unloaded/changed selection as a successful sample continuity test.

Future gameplay/replay instrumentation should use native player boundaries here, without adding test-driver dependencies to the judgement kernel or bypassing the ruleset's editor restrictions.
