# O2Lazer clean rewrite architecture

## Compatibility contract

The rewrite deliberately keeps the following externally persisted identity:

- assembly name: `osu.Game.Rulesets.O2Lazer`
- ruleset type: `osu.Game.Rulesets.O2Lazer.O2LazerRuleset`
- ruleset short name: `o2lazer`
- seven-key variant: `207`
- input action values: `Key1` through `Key7`, with their existing integer values

osu!lazer stores `RulesetInfo` by short name and also uses the short name for settings,
key bindings, beatmaps, scores and skin layouts. `RealmRulesetStore` updates the stored
instantiation information when an installed ruleset with the same short name moves to a
different assembly-qualified type. Keeping both the short name and entry type stable avoids
requiring a user-visible ruleset migration.

The clean root ruleset derives directly from `Ruleset`, is not an `ILegacyRuleset`, and keeps
`OnlineID = -1`. Runtime input uses native `ManiaAction` so the mania playfield remains the visual
and input protocol. Pre-rewrite replay compatibility has been removed; this does not change the
integer key bindings, ruleset identity, existing beatmap library or stored score records.

## Active modules and dependency boundaries

`Core` and `Clean` are the only compiled source roots. `Clean` is the historical directory name
for the active rewrite, not a second implementation. The old source tree and generator have been
removed from the solution; they remain available in Git history.

| Directory | Responsibility and boundary |
|---|---|
| `Core/` | Timing, judgement, difficulty scale, identity and gameplay state; no osu!, graphics, audio, database or filesystem dependency. |
| `Clean/Formats/` | OJN/OJM parsing and metadata encoding. `OjnBeatmapFactory` is the explicit bridge into mania-compatible hit objects; readers do not write Realm or create drawables. |
| `Clean/Import/` | Library scanning/planning, Realm writes and managed collections. Replaceable infrastructure, not a dependency of the judgement engine. |
| `Clean/Beatmaps/` | External-source descriptors, working beatmaps/caches and conversion boundaries. |
| `Clean/Audio/` | Archive stores, shared preloading, preview/gameplay coordination and native track/sample ownership. |
| `Clean/Objects/`, `Clean/Scoring/` | Adapt core rules into lazer hit objects, results, score and health processors. |
| `Clean/UI/`, `Clean/Skinning/` | Native mania presentation, clipping, HUD/editor integration and narrowly scoped compatibility patches. |
| `Clean/Replays/`, `Clean/Mods/` | v5 replay persistence/input, autoplay and native mania mod adapters; transformations stay outside judgement and drawables. |
| `Clean/Configuration/`, `Clean/SongSelect/`, `Clean/Localisation/` | Settings, metadata/search adapters and bilingual UI resources. |

The core owns gameplay truth. Import, audio and presentation may consume it, but the core never
calls into those adapters. Cross-ruleset operations are guarded by the O2Lazer identity or processor
type; unsupported conversions to or from O2Lazer are rejected without changing other rulesets.

## Native mania presentation

The first implementation uses osu!mania types as a presentation protocol rather than copying
their source:

- the playable beatmap derives from `ManiaBeatmap`;
- tap and hold domain objects are represented by subclasses of mania `Note` and `HoldNote`;
- drawable types derive from the corresponding mania drawables and replace judgement/input
  handling while retaining the visual hierarchy expected by mania skins;
- `CreateSkinTransformer()` delegates to the native `ManiaRuleset` using a seven-key presentation
  beatmap.

This is necessary because the native default, Argon and legacy hold-body pieces cast their
drawable dependency to `DrawableHoldNote`/head/tail types. A completely unrelated drawable can
not consume the native mania skin system safely.

The O2Jam long-note state remains authoritative. Native mania nested results are projections used
to drive skin visuals, including the dropped/missed hold appearance; they are not the source of
O2Jam score or replay truth.

The rewrite does not carry forward the old bundled mania textures, fonts or copied skin tree.
Native mania skin lookup is used directly. Two narrow legacy-hold adapters are retained: one
controls mania's built-in dark-grey broken-hold tint for the O2Jam miss appearance, and one tiles
only the otherwise blank remainder of a non-stretch body longer than mania's native span. The
native body still owns its initial texture span, animation and lighting, so neither requirement
needs a fork of the complete mania skin implementation.

The optional O2Jam LN visual keeps the parent and tail alive after resolution. Final COOL/GOOD
(including pill-rescued COOL) continue clipping. BAD/MISS freeze the remaining length and scroll
past the line; rejected heads never begin clipping. Neither retention nor clipping changes input,
judgement, score or sample scheduling. See the [behaviour policy](o2jam-behaviour-spec.md#long-note-presentation).

## Skin editor targets

Gameplay uses osu!lazer's two native ruleset-specific skin-editor targets. Their persisted keys are
the stable ruleset short name `o2lazer`, so they appear as the O2Jam-only HUD and O2Jam-only
Playfield layers and remain isolated from mania and BMS layouts. `HUDOverlay` creates both
`SkinnableContainer` targets independently of the active skin, so an audio-only beatmap skin or a
skin without mania defaults cannot mask the selected visual skin or disable either editor layer.

Ruleset-specific editor components live under `Clean/Skinning/Components` and are discovered from
the O2Lazer assembly by osu!lazer. `O2JamComboCounter` is the first such component and delegates its
rendering and increment animation to mania's native legacy counter. New Jam, pill or O2Jam-only HUD
drawables should be added to this directory as independent serialisable components rather than
added to the playfield or score processor.

The exact pre-rewrite type names `UI.HudComponents.O2LazerComboCounter` and
`UI.HudComponents.O2LazerJudgementDisplay` remain as non-editable layout adapters. The former uses
the native mania counter and the latter is intentionally empty because current mania presentation
already renders judgements in the stage. Keeping the types prevents an old skin-layout JSON from
failing before osu!lazer can open and resave it.

## Replay contract

The native replay pipeline follows osu!mania's separation of responsibilities:

- `Replays/O2JamReplayFrame.cs` stores the complete `ManiaAction` key state;
- `Replays/O2JamReplayRecorder.cs` records key press, key release and important judgement frames;
- `Replays/O2JamFramedReplayInputHandler.cs` restores the frame state into the mania input stack.
- `Replays/O2JamAutoGenerator.cs` supplies the exposed autoplay mod and the native editor/skin-editor contract.

Only clean replay schema v5 is supported. Pre-rewrite frames, interim mania-frame conversion,
stable bitmask conversion, branch-decision payloads and filename-based beatmap guessing have been
removed. The archive reader validates the schema version and frame data before creating a replay.
Current recordings include an `o2lazer` ruleset marker; already-recorded v5 files without it remain
supported. Imports require embedded chart hashes and a matching O2Jam beatmap.

The import patch does not claim foreign JSON/gzip envelopes, which BMSRuleset also uses. Unsupported
old files fail the native import header check without creating a score. For stored O2Jam scores,
unsupported data returns a null replay so osu! stops before opening the replay player. Existing
score records and attached replay files are never deleted by this check.

## Scroll contract

The presentation layer uses mania's visual time-range model:

```text
time range = 11485 / scroll speed
```

The O2Jam speed setting is mapped onto that model. Constant Speed (`CS`) selects the native
constant scrolling algorithm instead of sequential scrolling through BPM changes. The former
fixed-scroll-speed setting and its binding are removed; old stored values are ignored, so only
the selected mod enables constant scrolling. Judgement remains in chart-position space.

## Persisted difficulty and display scales

osu!'s `BeatmapInfo.StarRating` stores one base star rating per difficulty. Its modded
`BeatmapDifficultyCache` is in memory, and a set's maximum star difficulty is derived rather
than a second stored rating. O2Lazer uses these storage boundaries as follows:

| Data | Storage / role |
| --- | --- |
| Native mania stars | `BeatmapInfo.StarRating`; native `stars` search, difficulty sorting and grouping always use this value. |
| O2Jam stars | Per-difficulty `o2lazer-o2jam-stars:1:<level / 10>` metadata tag, with invariant round-trip formatting. |
| Mania cache version | `o2lazer-mania-version:1:<native algorithm version>` metadata tag; the first version covers our projection. |
| Main star badge | O2Jam stars without MS; native mania stars with MS. No database mutation or strain calculation on a display-mode change. |
| Mania stars attribute | Always reads stored native mania stars, independently of MS; positioned between o2ma and O2Jam level. |

`O2JamImportPlanner` uses the existing `OjnBeatmapFactory` to resolve seven-column note and hold
times, then `O2JamManiaStarRating` projects only those objects into a plain native `ManiaBeatmap`.
The native playable-beatmap/defaults pipeline and `ManiaDifficultyCalculator` calculate the
baseline rating without O2Jam judgement, automatic audio events, OJM access, Realm or UI state.
`O2JamLibraryWriter` alone persists both values. Refresh checks every difficulty's cache version
and updates old entries in place, retaining beatmap IDs and score associations. It publishes
detached updated snapshots after committing; the settings composition layer invalidates native
working-beatmap and difficulty caches through their public APIs.

`O2JamDifficultyCalculator` always returns mania stars, including without MS, so native background
reprocessing cannot overwrite the database with the display scale. Its version combines the
native algorithm version and projection version, causing old level-based native ratings to be
reprocessed. Valid stored ratings take a metadata-only path; missing, invalid or outdated values
can be calculated from the source without relying on the import settings UI. O2Jam combo remains
based on note/hold endpoints, not mania's duration-based legacy combo.

`O2JamStarRatingDisplayPatch` replaces one difficulty lookup only inside osu!'s display-bindable
update path. It leaves native scheduling, mod tracking, cancellation and invalidation intact,
and forwards other rulesets to the original lookup. Direct difficulty calculations and all native
search/sort code remain untouched. A second, validated adapter replaces only the star-display
constructor in the native results panel. It applies `O2JamDisplayedDifficulty` using the score's
recorded mods, including scores absent from the local library. Neither adapter changes stored
ratings or PP calculation. The public song-select attribute provider reads stored mania stars
directly. Old libraries should
run **Refresh beatmaps** once to populate the independent
O2Jam stars and version markers; missing mania stars use the native uncalculated sentinel `-1`.
Legacy level fallback is allowed only before native StarRating changes meaning.

The current cache represents baseline speed. Future rate or chart-transform mods that change
mania difficulty need their own calculation/cache policy. MS's mania scoring is still unimplemented
and the mod remains hidden; star display switching does not enable it or change gameplay judgement.

## Mod extension points

Autoplay is exposed for the native playback/editor contract. The available gameplay mods retain
mania's names, icons, categories, settings and intrinsic
`Ranked` properties. Descriptions use matching English text through `O2LazerStrings`; all supported
resources deliberately keep these descriptions in English. Autoplay already inherits native English text.
Difficulty Reduction contains No Fail, HT/DC and No Release. Difficulty Increase groups Sudden Death
with Perfect, DT with NC, and Fade In with Hidden and Cover before Flashlight and Accuracy Challenge.
Conversion lists Random, Mirror, Invert and Constant Speed in mania's relative order, followed by the
hidden Mania Score placeholder. Fun groups Wind Up with Wind Down before Muted and Adaptive Speed.

Sudden Death and Perfect attach native fail conditions to `O2JamHealthProcessor`, including on EX
where ordinary life depletion does not fail. SD reacts to MISS; PF requires COOL in O2Jam's result
set. PF reads the final result after pill conversion and judges LN heads and releases independently;
ignored hold-body results do not fail. Native restart settings and incompatibility with No Fail are
retained, and replay mod JSON stores these settings without a schema change.
PF's "Require perfect hits" control is only visible with Mania Score selected, since ordinary
O2Jam scoring has no separate PERFECT tier. Its custom native settings checkbox reads the
containing mod overlay's selection; the inherited bindable and replay setting remain intact.

Constant Speed inherits mania's presentation and intrinsic unranked status, with its native 0.9
score multiplier registered for the local mod type. It reimplements only
the drawable-mod interface: mania casts to `DrawableManiaRuleset`, while O2Lazer uses the common
`DrawableScrollingRuleset<ManiaHitObject>` base to select the same `ConstantScrollAlgorithm`.
No chart, keysound or judgement timing is rewritten.

No Release cannot use mania's replacement `NoReleaseHoldNote` and drawable pool because those
would erase O2Jam endpoint metadata and require `DrawableManiaRuleset`. The local adapter marks
the converted O2Jam hold and tail instead. The O2Jam tail drawable then resolves COOL when it
reaches the judgement point while still held; early releases remain on the ordinary O2Jam path.
Its native 0.9 multiplier is registered for the exact local type.

Fade In, Hidden and Cover reuse mania's native cover creation, coverage bindables, settings and
dynamic Hidden update interfaces. A shared adapter performs the same remove-wrap-add operation
against the common `ManiaPlayfield`, avoiding mania's concrete drawable-ruleset cast. Flashlight
and Accuracy Challenge already target generic playfield and score-processor contracts and need
only localised wrappers.

Invert reimplements the post-conversion transform because the native implementation creates plain
mania `HoldNote` objects. The local transform follows the same per-column locations and duration
formula while creating `O2JamHoldNote` heads and silent tails with chart positions from the immutable
timing map. Source playable-beatmap objects are fresh copies, and automatic audio plus measure data
remain outside the transform.

Wind Up, Wind Down and Adaptive Speed inherit the native live-rate implementations. The gameplay
clock receives their native track adjustments, while O2Jam visual compensation and endpoint
keysounds bind directly to the same `SpeedChange`. Their Adjust Pitch bindable selects Frequency
or Tempo for both BGM and player-triggered OJM sounds. Muted inherits native combo-driven volume,
metronome, hitsound and score-processor behaviour; the working beatmap track and drawable audio
containers remain the native application boundaries. Exact-type score multiplier entries retain
mania's 0.5 value for WU, WD and AS.

Rate mods use the native `IApplicableToTrack` path for preview and gameplay BGM. Preview background
layers and automatic keysounds already bind to `O2JamPreviewTrack`, including recreated layers after
a seek. Player-triggered tap and LN endpoint sounds bind to `O2JamHitSoundRateAdjustments`, cached only
inside their `O2JamDrawableRuleset`. The adapter mirrors native Frequency/Tempo policy and live settings
without applying rate mods to the entire drawable audio tree, so Nightcore percussion and unrelated
sounds stay on their native path. Visual time range binds directly to `SpeedChange`; this avoids applying
the native single-track audio helper to a second target while preserving mania's scroll compensation.

`O2JamBeatmapConverter` creates fresh tap/hold objects and sample lists for each playable beatmap.
The native converter alone reuses objects of its target type, so an in-place column mod would
otherwise mutate `WorkingBeatmap`'s cached source. Native Mirror flips the seven columns; native
Random applies a single seeded column permutation to the whole chart. Hold endpoints follow their
parent column. Timing, OJM sample identity/pan, automatic audio and silent tails are preserved.
Native mod JSON already carries Random's seed through the v5 replay archive; no replay schema
change or separate random algorithm is needed.

No Fail uses the native failure override. The scoring adapter separately passes a framework-free
`continueAfterLifeDepletion` policy to `O2JamGameplayState`, allowing score, life, Jam and pills to
continue at zero life for EX/NX/HX. Without No Fail, the original depletion policy is unchanged.
`O2JamScoreMultiplierCalculator` registers the local No Fail type at mania's 0.5 multiplier because
native multiplier lookup uses exact types. The core retains raw score, while native score
processing supplies multiplied totals and `TotalScoreWithoutMods` for persistence.

Mania Score (`MS`) is a hidden placeholder registered after Constant Speed in Conversion. The native category gives
it purple styling, and `Ranked = true` allows its selection to pass the PP eligibility policy without implementing
performance calculation. It has no applicable-mod interfaces and changes neither judgement nor
score. `HasImplementation = false` uses native filtering to hide the selection entry and prevents
the unfinished placeholder from being selected for gameplay; its type remains available for stored scores.
Its PNG is registered as a namespaced glyph through `FontStore` when the ruleset icon loads, allowing
native mod switches and badges to display it without a Harmony patch or a copied UI component.

`O2JamPerformanceEligibility` owns selection-level PP eligibility: MS must be present and every
selected mod must retain native `Ranked = true`. Without MS, even an empty No Mod selection is
ineligible. With MS, the current selection is eligible only when every other mod's native ranking
state is eligible. At default settings this includes No Fail, HT/DC, Mirror, Sudden Death/Perfect,
DT/NC, Fade In/Hidden/Cover, Flashlight, Accuracy Challenge and Muted. No Release, Random, Invert,
Constant Speed, Wind Up/Down, Adaptive Speed and Autoplay make it ineligible.
This policy never changes individual mod properties or depends on a global MS toggle, so stored
scores are evaluated using their own mods independently of the current selection.

Native ranking displays aggregate `Mod.Ranked` and consider an empty collection eligible, with no
ruleset hook for a combination policy. `O2JamPerformanceEligibilityPatch` therefore adapts only
O2Lazer's score PP displays via Harmony postfixes. The mod-selection footer substitutes the
eligibility result before the native `Ranked` bindable is written, preventing a spurious
unranked-to-ranked-to-unranked transition and its repeated flash. While O2Lazer remains unranked,
a scoped guard also suppresses the whole-panel flash from multiplier changes; the multiplier
counter keeps its native rolling, movement and colour animations. Real eligibility transitions
and all other rulesets retain their flashes. A finalizer always releases this guard, including
on exceptions. A postfix handles the no-beatmap case only.

The results PP statistic also initialises native dimming and tooltip styling for ineligible
O2Lazer scores without a stored PP value. osu! otherwise skips this styling when no performance
calculator is available. The adapter uses the control's existing default zero for presentation
only; `ScoreInfo.PP` stays null, stored values remain unchanged, and later calculations retain
their normal update path. Eligible MS selections and other rulesets keep native behavior.

The song-select Mods
button always runs its native `updateDisplay`. A validated transpiler adapts its eligibility
predicate and guards seven badge transform calls and three button-width calls. The guards pass
through unchanged except when entering or leaving O2Lazer No Mod. The native mod bar, overflow
count, multiplier, colours, localisation and score-multiplier context remain untouched. No synthetic
No Mod entry is injected into selections, scores or replays. Private API targets and exact call
signatures/counts are checked at installation; an incompatible host rolls back this adapter.

`O2JamNoModBadgeAnimation` tracks four destinations. The three native destinations are unranked
upper right (`X=0, Y=-5, Alpha=1`), eligible mods hidden beneath the mod bar
(`X=-badge.DrawWidth, Y=-5, Alpha=0`), and native No Mod (`Y=20, Alpha=0`, retaining the previous
native horizontal target). Only O2Lazer No Mod adds upper left. The native `Margin.Left=121`
remains unchanged; `X=-121` gives the same visible left position as the old zero-margin layout.
Lower left (`X=-121, Y=20, Alpha=0`) is a waypoint, not a separate selection state.
All custom movements use the native 240 ms `OutQuint` timing:

| Route | Animation |
|---|---|
| LeftUpper ↔ native upper right | Horizontal slide, fully visible; button width changes concurrently in both directions. |
| LeftUpper → native No Mod | Fade down to LeftLower, then relocate horizontally while hidden. |
| Native No Mod → LeftUpper | Relocate horizontally to LeftLower while hidden, then fade upwards. |
| Hidden beneath mod bar → LeftUpper | Move down instantly, move left instantly, then fade upwards. |
| LeftUpper → hidden beneath mod bar | Fade down, move right instantly, then move up instantly. |
| Any native destination ↔ another native destination | Original osu! animations, including their interruption and refresh behaviour. |

Pending custom requests are collapsed at the native button's next `Update`, after synchronous
ruleset/mod conversion notifications. Per-button weak state survives ruleset changes until an
outgoing custom route finishes. Unchanged destinations do not restart custom animations. Replaced
animations start from current values using framework transform replacement, without resetting to
the previous destination. A versioned fade completion performs hidden relocation only while its
request is still current; interrupted routes cannot execute an obsolete relocation later. If a
horizontal slide is interrupted by a hide request, downward fading starts in the current column;
an interrupted partial fade reverses from its current Y and alpha. Native animations resume when
the custom exit completes. Eligibility remains an independent policy with no global MS toggle.
Regression tests cover cross-ruleset paths, native-path equivalence, concurrent width changes,
batched notifications, interruptions and native hidden-position history. Loaded-control tests
exercise the real button, frame clock and native mod-selection overlay.

Actual mania scoring/PP calculation remains unimplemented. `IO2JamChartTransform<TChart>` remains
available for future domain transforms, but native mania column mods do not need a second core
implementation. `IO2JamPositionClock` reserves the timing boundary for rate transforms. If a caller
already supplies rate-adjusted chart time, do not apply playback rate a second time. Judgement must
remain in integrated chart-position space, not use a post-hoc millisecond-window multiplier.
See [rate-mod audio readiness](rate-mod-audio-readiness.md) for the implemented routing and backend limits.

## Import boundary

The initial importer is allowed to reuse the existing Realm workflow, but its implementation is
treated as replaceable infrastructure. Gameplay must not read importer records, tags or static
caches directly. Both importer and gameplay resolve a chart through an immutable source
descriptor and a format reader interface.

Imported OJN files remain external-source descriptors. Reimporting an unchanged file is a no-op
unless its decoded title, artist or charter changed, in which case those fields are refreshed on
the existing beatmaps without replacing their database identities;
reimporting a changed file from the same canonical source path creates the replacement set and then
soft-deletes the previous set. That ordering avoids losing the playable chart if parsing or Realm
insertion fails.

The managed OJN file is also used as osu!'s audio-identity file. It is never decoded as audio;
the `WorkingBeatmap` still supplies the OJM event track. This gives every difficulty of one OJN the
same native `AudioEquals` identity without making unrelated OJN sets compare equal merely because
both have an empty audio filename. Song Select can therefore retain one clock while switching
difficulty, replace only future difficulty-specific keysound events, and keep active background
layers playing.

Existing installations are migrated in place. The importer first matches the canonical source
path and the SHA-256 of the managed OJN payload, then refreshes the clean metadata marker and audio
identity on the existing Realm objects. It intentionally preserves both beatmap-set and beatmap
database identities, even when the historical importer calculated its set hash differently.

OJN has no dependable metadata encoding marker. The 2026-08-30 additions demonstrate that version
2.9 is used by both Chinese-client GBK charts and Korean/Japanese CP949 community charts. Automatic
decoding is still per field, in this order:

1. ASCII and a UTF-8 BOM require no directory inspection. Explicit reader encodings remain available
   for tooling, but no manual encoding setting is exposed in the UI.
2. Strict decoding failures/private-use mojibake and recognised Chinese catalogue labels distinguish
   individual fields. This evidence outranks every contextual preference, preserving mixed headers.
3. Ambiguous fields use a conservative directory hint: at most 96 evenly distributed OJN headers,
   at least four informative files, and at least 90% agreement. Mixed/tiny samples abstain. Only
   268-byte headers are read, with no notes, cover images or OJM data. The bounded cache is shared by
   import and gameplay, invalidated on directory changes and cleared on user-requested refresh.
4. Without a directory hint, consistent evidence elsewhere in the header is used. The historical
   version/round-trip policy is retained only as the final fallback. A translated pack takes priority
   over another header field because it may retain the original Japanese artist/charter strings.

The header-only corpus audit now covers 7,479 OJN files, including 2,654 new files in DSong, ESong,
HSong and NSong (705 non-ASCII titles). Their title/artist/charter bytes agree with CP949 apart from
the GBK charter in ESong/o2ma117.ojn. Regression cases also preserve the four previously corrected
Chinese-client titles, translated GBK titles with Japanese artists, and truncated field boundaries.
Literal question marks already written in a source header cannot be recovered by choosing another
code page; no song-name substitutions or source-file edits are performed.

The `o2lazer-encoding:2` import marker schedules a one-time metadata refresh for unchanged sources
with non-ASCII metadata, regardless of their OJN version. The existing set/beatmap identities and
score associations are retained. ASCII-only sources still skip chart parsing; subsequent refreshes
of migrated files return to the timestamp/length fast path.

The earlier 4,825-file explicit corpus test also built every playable difficulty through the clean beatmap and
preview factories: 14,473 of 14,475 chart slots produce 47,966,845 gameplay objects and 3,105,862
automatic audio events without invalid times, columns, hold durations or event ordering. It remains
explicit because this one-time catalogue calibration takes several minutes and is not a normal CI
regression test.

A second explicit compatibility test fully decodes all 4,838 OJM archives in the catalogue. This
found that some OMC background banks contain zero-length table slots. The reader now treats those
slots as intentional silence, matching its index-only path, instead of exposing an empty audio
payload as a playable sample.

Three catalogue fields end with the first byte of a multibyte GBK character because their fixed
32/64-byte metadata slots were truncated. The reader removes that final incomplete byte only when
the remaining prefix becomes wholly valid under the selected strict encoding. Malformed bytes in
the middle of a field still take the non-throwing fallback and are not silently discarded.

OJN blocks are parsed in two phases. Raw event positions and all channel-0 measure fractions are
collected first, then BPM, note and long-note endpoint positions are normalised. This avoids an
undocumented dependency on block/channel order. Measure boundaries are also retained explicitly:
native mania bar-line generation restarts its alignment at every timing point, but an OJN BPM event
may occur inside a measure and must not become a new bar line. The drawable ruleset therefore uses
the OJN measure/fraction boundaries while keeping mania's four-measure major-line cadence.

OJN sample references are normalised uniformly as `ref - 1`; background references then enter the
`1000+` bank. M30's explicit table references and OMC/OJM's positional references are not used to
guess or shift a missing sample. This matches player verification as well as the independent
Open2Jam and CXO2 parsers. In particular, a normalised id `0` that is absent from an archive is
intentional silence: borrowing archive id `1` produces the wrong hitsound. A catalogue audit over
214,026 per-file unique references also favours the fixed mapping over either adjacent shift.

## Player and replay settings

`Clean/UI/O2JamPlayerSettingsPatch` limits changes to O2Lazer's player loaders, replay loaders
and replay-player settings sidebar. osu! constructs the shared settings groups directly and
provides no ruleset factory for them, so a single constructor postfix attaches the framework's
native `OnLoadComplete` event. After loading, the adapter resolves the owning screen and uses
the replay score's ruleset where the loader has not yet applied it to the screen lease.

The controls retain their native config bindings. `CanBeShown` and `MatchingFilter` hide the
unsupported storyboard, beatmap skin, combo colours, colour normalisation, beatmap hitsounds
and mouse/touch disabling options without changing their saved values. Native resource identities
identify the controls independently of the displayed language; no private control fields or
translated text comparisons are required. Background dim/blur, beatmap offset, additional
controls and other rulesets remain untouched. Empty input groups are hidden with native layout
presence, and the one-shot load event requires no per-frame polling or lasting subscriptions.

`Clean/Audio/O2JamHitSampleLookupPatch` independently adapts the native beatmap sample lookup
gate. Only `O2JamHitSampleInfo` requests backed by `O2JamBeatmapSkin` bypass the beatmap hitsounds
switch: OJM keysounds are musical content rather than optional hit effects. Ordinary samples,
other skins and the saved global setting retain native behavior. The existing detached audio
host still applies master/music volume without inheriting global effect volume.

`Clean/UI/O2JamEditorAccessPatch` disables the native song-select Edit actions and rejects editor
screen pushes before suspension or loading when either the selected mode or source beatmap is
O2Lazer. osu! has no ruleset capability flag for disabling its generic beatmap editor. Existing
native editors also reject creating or switching to O2Lazer difficulties before state changes or
database writes. Blocked shortcuts/main-menu actions show a localised notification. This does
not affect other rulesets' editors or the independent gameplay skin editor.

## Song-select metadata and search

`Clean/SongSelect/O2JamBeatmapAttributes` supplies the public
`Ruleset.GetBeatmapAttributesForDisplay` override. It replaces inherited CS/AR/OD/HP with the
imported o2ma identifier, fixed mania stars, then the native O2Jam level. The identifier bar remains full;
the level bar uses 150 as its maximum and lets osu!'s renderer clamp overflow without changing
the displayed level. Labels and acronyms use the bilingual `O2LazerStrings` resources.

`Clean/SongSelect/O2JamFilterCriteria` implements osu!'s public `IRulesetFilterCriteria` contract.
`ln` and `note` comparisons use imported hold and total-object counts, counting every LN once.
`level` and `lv` comparisons share `O2JamStarRatingMetadata.ResolveLevel` with the display,
preferring the difficulty name and using independent O2Jam stars as fallback. Native mania stars
are never interpreted as a level. The aliases are case-insensitive and do not cap levels at 150.
Native `stars` filtering remains separate and uses the database's mania rating in either display mode.
Independent numeric clauses are intersected, including exclusions and repeated bounds.
No OJN/OJM reads or database writes are required while searching.

`Clean/SongSelect/O2JamLevelSortPatch` adds one private sentinel value immediately after native
Difficulty in the sort dropdown only while O2Lazer is selected. The item uses the localised Level
label and is removed before another ruleset publishes its filter criteria. A persisted sentinel restored outside O2Lazer falls
back to native Difficulty. Level sort separates a set's difficulties and orders their cached native
O2Jam levels ascending, with title, date-added and GUID tie-breakers matching osu!'s stable sort.
The existing Difficulty item remains separate and continues to order by `BeatmapInfo.StarRating`
(mania stars). The adapter handles the sentinel in native grouping and re-sort decisions; ordinary
sort modes and other rulesets retain their original paths.

`Clean/SongSelect/O2JamLevelGroupPatch` applies the same ruleset-scoped approach to the native
group dropdown. It inserts the localised Level item immediately after native Difficulty during the dropdown's own ruleset refresh,
so other rulesets never receive the sentinel. Cached native levels use `[N, N+10)` buckets from
`Lv.0 - 10` through `Lv.140 - 150`; levels at or above 150 use `Over Lv.150`. The headers are
language-independent. Each bucket is represented by osu!'s `StarDifficultyGroupDefinition` with
`N / 10` stars, which gives it the matching native star-group colour. Groups are ordered by ascending
level and difficulties remain independent carousel items. Native star-difficulty grouping and every
other group mode continue through osu!'s original path.

osu!'s native text matching runs first. The ruleset criterion then narrows identifier and numeric
matches: `o2ma100` requires the complete identifier tag, and bare numbers cannot match the numeric
part of an O2Jam identifier or internal import markers. Filename-derived titles and source paths
receive the same identifier protection. Ordinary metadata searches and other rulesets retain
their native matching behaviour.

## Audio ownership

The beatmap-local archive resource store feeds two deliberately different playback primitives.
Background events use seekable tracks, so pause, resume and Song Select seeks preserve the correct
position inside a long BGM layer. Playable key events use sample channels, which retain the low
latency and overlapping playback expected of hitsounds. Both event types are scheduled from the
same clock; Song Select always includes playable keysounds in its preview mix.

Long notes trigger a keysound only at the head, in both gameplay and Song Select preview.
The OJN parser retains release-record sample metadata for format inspection, but the beatmap
factory never assigns it to a tail and preview scheduling never emits release audio. Silent tail
objects remain present for O2Jam release judgement and scoring.

Song Select speculatively indexes at most two OJM archives at once, with queued work cancelled
when its WorkingBeatmap leaves the recent cache. A selected chart can start indexing immediately
without waiting behind panel-only work. The first ten seconds of BGM, automatic audio and playable
keysounds are preloaded, followed by a rolling ten-second lookahead. Audio preparation uses four
concurrent worker jobs across charts; recently used native resources are retained for at
most six WorkingBeatmaps, while active playback leases protect their stores from eviction.
The shared preload queue prioritises opening/due audio over speculative work and can promote an
existing request without creating duplicate decoders. Lookahead cursors enqueue each event once,
instead of rescanning/re-requesting the entire ten-second window on every audio frame. Gameplay
also keeps playable keysounds preloaded even though only judgements trigger their playback.

TrackStore.GetAsync completes before TrackBass has processed its native initialization and mixer
attachment. Background preparation therefore queues a StopAsync from a worker and waits for this
audio-thread fence before publishing the track. Failed decoders are disposed and treated as silence;
seek restoration and late BGM events remain pending until ready rather than blocking the audio
thread. Pausing cancels any pending start, so later decoder completion cannot resume playback.

Only preview skips the charted empty lead-in before the first nonzero-volume event whose sample
exists in the archive. All layers keep their original timestamps, gameplay still starts at zero,
and same-song difficulty transfers retain the live clock. Silence inside an audio asset is not
trimmed. Verbose logs record track creation-to-clock-ready time and the starting chart timestamp,
separately from background decoder readiness, to distinguish I/O delay from chart lead-in.

The track's synchronous Start/Stop methods wait for their audio-thread actions, matching the
framework Track contract. In particular, Stop must publish a stopped source clock before returning:
otherwise DecouplingFramedClock can observe the old running state and resume its own clock during a
pause. A stopped track may preload resources but does not consume scheduled audio events.

Both BGM and key samples follow universal and music volume. Their stores are deliberately attached
to the music mixer rather than the global sample/effect-volume tree, satisfying the ruleset contract
that neither background nor key samples are affected by effect volume. Gameplay disables the
automatic preview key-event stream while retaining BGM; object judgements then own playable
key-sample triggering.

## Ruleset identity and coexistence

The rewrite preserves the installed ruleset identity used by existing osu! databases: assembly and
ruleset class identity, short name, variant value and action values remain compatible.
The ruleset icon comes from the bundled O2Jam-specific `RulesetO2Jam.png`, under
`Textures/Icons/RulesetO2Jam`. The Mania Score preview icon `mod-mania-score.png` is bundled under
`Textures/Icons/Mods/mod-mania-score`; it supplies the UI placeholder described above.
Both follow osu!'s native icon naming/layout without changing the ruleset identity.

WorkingBeatmap integration uses an O2Lazer-specific Harmony ID and is guarded by the clean ruleset
type. The statistics/icon adapters can register through BmsRuleset's already-loaded Harmony runtime
to prevent two internalised Harmony copies from replacing the same native detour. This is optional
coexistence glue, not a compiled BMS dependency. Explicit tests load a separately built BmsRuleset
and check both its working beatmap and O2Lazer's, as well as overlapping icon/statistics hooks.

## Harmony policy

Harmony is not part of the domain or gameplay design. If osu!lazer exposes no public hook for an
O2Jam WorkingBeatmap or event-based Song Select preview, a compatibility patch may be added to the
relevant adapter module only when all of the following are true:

1. the target object is confirmed to represent an `o2lazer` beatmap;
2. the original method runs unchanged for every other ruleset;
3. failure disables only the O2Jam integration feature;
4. a focused integration test covers the scope guard;
5. the patch has a unique Harmony ID that does not overlap BmsRuleset.
