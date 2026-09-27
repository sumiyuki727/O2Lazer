# 补丁清单与失败策略

这份清单对应 `O2JamCompatibilityPatches` 的安装顺序和当前 osu!lazer 2026.921.0。每项先尝试复用原生入口；表中的缺口是当前必须进入原生私有路径的原因。更新宿主版本时，应逐项确认缺口是否仍存在，能移回原生接口的补丁应删除。

| 补丁 | 级别 | 原生能力的缺口 |
|---|---|---|
| `O2JamWorkingBeatmapHook` | 必需 | 没有按 ruleset 创建外部 OJN `WorkingBeatmap` 的工厂入口。 |
| `O2JamDifficultyIconPatch` | 可选 | 社区 ruleset 的负在线 ID 无法在所有难度图标位置正确解析。 |
| `O2JamComboCompatibilityPatches` | 必需 | 原生连击显示和断连音效不认识 O2Jam 的 `-1` 内部哨兵。 |
| `O2JamSongSelectRankPatch` | 可选 | 共用来源文件时，原生本地成绩展示不能精确对应 OJN 难度。 |
| `O2JamBeatmapBoundaryPatches` | 必需 | 原生跨模式转换入口会尝试把 O2Jam 谱面交给不兼容的 ruleset。 |
| `O2JamReplayPersistencePatch` | 必需 | 原生 replay 导入、写入入口不保存 O2Jam 的专用数据。 |
| `O2JamPerformanceEligibilityPatch` | 可选 | 原生 PP/资格显示入口没有 O2Jam 的标签呈现规则。 |
| `O2JamPlayerSettingsPatch` | 可选 | 原生玩家设置组没有按 O2Jam 显示特定控件的扩展点。 |
| `O2JamModSelectAttributesPatch` | 可选 | Mod 选择器没有直接采用 O2Jam 难度属性的公开入口。 |
| `O2JamManiaScoreRulesetSelectionPatch` | 必需 | 切换 ruleset 后，原生 Mod 转换不保证强制 Mania Score 的依赖。 |
| `O2JamManiaScoreModAvailabilityPatch` | 必需 | 原生 Mod 选择界面不限制仅适用于 Mania Score 的 Mod。 |
| `O2JamManiaScoreStatisticsPatch` | 可选 | 原生结果统计没有区分 Mania Score 的展示路径。 |
| `O2JamDifficultyCachePatch` | 可选 | 原生难度绑定不会直接采用已持久化的 Mania 星级。 |
| `O2JamStarRatingDisplayPatch` | 可选 | 原生星级文本无法按 O2Jam 的 LV 规则显示。 |
| `O2JamStarRatingPresentationPatch` | 可选 | 原生难度组件不能直接绑定 LV 与 Mania 星级的两种颜色来源。 |
| `O2JamCarouselTransitionPatch` | 可选 | 原生曲库过渡时序没有 O2Jam 模式切换所需的入场触发点。 |
| `O2JamModeSwitchDirectionPatch` | 可选 | 原生音轨方向推断会在跨模式切换时重复扫描曲库。 |
| `O2JamLevelFilterPatch` | 可选 | 原生筛选面板没有 LV 条件及其显示。 |
| `O2JamLevelSortPatch` | 可选 | 原生曲库排序没有 LV 顺序。 |
| `O2JamLevelGroupPatch` | 可选 | 原生分组没有 LV 分组。 |
| `O2JamHitSampleLookupPatch` | 必需 | 原生采样门控会拒绝 OJM 音色，且播放声道需要独立音量规则。 |
| `O2JamHeldKeySoundPatch` | 必需 | Mania 的列级补音会在已判定 LN 松手后回退到父对象，反复播放头部 KS；只在 O2Lazer 列中跳过这个已触发的 LN。 |
| `O2JamEditorAccessPatch` | 可选 | 原生入口仍可尝试打开 O2Jam 谱面编辑器；原生 `Editor` 只允许 `ILegacyRuleset` 保存，因此该补丁用于提前拦截并给出明确提示。 |

安装只尝试一次。必要补丁失败时，ruleset 仍可被宿主枚举，但 `CreateDrawableRulesetWith` 会抛出包含失败补丁名称的 `RulesetLoadException`，阻止进入可能损坏成绩或缺少音色的玩法；可选补丁失败只记录日志。安装器在异常时按独立 Harmony ID 撤销本项已经安装的钩子，包含可能由 BMSRuleset 携带的另一份 Harmony 运行时。BMSRuleset 携带独立 Harmony 时，`Host/Compatibility/O2JamBmsHarmonyCompatibility` 为重叠方法按载入顺序登记 O2Lazer 钩子；只以 O2Lazer 的 Harmony ID 回滚。它仅服务已知 BMS 组合，不作为通用设施。指定版本的测试矩阵、原生缺口与仍待客户端验收的范围见[共存验证](bms-coexistence.md)。
