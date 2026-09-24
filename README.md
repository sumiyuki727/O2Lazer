# O2Lazer

An osu!lazer ruleset for playing native O2Jam libraries directly from `.ojn` and `.ojm` files.

[简体中文](./README.zh-CN.md)

See the [current architecture](docs/clean-rewrite-architecture.md), [architecture audit and issue list](docs/architecture-audit.md), and [refactor roadmap (Chinese)](docs/refactor-roadmap.md).

## Design

O2Lazer treats confirmed O2Jam judgement, score, life, combo, Jam, pill, long-note and OJM event
behaviour as gameplay truth while participating in osu!lazer as a native ruleset. When osu! needs a
concept which O2Jam does not define, the ruleset supplies an O2Jam-language result through the
closest mania/lazer contract; non-MS accuracy and letter rank follow this policy. Application
lifecycle behaviour without a consistent O2Jam authority, including pause and resume, follows
lazer. Ruleset-scoped compatibility patches correct host assumptions only where required to expose
O2Jam's identity, while retaining native controls, layouts, animation and interaction conventions.
See the [architecture decision precedence](docs/clean-rewrite-architecture.md#design-intent-and-decision-precedence).

## Features

- Reads classic and newly encrypted OJN files with EX, NX, and HX difficulties.
- Decodes M30, OMC, and OJM keysounds and background music in memory.
- Supports seven-key notes, long notes, BPM changes, keysounds, and BGM events.
- Displays native O2Jam difficulty names and levels. Song select shows o2ma, mania stars, and O2Jam level in that order instead of CS/AR/OD/HP. The mania-stars attribute follows the selected mods. The identifier bar is always full; the level bar fills proportionally up to 150, while higher levels still display their actual value.
- Adds “Level” sort and group options directly below the native difficulty options only while O2Lazer is selected. Sorting orders individual difficulties by O2Jam level. Grouping uses `[N, N+10)` ranges from `Lv.0 - 10` through `Lv.140 - 150`, followed by `Over Lv.150`; each range reuses the colour of its level-divided-by-ten native star group. Native difficulty continues to use mania stars.
- Reuses song select's native difficulty-range control as an integer O2Jam level filter (`Lv.0`–`Lv.100`, then no limit) without Mania Score. Slider nubs show only the number so level 100 fits within the native control. Selecting Mania Score restores the original star filter; both ranges retain their own values when switching.
- Imports embedded OJN cover art as the beatmap background and continues past unreadable charts.
- Keeps score displays separate for EX, NX, and HX difficulties.
- Automatically distinguishes CP949, GBK/CP936 and UTF-8 metadata using field validation and conservative folder hints; the OJN version alone is not an encoding marker.
- Uses O2Jam-style COOL/GOOD/BAD/MISS judgement in chart-position space, including BPM changes within a song, raw score, life, Jam, pills and independently judged LN endpoints.
- Defines non-MS accuracy from O2Jam base judgement values (`COOL=200`, `GOOD=100`, `BAD=4`, `MISS=0`) and maps it to osu!'s common letter-grade boundaries. These compatibility metrics do not replace the raw O2Jam score; Mania Score delegates both values to native mania instead.
- Song-select and local expanded-results star badges use native mania difficulty for the selected or recorded mods. Without Mania Score, the badge displays only `Lv.N`; the badge, neighbouring set markers and results ruleset icon take their colours from `level / 10`, and set difficulties are ordered by level with EX/NX/HX only breaking ties. Mania Score immediately restores native star glyphs, mania-star values, colours and star ordering. Baseline switches read versioned Realm values without chart I/O; only rate or structural mods recalculate. Native StarRating also supplies star searches and global difficulty sorting. New imports store no level-divided-by-ten tags; Refresh beatmaps removes those tags from existing charts.
- Disables the native beatmap editor for O2Lazer to protect imported charts; the skin editor remains available. OJM keysounds are independent of the native beatmap hitsounds switch and global effect volume.
- Keeps o2ma, SR and level visible in song select while omitting all three from the right side of the O2Lazer mod-select footer. Its star badge and BPM display remain available.
- Provides a persistent library path and an optimised incremental update that keeps unchanged charts out of parse/write batches while still handling changed and removed sources.
- Reuses osu!mania's native playfield and stable-skin presentation while keeping O2Jam judgement and scoring state independent.
- Supports clean-format replay recording/playback and O2Jam-specific HUD/playfield skin-editor layers.
- Includes native autoplay and mania-compatible No Fail, Easy, Half Time, Daycore, No Release, Sudden Death, Perfect, Hard Rock, Double Time, Nightcore, Fade In, Hidden, Cover, Flashlight, Accuracy Challenge, Random, Mirror, Mania Score, Classic, Invert, Constant Speed, Wind Up, Wind Down, Muted and Adaptive Speed. Names, English descriptions, settings, icons, ordering, score multipliers and ranking states follow mania. O2-specific adapters preserve exact note/hold objects, chart-position judgement and OJM audio while reusing native mod behaviour. HT/DT preserve BGM and keysound pitch by default; their Adjust Pitch setting affects both. DC/NC apply mania's pitch policy to both audio paths, and NC retains the native beat overlay. Dynamic rate mods also keep visual scrolling and player-triggered keysounds synchronised with their live speed. Constant Speed replaces the former fixed-scroll-speed setting without changing judgement timing. Mania Score converts gameplay to native mania objects and delegates scoring, combo, ranking, health, result statistics and PP to mania. Its integrated OD/HP adjustment defaults to 7; unchanged settings remain performance-eligible, while edits use mania's unranked policy. Easy, Hard Rock and Classic are shown as unavailable until MS is selected; forcing or carrying one into O2Lazer selects MS, disabling MS removes them, and disabling the dependent mod leaves MS selected. Without Mania Score, all selections, including No Mod, are ineligible for PP.

The default key bindings are `S D F Space J K L`.

## Install

The current `master` development build targets osu!lazer **2026.921.0**; the ruleset assembly
version remains **1.0.0** to preserve its identity. This branch is not a new release tag. Build
against matching Game and Mania binaries, close lazer, replace the DLL in its data directory's
`rulesets` folder, and restart. Keep DLL backups outside `rulesets`; do not install two O2Lazer
versions there. Existing import and score associations are intended to be retained, but storage
migration still needs explicit validation. The current replay reader accepts marked schema v5;
pre-rewrite unmarked test replays are intentionally unsupported.

## Importing a library

Keep each `.ojn` beside its corresponding `.ojm`, `.omc`, or `.m30` file. Open **Settings -> O2Jam**
and choose the persistent library path, then use **Update beatmaps**. A batched parallel fingerprint pass
separates unchanged sources before parse/write work, so adding a few charts does not schedule every
existing chart through the importer. Exact-content OJN duplicates at other paths are also recognised
from their stored hashes before star calculation. The same action still updates changed charts,
migrates metadata and removes imports whose source files disappeared.
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

After upgrading an existing library, use **Refresh beatmaps** once to populate both ratings and their version metadata without replacing beatmap IDs or score links. Future refreshes skip unchanged, current entries. Native background reprocessing also computes mania stars; switching MS only reads stored ratings. An unavailable mania rating uses an internal `-1` sentinel; the song-select SR row retains its previous valid value or stays blank until calculation completes.

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
