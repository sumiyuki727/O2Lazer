# Architecture refactor

## Baseline and workspaces

- `master`: active refactor, at `D:/o2lazer` locally and the GitHub default branch.
- `clean-rewrite`: preserved baseline, at `D:/o2lazer-clean-rewrite` locally.
- `pre-architecture-refactor`: baseline tag at `04e56b8`.
- `old`: earlier remote implementation, preserved unchanged.

The worktrees share Git metadata in `D:/o2lazer-clean-rewrite/.git`. Keep that
folder: these are not independent repository copies. The remote baseline/tag
provides a separate backup. Relocation must also update worktree links.

## Agreed architecture

1. **O2Jam core** owns judgement, hold state, score, combo, Jam, pills, life and
   failure rules. It must run unchanged in another game, without lazer types.
   Host time drives execution; the core interprets chart position. Input ordering,
   reset and rewind need explicit contracts. The complete core remains planned.
2. **Bridge** separates independent format decoding from runtime mapping. Formats
   expose chart/sample data. Runtime mapping translates input, time, results and
   state without duplicating gameplay decisions.
3. **lazer host** reuses public interfaces and Mania where behaviour matches:
   input, clocks, replay, audio, storage, object lifecycle, skinning and rendering.
   Drawable callbacks can transport events; hold decisions belong to the core.
4. **Presentation customisation** consumes metadata and gameplay policy for level
   colours, sorting, labels and UI details. It does not decide score or PP eligibility.

Mania Score is a separate gameplay route sharing decoding and suitable host
facilities. It uses native Mania gameplay; the O2Jam core must not know about it.
Its current implementation has not yet been reorganised.

Patches belong to the feature they support. Central installation and compatibility
reporting do not make all patches presentation features. Prefer native behaviour;
use patches only for a demonstrated mismatch or missing extension point, with
necessity and failure impact documented in the owning module.

Extract shared facilities only when another ruleset, such as BMS, can use them
unchanged. Portability of O2Jam rules to another game is a separate requirement.
Storage/synchronisation is a parallel subsystem consuming completed results, not a
live judgement dependency. Prefer native persistence for native data. Whether
local/server archives are independent or synchronised remains undecided.

## Implemented first slice: independent formats

```text
OJN bytes/stream --> OjnReader --> OjnDocument / OjnChart
OJM/OMC/M30 -----> OjmReader --> OjmArchive / OjmArchiveIndex
                                  |
              existing host bridge consumes decoded values
```

`O2Jam.Formats` references only .NET and the code-pages package: no ruleset,
gameplay-core, Game, Mania, framework, Realm or Harmony references. Existing
namespaces are retained to limit consumer churn; their spelling does not add
an assembly dependency on osu.

### Data contract

- Metadata includes title/artist/arranger, sample filename, levels/durations and
  optional images. Decoding does not resolve directories or write a database.
- Charts expose OJN difficulty, level, BPM events, normalised measure positions,
  measure fractions and note/sample events. Positions are not milliseconds.
- Existing fraction normalisation, hold pairing, orphan handling, volume/pan and
  zero-based sample IDs are retained. Tail sample references stay in decoded data
  even when the gameplay bridge intentionally does not play them.
- `OjnDifficulty` and `OjnBpmEvent` are format values. `OjnGameplayMapping` maps
  them explicitly to the existing gameplay types at the host boundary.
- OJM archives expose sample IDs, encoded audio bytes and metadata, not channels.
  `ReadLazy` reads payloads from a file on demand; callers must keep that file
  available and unchanged until loading finishes.
- Full OJN reads return all three chart slots. Selected reads return one and omit
  image materialisation. Callers own input streams.

This is decoded format data, not the final gameplay input model. It still carries
OJN channels and source concepts; these are not proposed as a generic BMS model.
Existing lists/byte arrays are not deeply immutable. Consumers must not mutate
shared cached decode results.

### Remaining work

The existing host still owns `OjnBeatmapFactory`, directory encoding fallback,
document/archive caches, import, timing maps, audio scheduling and gameplay.
These are not claimed as completed architecture layers. Next, specify the
format-to-core chart translation and complete hold-state ownership before
changing gameplay. Do not change level presentation or Mania Score policy as
part of decoding extraction.

### Build and deployment

Another application can reference the standalone format project directly. The
lazer project references it and embeds it using ILRepack, preserving single-DLL
installation. Format types retain public names. Host integration tests compile
against the embedded identities, excluding the transitive standalone assembly
to avoid duplicates; independent tests reference only the standalone library.
Both use one shared synthetic OJN fixture.

```powershell
dotnet test O2Jam.Formats.Tests/O2Jam.Formats.Tests.csproj -c Release `
  --filter 'FullyQualifiedName~.Normal.'
```

No osu installation or private chart library is needed for that command. The
routine verification script runs both test projects; host tests still require
matching lazer binaries as described in [development.md](development.md).
Always use test filters; do not run benchmarks without permission.
