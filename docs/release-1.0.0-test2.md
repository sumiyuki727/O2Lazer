# O2Lazer 1.0.0-test2

The second 1.0.0 test release targets **osu!lazer 2026.1005.0**. The assembly version remains
**1.0.0** to preserve ruleset identity; the release tag identifies this test build.

## Changes since the first test release

- Separates OJN/OJM decoding and O2Jam gameplay rules into independent modules, with host storage,
  native integration and presentation boundaries checked automatically.
- Implements Mania Score using native mania scoring, health, statistics and PP. Carrying Easy,
  Hard Rock or Classic into O2Lazer selects Mania Score; non-MS play remains ineligible for PP.
- Reworks library updates, source-content identity, missing-source cleanup and deferred star calculation.
  Imported charts become available before the remaining star calculation finishes. Update counters use
  unique source content and animate from zero; clearing imports also reports progress.
- Reworks preview/gameplay audio, pause handling and replay seeking while preserving authored sample
  references and keeping musical samples independent of global effect volume and the hitsound toggle.
- Refines O2Jam judgement, LN presentation, randomisation and zero-life EX recording policies.
- Restores native difficulty presentation transitions while retaining O2Jam level colours and values.
- Tracks folder-collection ownership by native collection ID. Enabling folder collections now shows
  synchronisation and completion notifications. Untracked old collections are preserved.
- Fixes the optional Percy LN-body extension: correct column width, fixed-scale lower-half segments,
  no overlap with the first body span, and no enlarged final remainder. Disabled repair keeps native
  Mania rendering. The reported skin issue has passed user testing.
- Removes the CI source checks' dependency on ripgrep; GitHub's Windows runner can execute them directly.

## Install / upgrade

1. Close osu!lazer.
2. Download `osu.Game.Rulesets.O2Lazer.dll` and replace the previous DLL in your lazer data directory's
   `rulesets` folder. Keep backups outside that folder and install only one O2Lazer version.
3. Start lazer, select your library path in **Settings → O2Jam**, and run **Update beatmaps**.
   There is no need to clear imports first.

Keep the original `.ojn` and matching `.ojm`, `.omc` or `.m30` files available. Existing scores are
retained; pre-rewrite unmarked test replays are intentionally unsupported. Current replays use schema v5.
`SHA256SUMS.txt` verifies the attached DLL.

## Validation and remaining limits

- 1,382 filtered tests passed with no failures or skips; production/diagnostic dependency analysis found
  no compilation errors, boundary violations or stale exceptions.
- The maintenance commit passed GitHub Actions. Remote CI covers portable modules and the checker;
  full host analysis was performed locally against matching binaries.
- Seeking into the middle of an already-started short sample does not reconstruct its remaining audio.
- Old folder collections without ownership records remain untouched and may coexist with newly created
  collections bearing the same name; automatic adoption is not implemented.
- This is a prerelease, not a declaration that every skin, replay, library migration or host version has
  been validated. Remaining issues are recorded in the repository's architecture/review documents.

See [the maintenance review](maintenance-update-2026-10-06.md),
[storage contract](library-persistence-contract.md), and [behaviour specification](o2jam-behaviour-spec.md).
