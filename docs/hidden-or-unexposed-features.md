# Historical feature checklist

This document concerns the pre-rewrite implementation, not switches which can be enabled in the
current code. Its source snapshot is [commit 721052e](https://github.com/sumiyuki727/O2Lazer/tree/721052e).
Old source paths below are relative to that snapshot; they are intentionally absent from the active
build. Retain the checklist when deciding which features to reimplement, rather than restoring old
runtime dependencies.

| Historical feature | Former implementation | Current disposition |
|---|---|---|
| Manual/automatic visual offset | `Configuration/O2LazerRulesetConfigManager.cs`, `UI/Gameplay/O2LazerGameplaySettingsController.cs`, `O2LazerGameplayCompletionController.cs` | Not carried forward as automatic calibration. Audio diagnostics observe only; they do not change offsets. |
| Up/down scroll direction | `Settings/O2LazerSettingsSubsection.cs`, `UI/O2LazerScrollingDirection.cs` | Available in current O2Jam settings using mania's direction model. |
| Stage HUD line/light offsets | `UI/HudComponents/O2LazerStageHud.cs`, `O2LazerStageHudController.cs` | Old bespoke controller not restored. Current HUD/Playfield layers use native skin-editor targets; Jam/pill components remain future work. |
| Scroll-speed hotkeys | `UI/O2LazerDrawableRuleset.cs` | Native global scroll actions call the current `O2JamDrawableRuleset.AdjustScrollSpeed`. |
| Music-volume routing for BGM and keysounds | Former custom audio pipeline | Retained as a required contract in the new stores/mixer ownership; effect volume must not control these sounds. |

The current implementation and settings are described in [the architecture](clean-rewrite-architecture.md)
and [the README](../README.md). Copying an old hidden-control constant into the rewrite does not
restore its dependencies and is not a supported migration procedure.
