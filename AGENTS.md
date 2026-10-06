# AGENTS.md

## Local References

- Use sibling checkouts for API/source inspection: `..\osu`, `..\osu-framework`, `..\rulesets`.
- If missing, clone `https://github.com/ppy/osu.git` and `https://github.com/ppy/osu-framework.git`; community rulesets are linked from `https://github.com/ppy/osu/discussions/13096`.
- Treat sibling checkouts as read-only references. Never edit, format, stage, commit, or otherwise modify files in `..\osu`, `..\osu-framework`, or `..\rulesets`.
- Make all implementation changes in this repository. The old BMS-derived project is retained as a read-only reference at `D:\o2jam-lazer`.

- The pre-refactor worktree at `D:\o2lazer-clean-rewrite` is a read-only implementation reference. Make refactor changes in `D:\o2lazer` on `master`.

## Ruleset Rules

- Prefer native osu!lazer / osu!framework implementations whenever they satisfy the required behaviour and presentation. Add custom implementations, adapters, or patches only when native behaviour cannot meet the requirement; document the specific gap and keep the adaptation scoped to it.

- Background Sample and KeySound volumes should NOT be affected by the effect volume of global volume settings.

## Code Style

- use `var` for local variables when possible
- use collection expressions when possible (e.g. `string[] vowels = ["a", "e", "i", "o", "u"]`)

## Localisation

- All user-facing text must use the `O2LazerStrings` localisation system; do not hard-code labels, abbreviations, formatted values, or units in UI code.
- Keep the English base resource and all supported `.resx` translations in sync when adding or changing user-facing text.
- User-selected exception: the Random algorithm caption, names, and descriptions remain fixed English through `O2LazerStrings` English resource access. Keep those seven resource entries identical in all supported translations; do not localise algorithm identities or change saved enum values.

## Test Rules

- Do NOT run benchmark tests without asking. Benchmarks can take minutes and consume significant resources.
- Always run tests with `--filter` arguments — never run the full unfiltered test suite.

## Git Style

- Match the existing commit subject style: `type(scope): summary` when a focused scope helps, or `type: summary` when it does not.

## Comment Style

- Write **why**-type comments (the rationale, intent, or non-obvious trade-off), not **what**-type comments (restating what the code already says).
