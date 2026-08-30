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
| `Clean/Replays/`, `Clean/Mods/` | v5 replay persistence/input and autoplay; future transforms must consume core contracts. |
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

The O2Jam speed setting is mapped onto that model. A fixed-scroll-speed setting selects whether
the visual time range remains constant through BPM changes. This setting never changes judgement:
judgement is performed in chart-position space.

## Default difficulty rating

Default O2Lazer difficulty is the OJN chart level multiplied by `0.1`. O2Jam levels already encode
the chart author's intended difficulty ordering, while the scale conversion makes three-digit
O2Jam levels fit osu!'s conventional star display. Native mania strain difficulty and mania
judgement are reserved for a future, explicit mania-scoring mod; they are not default behaviour.

## Mod extension points

Autoplay is exposed for the native playback/editor contract. Mirror, Random, rate-changing and
mania-scoring mods are not yet exposed. The core reserves two interfaces:

- a chart transform for future Mirror/Random-style column transforms;
- a position clock for future playback-rate transforms.

Rate-changing mods must change the mapping from real time to chart position/effective BPM. They
must not multiply a millisecond hit window after judging against the source BPM.

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

## Song-select metadata and search

`Clean/SongSelect/O2JamBeatmapAttributes` supplies the public
`Ruleset.GetBeatmapAttributesForDisplay` override. It replaces inherited CS/AR/OD/HP with the
imported o2ma identifier followed by the native O2Jam level. The identifier bar remains full;
the level bar uses 150 as its maximum and lets osu!'s renderer clamp overflow without changing
the displayed level. Labels and acronyms use the bilingual `O2LazerStrings` resources.

`Clean/SongSelect/O2JamFilterCriteria` implements osu!'s public `IRulesetFilterCriteria` contract.
`ln` and `note` comparisons use imported hold and total-object counts, counting every LN once.
`level` and `lv` comparisons share `Core/O2JamDifficultyRating.ResolveLevel` with the display
and difficulty calculator, preferring the imported difficulty name and retaining the existing
star-rating fallback. The aliases are case-insensitive and do not cap levels at 150.
Independent numeric clauses are intersected, including exclusions and repeated bounds.
No OJN/OJM reads, database writes or additional Harmony patches are required while searching.

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
The ruleset icon comes from the bundled O2Jam-specific `o2jamruleset.png` resource.

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
