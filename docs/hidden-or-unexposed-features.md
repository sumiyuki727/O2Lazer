# Hidden or Unexposed Features

This file records O2LAZER features that are fully implemented but whose user-facing
settings/UI channel is hidden or does not exist. The goal is to prevent future
refactors from deleting the wiring while removing the visible entry, which would
silently break the feature.

## 1. Visual offset adjustment (manual + automatic) - hidden settings UI

状态：完整实现，设置项被显式隐藏。

- Config keys: `O2LazerRulesetSetting.VisualOffset`, `O2LazerRulesetSetting.AutomaticallyAdjustVisualOffset`
  in [O2LazerRulesetConfigManager.cs](../../osu.Game.Rulesets.O2Lazer/Configuration/O2LazerRulesetConfigManager.cs).
- Runtime wiring:
  - `O2LazerGameplaySettingsController` binds `VisualOffset` to `O2LazerPlayfield.VisualOffset`.
  - `O2LazerGameplayCompletionController` records median hit error and, when
    `AutomaticallyAdjustVisualOffset` is enabled, writes the suggested offset.
  - `O2LazerRulesetRuntime.VisualOffsetSuggestions` keeps suggestion history.
- UI: the "Visual" settings group plus `VisualOffsetAdjustControl` exist but are gated by
  `enable_visual_offset_settings = false` in
  [O2LazerSettingsSubsection.cs](../../osu.Game.Rulesets.O2Lazer/Settings/O2LazerSettingsSubsection.cs:39).
- To re-enable: flip that constant to `true`. Do not remove the config defaults, the
  playfield binding, the completion-controller suggestion logic, or `VisualOffsetAdjustControl`.

## 2. Scroll direction (up/down flip) - hidden settings entry

状态：设置下拉项已隐藏（2026-08-25），全部运行逻辑保留。

- The dropdown lives in the "Scroll speed" group in
  [O2LazerSettingsSubsection.cs](../../osu.Game.Rulesets.O2Lazer/Settings/O2LazerSettingsSubsection.cs).
- The dropdown is gated by `enable_scroll_direction_settings = false` in that file.
- Config key: `O2LazerRulesetSetting.ScrollDirection`, default `Down`.
- Runtime wiring:
  - `O2LazerDrawableRuleset` binds the config to `O2LazerLocalScrollingInfo.Direction`.
  - `O2LazerPlayfield` copies it to `ScrollController.Direction`.
  - KeyArea, HitTarget, notes, LN pieces, bar lines, column lights and hit explosions
    flip based on this direction.
- Keep the enum, the config key, the config binding, and all flip logic intact. To
  re-enable the entry, flip `enable_scroll_direction_settings` to `true`.

## 3. Stage HUD skin-editor component (judgement line / light offsets)

状态：已实现，入口只在皮肤编辑器布局里，不在规则设置中。

- `O2LazerStageHud` exposes `[SettingSource]` bindables:
  `ProportionalWidthReference`, `JudgementLineOffset`, `LightPositionOffset`.
- `O2LazerStageHudController` syncs those offsets into
  `O2LazerStage.SetHitTargetPositionOffset` / `SetLightPositionOffset` and applies the
  stage HUD transform.
- This is not visible in the normal settings panel. Do not remove the controller or the
  HUD component while cleaning up settings UI.

## 4. In-game scroll speed hotkeys

状态：已实现，使用 osu! 全局按键绑定，没有 O2LAZER 专用设置项。

- `O2LazerDrawableRuleset` handles `GlobalAction.IncreaseScrollSpeed` /
  `GlobalAction.DecreaseScrollSpeed` during lead-in, breaks and replays.
- The adjustment channel is osu!'s global key binding UI, not this ruleset's settings.

## General note

- Ruleset rule: background sample and KeySound volumes must NOT be affected by the global
  effect volume setting. This behavior is implemented without any toggle; keep it intact
  when touching volume/mixing code.
