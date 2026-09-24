# 产品表现与曲库功能归属

更新：2026-09-24。本清单描述当前实现和预期所有者，不将所有 Harmony 补丁统称为“产品层”。技术依赖与风险见[架构](clean-rewrite-architecture.md)及[问题清单](architecture-audit.md)。

| 功能 | 当前代码位置 | 责任边界 |
|---|---|---|
| 等级/星数、难度色与动画 | `Presentation/DifficultyLevels/Policy`、`Native/O2JamDifficultyColourBinding.cs`、`O2JamStarRatingPresentationPatch.cs`、`O2JamStarRatingDisplayPatch.cs` | Lv 文本和 Lv/10 颜色只用于显示；实际 Mania 星数、计算与 PP 不读显示投影。原生 `StarRatingDisplay` 执行数值和颜色动画。 |
| o2ma/SR/LV 属性 | `Presentation/DifficultyLevels/Policy/O2JamBeatmapAttributes.cs`、`Native/O2JamModSelectAttributesPatch.cs` | 标题、选歌和 Mod 面板的属性展示；异步 SR 待计算时不显示 -1 中间值。 |
| 难度计算和缓存 | `Host/Difficulty/Calculation`、`Caching`、`Data` | 原生 Mania 算法、已保存版本/最大 Combo 和缓存失效；不属于 UI 动画。 |
| 等级/音符比例/编号查询 | `Integration/Library/Filtering/O2JamFilterCriteria.cs` | 搜索语义与匹配，不负责列表绘制。 |
| 等级筛选、排序、分组 | `Presentation/LibraryBrowsing/O2JamLevelFilterPatch.cs`、`O2JamLevelSortPatch.cs`、`O2JamLevelGroupPatch.cs`、`O2JamLevelSpreadAdapter.cs` | 曲库组织；等级政策提供数值，不把筛选逻辑搬入等级颜色模块。 |
| 跨模式选曲过渡 | `Presentation/LibraryBrowsing/O2JamCarouselTransitionPatch.cs`；方向接入在 `Host/Audio/O2JamModeSwitchDirectionPatch.cs` | 复用原生筛选、面板淡入和 Now Playing 滑动；适配同帧条件、等待及有序方向，属于 A02/A11 的补丁审查范围。 |
| 当前难度本地成绩 | `Presentation/LibraryBrowsing/O2JamSongSelectRankPatch.cs`；通知接入在 `Host/Persistence/Realm` 同名 partial | 选择政策与 Realm 集合通知分开；同程序集仍有 partial 耦合。 |
| PP 资格 | `Integration/Performance/Eligibility/O2JamPerformanceEligibility.cs` | MS 且所有展开 Mod 原生 Ranked；不计算 PP，也不决定标签动画。 |
| PP 计算 | `Host/Performance/Calculation/O2JamPerformanceCalculator.cs` | 计算入口独立于资格和展示。 |
| 资格标签与 No Mod 动效 | `Presentation/Performance/O2JamPerformanceEligibilityPatch.cs`、`O2JamNoModBadgeAnimation.cs` | 消费资格，保持原生组件动效；不改 Mod.Ranked 或成绩真值。 |
| Mania Score 视觉/Mod 可用性 | `ManiaScore/UI`；选择政策在 `ManiaScore/Mods` | 横切玩法路线的显示端，不把原生 Mania 计分混入 O2Jam Core。 |
| 结算统计与 Combo | `Presentation/GameplayFeedback/O2JamResultStatisticsAdapter.cs`、`O2JamComboCompatibilityPatches.cs`、`Skinning/Components` | 展示和皮肤消费判定状态；核心内部 -1 Combo 不应从 UI 回写。 |
| 长条视觉与旧皮肤 | `Host/Gameplay/Objects/O2JamDrawableHoldNote*.cs`、`Presentation/GameplayFeedback/Skinning` | Drawable 承担必要原生回调/对象池生命周期，视觉只读取已解析状态；细分见 A10。旧 HUD 序列化适配仍需保留。 |
| 设置、图标及编辑器入口 | `Presentation/AccessAndSettings`；会话在 `Host/Configuration`；Realm 注入在 `Host/Persistence/Realm` | UI 控件与导入操作生命周期分开；原生编辑器不支持 OJN/OJM，入口保持受限。 |
| 本地化 | `Host/Localisation`、`Resources/Localisation` | 为各 UI 模块服务的设施，不是某个界面的私有翻译；新增文本须同步英文和全部支持语言。 |

等级系统和曲库查询允许共享等级数据，不共享 UI 状态。音频预览、解码、判定、持久化和回放不因“改善用户体验”而搬进 Presentation。新增功能先选择数据/规则所有者，再找到原生扩展点，最后决定是否需要局部补丁；补丁逐项登记在 A01/A02 的治理清单。当前视觉效果曾由用户验收部分跨模式颜色与标题 SR 场景，不等于所有皮肤、Mods 或宿主升级均已验收。
