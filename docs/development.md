# Building and verifying O2Lazer

## Requirements

- .NET 10 SDK (C# 14); `global.json` accepts later stable .NET 10 feature bands.
- .NET 8 runtime for the test host. The ruleset itself targets `net8.0` and runs inside lazer.
- Matching **osu!lazer 2026.804.2** binaries, including `osu.Game.dll` and
  `osu.Game.Rulesets.Mania.dll`. Windows is the currently validated platform.
- NuGet access for the framework, Realm, Harmony, test runner and build dependencies.

An SDK alone does not supply osu!'s assemblies. Use an existing matching lazer installation or
build osu! separately under its own instructions. This repository does not build, modify or bundle
the sibling osu!, osu-framework or community-ruleset checkouts. Do not mix Game and Mania binaries
from different lazer releases.

## Build from a clone

From the repository root, in PowerShell:

```powershell
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
dotnet build osu.Game.Rulesets.O2Lazer.slnx -c Release "-p:OsuBinaryDirectory=$lazerBinaries"
```

Use the actual binary directory if your installation is elsewhere. The output to install is:

```text
osu.Game.Rulesets.O2Lazer/bin/Release/net8.0/osu.Game.Rulesets.O2Lazer.dll
```

Only install that DLL into the lazer **data directory's** `rulesets` folder after closing the game.
Harmony is internalised into it. Do not copy Game, Mania, framework, Realm, test binaries or a
standalone `0Harmony.dll` into `rulesets`. Back up an old ruleset DLL outside that folder, not under
a second filename inside it. No new release version or tag is implied by a source-branch build.

The clean rewrite starts at version `1.0.0`, independent of the host's `2026.804.2` compatibility
version. The project `Version` is the single release-version source; the SDK derives assembly/file
version `1.0.0.0` and the informational version (which may include a Git commit suffix).
The ruleset assembly name, entry type and short name remain unchanged. Native `RealmRulesetStore`
updates the existing short-name record when the assembly version changes; do not install both
the old and new DLL at once. Historical tags are maintained separately and are not rewritten by builds.

For separately located binaries, these properties remain supported:

```powershell
dotnet build osu.Game.Rulesets.O2Lazer.slnx -c Release `
  "-p:OsuGameProjectPath=$lazerBinaries/osu.Game.dll" `
  "-p:OsuManiaAssemblyPath=$lazerBinaries/osu.Game.Rulesets.Mania.dll"
```

Despite its historical name, `OsuGameProjectPath` now accepts only a DLL. Missing configuration
fails with a diagnostic instead of searching hidden `.artifacts` directories or building a sibling
project. `UseLocalOsu=false` retains the NuGet reference route for environments where the exact
`ppy.osu.Game` and `ppy.osu.Game.Rulesets.Mania` versions are available; do not substitute a different
release simply to make restore succeed.

## Routine verification

```powershell
./scripts/verify.ps1 -OsuBinaryDirectory $lazerBinaries
```

The script always supplies a test filter, builds in Release with audio tracing disabled, and
excludes `LocalDiagnostics` and `Isolated`. It does not need a real O2Jam library, user database,
skin or replay. Most format tests build synthetic binary fixtures in memory or temporary storage.
The headless visual tests verify drawable state, clipping, pooling and input, not GPU screenshots.
Reports are written under ignored `.artifacts/test-results`.

For a narrower check:

```powershell
dotnet test osu.Game.Rulesets.O2Lazer.Tests -c Release `
  "-p:OsuBinaryDirectory=$lazerBinaries" `
  --filter 'FullyQualifiedName~O2JamHoldNoteTest|FullyQualifiedName~TailResolutionUsesResolvedAccuracyForClipping|FullyQualifiedName~RejectedHeadDoesNotClip'
```

Run the forced-GC setting-subscription check in its own process, away from Realm tests:

```powershell
dotnet test osu.Game.Rulesets.O2Lazer.Tests -c Release --no-build `
  "-p:OsuBinaryDirectory=$lazerBinaries" `
  --filter 'FullyQualifiedName~O2JamRuntimeOptionsTest.RuntimeProjectionSurvivesGarbageCollection'
```

The native ruleset-store version-migration check also runs in its own process. Mixing its full
store bootstrap with later Realm lifetime tests has triggered a native Realm transaction assertion
in the shared test host; a separate invocation preserves this coverage without destabilising the
routine checks. It uses a temporary database, never the player's library.

```powershell
dotnet test osu.Game.Rulesets.O2Lazer.Tests -c Release --no-build `
  "-p:OsuBinaryDirectory=$lazerBinaries" `
  --filter 'FullyQualifiedName~NativeRulesetStorePreservesAssociationsAfterVersionRestart'
```

Never run benchmarks or full-corpus decodes as a routine verification step. Those expensive
operations require an explicit decision and an exact method filter; `[Explicit]` is an additional
safeguard, not a reason to use an unfiltered test command.

## Optional local diagnostics

Private fixtures are deliberately not committed. Diagnostic tests are marked `LocalDiagnostics`
and `[Explicit]`; select an exact method only after supplying its inputs.

| Environment variable | Purpose |
|---|---|
| `O2JAM_CORPUS_PATH` | External O2Jam library root for corpus and known-chart checks. |
| `O2JAM_REPLAY_DIAGNOSTIC_PATH` | Existing clean-schema v5 replay file. |
| `O2JAM_DIAGNOSTIC_REALM` | Existing `client.realm`, opened read-only by diagnostics. |
| `O2JAM_DIAGNOSTIC_SKIN` | Skin GUID from that database; assets are read from its file store. |
| `O2JAM_BMS_RULESET_PATH` | Separately built BmsRuleset DLL for coexistence checks. |
| `O2JAM_ENCODING_AUDIT_PATH` | Optional output path for a header-encoding audit. |

`O2JamReplayPlayfieldProbeTest.InspectActualReplay` is a specific regression fixture, not a generic
replay player: it expects `ESong/o2ma387.ojn` (SAY THAT YOU, HX Lv.59) and the reported replay with
the final seven-tail group. It compares saved statistics, final Stage judgement, pill-rescued COOL
clipping, neighbouring MISS retention, and object recycling, with both visual and frame-accuracy
settings. It needs the first four environment variables above. Other users without those fixtures
should run the synthetic clipping tests instead; do not upload copyrighted charts or private data.

Runtime audio tracing has a separate opt-in build flag. See
[audio-sync-diagnostics.md](audio-sync-diagnostics.md). It remains disabled in normal builds.

## Dependency audit note

NuGet reports [GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x)
for AutoMapper 13.0.1, concerning unbounded recursive mapping. The test project references that
version to match the target lazer binary's Realm helpers; it is not bundled into the ruleset DLL.
The referenced osu! source also pins 13.0.1 and configures depth limits/ignored back-references
for its beatmap mappings. O2Lazer does not define additional AutoMapper mappings. This is a scoped
dependency observation, not a blanket security guarantee: retain the audit warning and reassess
when the host version changes rather than silently substituting a binary-incompatible major version.

## Source and publication boundaries

- `Core/`: framework-independent O2Jam timing and gameplay state.
- `Clean/`: the active format, import, audio, scoring, replay and presentation adapters.
- `Normal/Core/` and `Normal/Clean/`: active tests; historical names are retained to avoid needless moves.
- `.artifacts/`, `.dotnet/`, `bin/`, `obj/`, `export/`: local-only inputs and generated outputs.
- Previous BMS-derived sources remain in Git history; they are not compiled by this tree.

Before pushing, check the staged diff for credentials, local data, generated binaries and unrelated
assets. Keep English and Chinese resources aligned. Preserve the persisted ruleset identity and
the latest [LN visual policy](o2jam-behaviour-spec.md#long-note-presentation). Do not broaden a
compatibility patch's scope or change gameplay rules as part of formatting and documentation work.
