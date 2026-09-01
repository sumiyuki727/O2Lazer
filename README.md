# O2Lazer

An osu!lazer ruleset for playing native O2Jam libraries directly from `.ojn` and `.ojm` files.

[简体中文](./README.zh-CN.md)

## Features

- Reads classic and newly encrypted OJN files with EX, NX, and HX difficulties.
- Decodes M30, OMC, and OJM keysounds and background music in memory.
- Supports seven-key notes, long notes, BPM changes, keysounds, and BGM events.
- Displays native O2Jam difficulty names and levels. Song select shows o2ma, mania stars, and O2Jam level in that order instead of CS/AR/OD/HP. The mania stars attribute always shows stored mania difficulty, regardless of MS. The identifier bar is always full; the level bar fills proportionally up to 150, while higher levels still display their actual value.
- Adds “Level” sort and group options directly below the native difficulty options only while O2Lazer is selected. Sorting orders individual difficulties by O2Jam level. Grouping uses `[N, N+10)` ranges from `Lv.0 - 10` through `Lv.140 - 150`, followed by `Over Lv.150`; each range reuses the colour of its level-divided-by-ten native star group. Native difficulty continues to use mania stars.
- Imports embedded OJN cover art as the beatmap background and continues past unreadable charts.
- Keeps score displays separate for EX, NX, and HX difficulties.
- Automatically distinguishes CP949, GBK/CP936 and UTF-8 metadata using field validation and conservative folder hints; the OJN version alone is not an encoding marker.
- Uses O2Jam-style COOL/GOOD/BAD/MISS judgement in chart-position space, including BPM changes within a song, raw score, life, Jam, pills and independently judged LN endpoints.
- Song-select and results star badges display OJN level / 10 without MS and stored mania stars with MS; results use the score's recorded mods. Native `StarRating` stores mania difficulty for searching and sorting; O2Jam stars are stored independently. MS remains a hidden placeholder until mania scoring is implemented.
- Disables the native beatmap editor for O2Lazer to protect imported charts; the skin editor remains available. OJM keysounds are independent of the native beatmap hitsounds switch and global effect volume.
- Provides a persistent library path with incremental refresh and removal of imported O2Jam beatmaps.
- Reuses osu!mania's native playfield and stable-skin presentation while keeping O2Jam judgement and scoring state independent.
- Supports clean-format replay recording/playback and O2Jam-specific HUD/playfield skin-editor layers.
- Includes native autoplay and mania-compatible No Fail, Half Time, Daycore, No Release, Sudden Death, Perfect, Double Time, Nightcore, Fade In, Hidden, Cover, Flashlight, Accuracy Challenge, Random, Mirror, Invert, Constant Speed, Wind Up, Wind Down, Muted and Adaptive Speed. Names, English descriptions, settings, icons, ordering, score multipliers and ranking states follow mania. O2-specific adapters preserve exact note/hold objects, chart-position judgement and OJM audio while reusing native mod behaviour. HT/DT preserve BGM and keysound pitch by default; their Adjust Pitch setting affects both. DC/NC apply mania's pitch policy to both audio paths, and NC retains the native beat overlay. Dynamic rate mods also keep visual scrolling and player-triggered keysounds synchronised with their live speed. Constant Speed replaces the former fixed-scroll-speed setting without changing judgement timing. Without Mania Score, all selections (including No Mod) display as ineligible for PP. With Mania Score, the display follows mania's native mod eligibility. The Mania Score selection UI is currently hidden. Actual mania scoring/PP calculation remains future work.

The default key bindings are `S D F Space J K L`.

## Install

The current clean-rewrite prerelease tag is **1.0.0-test**; its persisted assembly version remains
**1.0.0** for ruleset identity compatibility. It targets osu!lazer **2026.804.2**. Build or obtain a compatible
`osu.Game.Rulesets.O2Lazer.dll`, close lazer, replace the DLL in its data directory's `rulesets`
folder, and restart. Keep DLL backups outside `rulesets`; do not install two O2Lazer versions there.
The persisted ruleset identity is unchanged, so existing imports and score associations are retained.
Pre-rewrite replays are not supported; existing score records are not deleted.

## Importing a library

Keep each `.ojn` beside its corresponding `.ojm`, `.omc`, or `.m30` file. Open **Settings -> O2Jam**,
choose the persistent library path, then use **Update beatmaps**. Updates import new/changed charts
and remove imports whose source files disappeared; unchanged charts still count toward progress.
**Clear beatmap imports** removes the in-game imports, not the source files. Keep the original
library available because audio archives remain externally referenced.

Folder-based collections are optional and off by default. When enabled they synchronise with library
updates; disabling the option removes the collections managed by this feature, not unrelated collections.
Song-select preview always mixes BGM and playable keysounds. Compatible difficulties share playback;
difficulties with different background arrangements start their own preview. LN tails are silent.

## Gameplay and skin options

Scroll speed uses mania's visual scale and also shows an O2Jam-equivalent multiplier. Constant Speed
keeps the visual time range fixed through BPM changes without changing judgement; the former settings
toggle has been removed, so only the selected mod enables this behaviour. The O2Jam LN visual option is off by
default. When enabled, released LNs remain in their original colour: final COOL/GOOD (including
pill-rescued COOL) continue clipping; BAD/MISS stop clipping and let the remainder scroll past the
line. This does not delay scoring or keep the hold light active. A separate Percy-body fix extends
overlong legacy hold textures and follows their animation frames.

The gameplay model is based on reference implementations and player checks, not a claim of complete
original-client equivalence. See the [behaviour specification](docs/o2jam-behaviour-spec.md) for
evidence and limitations. Dedicated Jam/pill HUD widgets and further preview-performance work remain.

## Searching beatmaps

Combine these filters in the O2Lazer song-select search box:

- `ln>50`: LN percentage strictly above 50%; `ln>=50` also includes exactly 50%.
- `stars>5`: native osu!mania stars, regardless of MS. For example, `stars>=3 stars<5 lv>=50` combines mania difficulty and O2Jam level.
- `note>50`: tap-note percentage above 50%. Percentages use each difficulty's object counts: LN count / (tap count + LN count). Each hold counts once, regardless of duration or its two judgements.
- Percentages support `=`, `!=`, `<`, `<=`, `>`, `>=`, decimals, and an optional `%`, for example `ln>=25 ln<75`.
- `level>=50` or `lv>=50`: filters by the native O2Jam level. Both keywords are case-insensitive and support osu!'s comparison operators (`=`, `!=`, `<`, `<=`, `>`, `>=`, including their `:` variants). Conditions can be combined, such as `LEVEL>=50 lv<100`; searches are not capped at level 150.
- `o2ma100`: matches only the complete `o2ma100` identifier tag, not `o2ma1000` or `o2ma1001`. Matching is case-insensitive.
- A bare number such as `100` can still match ordinary titles, creators, difficulty names, and other metadata, but not O2Jam identifiers, identifier-based filenames, or internal import tags.

For example, `o2ma100 ln>50` selects that song's difficulties with more than 50% LNs. Search uses existing imported metadata without reimporting or decoding charts.

After upgrading an existing library, use **Refresh beatmaps** once to populate both ratings and their version metadata without replacing beatmap IDs or score links. Future refreshes skip unchanged, current entries. Native background reprocessing also computes mania stars; switching MS only reads stored ratings. An unavailable mania rating is marked as uncalculated (`-1`) until processing completes.

## Build

Use a **.NET 10 SDK** to compile C# 14, with a .NET 8 runtime for tests. Reference matching existing
lazer binaries; the build does not modify sibling source checkouts.

```powershell
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
dotnet build osu.Game.Rulesets.O2Lazer.slnx -c Release "-p:OsuBinaryDirectory=$lazerBinaries"
./scripts/verify.ps1 -OsuBinaryDirectory $lazerBinaries
```

See [development and testing](docs/development.md) for alternate binary paths, filtered tests and
optional local diagnostics. Architecture is documented in [clean-rewrite-architecture.md](docs/clean-rewrite-architecture.md).

## Credits and license

The current ruleset is a clean implementation and does not compile or include the archived BMS-derived implementation. The pre-rewrite project remains available separately for behavioural reference. O2Jam format work references the MIT-licensed [O2MusicBox](https://github.com/SirusDoma/O2MusicBox), [CXO2](https://github.com/SirusDoma/CXO2), and public Open2Jam format documentation. The project is licensed under AGPL-3.0; see [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).
