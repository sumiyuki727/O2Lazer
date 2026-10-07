# 补丁清单与失败策略

维护状态（2026-10-06）：现行补丁与原生缺口清单；当前 26 项；EX 锁血/F 使用原生结果接口，没有新增补丁。
文档职责与最新状态入口见[索引](README.md)。

这份清单对应 `Composition/O2JamCompatibilityPatches.cs` 的安装顺序和当前 osu!lazer 2026.1005.0。总安装器只负责跨模块组装；具体补丁仍归所属功能，工作谱面钩子位于 `Host/Beatmaps`，BMS 共存适配位于 `Host/Compatibility`。每项先尝试复用原生入口；表中的缺口是当前必须进入原生私有路径的原因。更新宿主版本时，应逐项确认缺口是否仍存在，能移回原生接口的补丁应删除。

| 补丁 | 级别 | 原生能力的缺口 |
|---|---|---|
| `O2JamWorkingBeatmapHook` | 必需 | 没有按 ruleset 创建外部 OJN `WorkingBeatmap` 的工厂入口。 |
| `O2JamDifficultyIconPatch` | 可选 | 社区 ruleset 的负在线 ID 无法在所有难度图标位置正确解析。 |
| `O2JamComboCompatibilityPatches` | 必需 | 原生连击显示和断连音效不认识 O2Jam 的 `-1` 内部哨兵。 |
| `O2JamHitErrorMeterPatch` | 可选 | 原生条形误差表直接共用毫秒判定窗口与 TimeOffset，没有独立显示范围/坐标投影接口；限定适配两种原生条形表的 tick 坐标及不对称色区，复用标记与动画。 |
| `O2JamSongSelectRankPatch` | 可选 | 共用来源文件时，原生本地成绩展示不能精确对应 OJN 难度。 |
| `O2JamBeatmapBoundaryPatches` | 必需 | 原生跨模式转换入口会尝试把 O2Jam 谱面交给不兼容的 ruleset。 |
| `O2JamReplayPersistencePatch` | 必需 | 原生 replay 导入、写入入口不保存 O2Jam 的专用数据；单条谱面查询不能验证旧共享 Hash 与 MD5 的唯一一致身份。A04-2 复用 ScoreImporter 现有 Realm 接缝限定查询，不改变正式 v5 编码。 |
| `O2JamPerformanceEligibilityPatch` | 可选 | 原生 PP/资格显示入口没有 O2Jam 的标签呈现规则。 |
| `O2JamPlayerSettingsPatch` | 可选 | 原生玩家设置组没有按 O2Jam 显示特定控件的扩展点。 |
| `O2JamModSelectAttributesPatch` | 可选 | Mod 选择器没有直接采用 O2Jam 难度属性的公开入口。 |
| `O2JamManiaScoreRulesetSelectionPatch` | 必需 | 切换 ruleset 后，原生 Mod 转换不保证强制 Mania Score 的依赖。 |
| `O2JamManiaScoreModAvailabilityPatch` | 必需 | 原生 Mod 选择界面不限制仅适用于 Mania Score 的 Mod。 |
| `O2JamManiaScoreStatisticsPatch` | 可选 | 原生结果统计没有区分 Mania Score 的展示路径。 |
| `O2JamDifficultyCachePatch` | 可选 | 原生难度绑定不会直接采用已持久化的 Mania 星级。 |
| `O2JamNativeDifficultyPersistencePatch` | 可选 | 原生启动后台从完整难度结果中只保存 StarRating，丢弃 MaxCombo；在该原生写入事务内保存已计算、已验证的 O2Lazer 基线属性，避免后置阶段重算。 |
| `O2JamFileVerificationPatch` | 可选 | 原生 RealmFileStore.Add 会校验并修复文件，但不返回修复结果；观察本 ruleset 当前写入的校验结果，避免为缓存失效通知提前再读一次文件。 |
| `O2JamStarRatingDisplayPatch` | 可选 | 原生星级文本无法按 O2Jam 的 LV 规则显示。 |
| `O2JamStarRatingPresentationPatch` | 可选 | 原生难度组件不能直接绑定 LV 与 Mania 星级的两种颜色来源。 |
| `O2JamCarouselTransitionPatch` | 可选 | 原生曲库过渡时序没有 O2Jam 模式切换所需的入场触发点。 |
| `O2JamModeSwitchDirectionPatch` | 可选 | 原生音轨方向推断会在跨模式切换时重复扫描曲库。 |
| `O2JamLevelFilterPatch` | 可选 | 原生筛选面板没有 LV 条件及其显示。 |
| `O2JamLevelSortPatch` | 可选 | 原生曲库排序没有 LV 顺序。 |
| `O2JamLevelGroupPatch` | 可选 | 原生分组没有 LV 分组。 |
| `O2JamHitSampleLookupPatch` | 必需 | 原生采样门控会拒绝 OJM 音色；现有 GetChannel 接缝限定当前游玩的 OJM 声道，协调空击/判定实例的停旧重播与尾音暂停，不改变预览、背景或其他 ruleset。 |
| `O2JamResolvedKeySoundPatch` | 必需 | Mania 的列级补音会回退到已判定单点或 LN 父对象；只在 O2Lazer 列中跳过已判定单点/头部，MS 按保留的 OJM 身份适配，回退重判按原生 lifetime 恢复。 |
| `O2JamEditorAccessPatch` | 可选 | 原生入口仍可尝试打开 O2Jam 谱面编辑器；原生 `Editor` 只允许 `ILegacyRuleset` 保存，因此该补丁用于提前拦截并给出明确提示。 |

文件校验观察器仅在 O2Lazer 的同步 Add 调用内按线程、实例和预期 Hash 生效；其他文件库操作保持原生路径。它不改变校验布尔值，也不接管写入。安装失败保留原先提前校验；检查未被观察或回调失败时保守通知本 ruleset 的活动所有者。升级宿主须复核私有校验方法签名和调用路径；若原生提供修复结果入口，应删除该观察器。2026-10-03 已通过 121 项迁移/文件恢复回归和 BMS 两种载入顺序检查。

安装只尝试一次。必要补丁失败时，ruleset 仍可被宿主枚举，但 `CreateDrawableRulesetWith` 会抛出包含失败补丁名称的 `RulesetLoadException`，阻止进入可能损坏成绩或缺少音色的玩法；可选补丁失败只记录日志。安装器在异常时按独立 Harmony ID 撤销本项已经安装的钩子，包含协调器实际登记的外部 Harmony 运行时。BMSRuleset 携带独立 Harmony 时，`Host/Compatibility/O2JamBmsHarmonyCompatibility` 为重叠方法按载入顺序登记 O2Lazer 钩子；只以 O2Lazer 的 Harmony ID 回滚。BMS 适配器只服务已知组合；注册、后加载与回滚由通用 O2JamPatchCoordinator 处理，见[协调设计](ruleset-compatibility-design.md)。安装及已知提供方后加载完成时输出一次协调器 Verbose 清单，查询失败仅作为诊断证据，不改变玩法可用性。指定版本的测试矩阵、原生缺口与仍待客户端验收的范围见[共存验证](bms-coexistence.md)。

2026-10-05 已在正式版 2026.1005.0 二进制上重新验证全部 26 项安装、常规行为与 BMS
两种载入顺序；发布版没有提供可替代现有私有接缝的新增接口。本轮未增加或重写补丁，
验证范围与原生变更见[宿主适配记录](lazer-20261005-compatibility.md)。

## Tick 误差条适配（2026-10-05）

`Presentation/GameplayFeedback/O2JamHitErrorMeterPatch` 只处理原生
`LegacyBarHitErrorMeter` 和 `BarHitErrorMeter` 的确切类型，且仅当其实际读取的
对象窗口为 `O2JamFrameworkHitWindows`。空谱面、原生 Mania、MS、非位置色表及
第三方自定义子类保持原路径；原生类型的序列化 HUD 也通过同一个加载接缝生效。

原生 `HitErrorMeter.load` 完成后替换该显示组件自己的窗口；Legacy 的
`OnNewJudgement`、Argon 的 `OnNewJudgement` 及其池化标记回调各有一处
TimeOffset 读取，改成只读显示投影。Argon 的移动平均与标记使用同一刻度。
早侧 COOL 锚定原生 OD7、1× Great 色区半宽，晚侧同刻度增加 1 tick。
原生窗口只提供对称单值，不能直接绘制 6/7、18/19、25/26 的早晚边界。Legacy.load
postfix 保留原生轴中心，调整原生色块宽度和位置；Argon.load postfix 延长根轴，
调整已有早侧色块范围，保留原生晚侧布局、渐变、厚度、入场动画和 HUD 设置。
最大轴用 ±26 tick 包络，早侧多出一 tick 透明留白。没有每帧重建或新监听。
BAD 统一映射到 HitResult.Meh，色区、渐变、标记与结算直接使用原生 50 黄色。
旧 GetColourForHitResult 取色 prefix 已删除；MS 保留原生判定和配色。
不补丁全局 TimeOffset getter，不改物件窗口或结果。匹配的私有签名或预期读取
数量变化时，本项按独立 Harmony ID 全部回滚，记录可选功能失败并恢复原生显示。
升级时复测两种表的加载、标记/平均箭头、清空，以及原生 Mania/MS 隔离；若宿主
公开独立显示窗口和偏移投影入口，应移除此补丁。

升级还需核对 Legacy 原生子树顺序及 Argon 的早侧容器、单个根容器与轴长属性；轴长调整必须先于原生
LoadComplete 的入场动画，不能用缩放整棵树来改变图标或标记厚度。

固定范围、Mania OD7 黄300基准和统计单位见[玩法规格](o2jam-behaviour-spec.md#hit-error-display)。


2026-10-05，用户确认 COOL 基准和 MS 变速修复表现正常，临时误差条监听及
其构建开关已移除。2026-10-06 BAD 改为 Meh 判定及统计键并删除取色补丁；两种表的实际色区和
标记、MS/Mania 隔离及 BMS 双顺序已通过定向测试，见[显示验收](hit-error-display-validation.md)。
