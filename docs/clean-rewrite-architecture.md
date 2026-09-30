# O2Lazer 当前架构

更新：2026-09-30。本文件描述当前 `master` 的责任边界和新增功能指引，不用历史目录名或某次测试数表示架构已全部完成。[全项目审查和待处理问题](architecture-audit.md)、[路线图](refactor-roadmap.md)、[玩法行为规格](o2jam-behaviour-spec.md)分别负责风险、进度和游戏规则。

## Design intent and decision precedence

1. 有可靠依据的 O2Jam 时间、判定、长条、分数、Combo、Jam、药丸、生命和 OJM 事件语义优先。
2. O2Jam 没定义、而 osu! 需要的概念，使用最接近的原生 Mania/lazer 契约表达，例如非 MS 准确率与字母评级。
3. 无一致 O2Jam 权威的应用生命周期沿用 lazer，例如暂停与恢复。
4. 在满足需求时先使用原生实现。只有原生接口无法给出需要的行为或表现，才加局部适配/补丁；记录具体缺口、作用范围、失败影响和升级验证方式。尽量继续让原生控件执行动画、布局、输入和结果生命周期。

忠于原生体验不等于复制原生模式的每个内部中间值。跨模式进入 O2Lazer 时，目标 OJN 往往尚未选定；不能为即时变色猜测最终等级。当前等级色将 Lv/10 投影到原生 `StarRatingDisplay`，MS 使用 Mania 星数；两个模式均过滤不影响颜色的 `MaxCombo` 元数据更新，避免重启原生过渡。标题 SR 待计算时保留先前有效星数或空白，O2MA/LV 仍按原生行入场。

## 构建与依赖方向

```text
O2Jam.Formats ──> 纯解码结果
O2Jam.Core    ──> 纯玩法规则/状态
         \       /
       Integration ──> osu! 谱面、对象、判定与查询契约
             ↑
       Host / ManiaScore ──> 原生生命周期、音频、Mods、缓存、存储
             ↑
        Presentation ──> 原生控件、等级/颜色、曲库、HUD/皮肤
```

箭头表达推荐的数据使用方向，不保证现有 C# 引用严格满足该图。除两个独立项目外，其余区域编译在同一 ruleset 项目；玩法接缝已按 A06 核对，命名及完整依赖矩阵仍由 [A07、A12](architecture-audit.md#初始审查问题) 处理。`O2LazerRuleset` 是 ruleset 入口，`Host/Compatibility/O2JamCompatibilityPatches` 汇总补丁安装；目录移动本身不产生编译隔离。

| 目录 | 应有责任 | 当前代表入口 |
|---|---|---|
| `O2Jam.Core/` | 音乐位置、BPM、判定窗口、长条状态、整局分数/Combo/Jam/药丸/生命；只依赖 .NET | `O2JamTimingMap`、`O2JamJudgementEngine`、`O2JamGameplayState` |
| `O2Jam.Formats/` | OJN/OJM/OMC/M30 字节到格式数据；不写数据库、不创建 osu 对象 | `OjnReader`、`OjmReader` |
| `Integration/Formats`、`Integration/Beatmaps` | 格式数据到可游玩谱面、外部资源快照、原生 WorkingBeatmap 与转换边界 | `OjnBeatmapFactory`、`O2JamExternalChartResources`、`O2JamWorkingBeatmap` |
| `Integration/Objects`、`Integration/Scoring` | 时间和判定转译、核心结果与原生结果关联；向宿主提供判定解析契约 | `O2JamJudgementBridge`、`IO2JamJudgementResolver`、`O2JamJudgementHistory` |
| `Integration/Library/Filtering`、`Integration/Performance/Eligibility` | 曲库查询语义和 PP 资格策略，不决定绘制 | `O2JamFilterCriteria`、`O2JamPerformanceEligibility` |
| `Host/Library`、`Host/Configuration` | 导入应用流程、写入契约、操作会话、配置 | `O2JamImportService`、`IO2JamLibraryWriter`、`O2JamLibrarySettingsSession` |
| `Host/Audio`、`Host/Gameplay`、`Host/Replays`、`Host/Mods`、`Host/Difficulty` | 原生轨道、Drawable/对象池、计分协调、输入回放、Mod、难度缓存与计算 | `O2JamPreviewTrack`、`O2JamDrawableRuleset`、`O2JamScoreProcessor`、`O2JamDifficultyCalculator` |
| `Host/Persistence/Realm` | 当前 Realm 实现及原生存储接入 | `O2JamLibraryWriter`、`O2JamReplayPersistencePatch` |
| `ManiaScore/` | 与 O2Jam 核心并列的 Mania 玩法路线及其展示联动 | `O2JamManiaScoreBeatmapAdapter`、`ManiaScoreProcessorAdapter` |
| `Presentation/` | 等级/星数、难度颜色、曲库组织、资格标签、设置界面、皮肤和 HUD 展示 | `DifficultyLevels`、`LibraryBrowsing`、`Performance`、`GameplayFeedback` |
| `Resources/` | 本地化、音效、图标 | `Localisation/O2LazerStrings*.resx` |

`Host/Localisation` 是服务 UI 的本地化设施，不拥有玩法规则。`Host/Compatibility` 是安装清单，不把所有补丁变为同一功能。旧 namespace 为持久化/反射兼容暂未全部迁移；判断边界以项目引用和真实调用为准。

## 玩法与原生宿主的接缝

Core 接收小节位置与普通规则配置，输出原始/药丸修正后的判定、长条状态和整局快照；它不认识 `HitObject`、Drawable、Mod、Realm 或 osu! 时钟。宿主提供谱面毫秒和输入，`O2JamJudgementBridge` 将它们转成核心所需位置并把最终判定映射为原生结果。Drawable 可以承载原生回调与延后父长条提交，但不能重新定义判定或分数；画面只消费已解析状态。内部 Combo 可为 -1，宿主显示和成绩分别投影。

### 长条的判定、提交与视觉边界

| 责任 | 所有者 | 当前接入点 |
|---|---|---|
| 头判/尾判、可开始/释放、拒绝头判后的尾 MISS | `O2Jam.Core` | `O2JamHoldJudgementEngine`、`O2JamHoldState`；`O2JamJudgementBridge` 转换原生结果 |
| 按键回调与结果提交 | `Host/Gameplay/Objects` | `O2JamDrawableHoldNote` 调用原生 Head/Tail/Body 的 `ApplyResult` 路径，父体结果留到 `CheckForResult`；若在释放回调立刻提交父体，Mania 对象池可能在同一帧移除仍被 `Update` 读取的 Head/Tail |
| 裁剪、色调、尾端与头部保留 | `Host/Gameplay/Objects` 内的原生 Drawable 适配 | `Update` 只读取已解析的 `O2JamHoldState` 和原生显示状态；`UpdateHitStateTransforms` 延长视觉寿命，不延后判定或计分。直接改动 Mania 的私有裁剪容器仍需在 Drawable 完成 |
| Legacy 皮肤 MISS 色调适配 | `Presentation/GameplayFeedback/Skinning` | 继续使用原生 Mania 皮肤组件，只消除内部独立灰化，由父长条统一决定开关效果。对象池预加载时 `HitObject` 尚未赋值，因此按 O2Jam 绘制对象类型取得绑定副本；身体皮肤的依赖对象为父长条，不是逻辑 Body。副本随皮肤释放，不能解绑原始长条状态。原生 Mania/MS 绘制对象不应用此色调适配 |

新增长条规则时先改 Core；仅当需要把规则结果提交给 osu! 时改桥接/Drawable。新增长条外观时只消费状态，不能在绘制路径调用判定。回放倒退必须恢复原生结果与 Core 历史，对象池复用后也要复测；现有过滤测试覆盖这些路径。

长条视觉开关读取当前游玩依赖中的 `O2JamRulesetConfigManager` 绑定值，进程级投影只作未注入配置时的回退。原生依赖容器缓存具体配置类型；不能以接口缓存的测试替代真实宿主注入。皮肤回归同时检查父对象和内部原生组件的实际颜色，避免父对象正常掩盖内部灰化。

谱面运行链为 `OjnReader` → `OjnDocument` → `OjnBeatmapFactory` → `O2JamBeatmap` → 原生可玩对象。`OjnDocument` 已是独立格式结果，不再复制第二套音符/时间中间模型。`O2JamExternalChartResources` 管理外部 OJN/OJM 的懒读取、归档文件戳和可复用性；`O2JamWorkingBeatmap` 保留原生 Beatmap、Skin、Track 绑定及转移，封面由内部原生工作谱面提供。缓存位于 Integration。解码结果的集合与索引在构造时冻结，封面及公开音频字节返回独立副本；BGM 通过不暴露底层数组的只读流读取，因此共享缓存可安全供多个只读消费者使用。导入则由 `Host/Library` 计划与批处理，经 `IO2JamLibraryWriter` 进入当前 Realm 后端；UI 设置页只提交操作并显示状态。

普通 O2Jam 分数从 Core 历史构造；Mania Score 在转换谱面后交给原生 Mania 规则与计分适配。Mod 属性、玩法选择和预览/游玩音频均由宿主处理，不能让 Core 了解 MS。PP 资格策略为“选中 MS 且所有展开的 Mod 均原生 Ranked”；计算入口、资格判定、标签展示分属不同模块。

### 玩法路线与 Mod 应用阶段

`O2JamScoreProcessor` 位于 `Host/Gameplay/Scoring`，协调两条计分路线并承接原生 `ApplyResult`、回退、Combo 和成绩填充。Integration 的判定桥接只通过 `IO2JamJudgementResolver` 请求最终准确度，不读取 MS Mod 或依赖具体宿主计分器；MS 的解析入口不改变 O2Jam 核心状态。`ManiaScoreProcessorAdapter` 只公开受保护的原生 Mania 计分钩子，不复制其公式。已有类的 namespace/程序集身份保持不变。

| 阶段或功能 | 所有者与原生入口 | 保留适配的原因 |
|---|---|---|
| MS 选择、依赖 Mod | `ManiaScore/Mods`；原生 Mod 选择转换后做依赖补全 | EZ/HR/Classic 需要同时选中 MS；仅有依赖 Mod 不隐式切换计分路线 |
| MS 谱面替换 | `O2JamModManiaScore` → `O2JamManiaScoreBeatmapAdapter`；`IApplicableAfterBeatmapConversion` | 在 `ApplyDefaults` 前生成原生 Mania 对象，OD/HP 基线先于普通难度 Mod 应用 |
| Invert | MS 已转换为原生对象时直接调用原生 ManiaModInvert；O2Jam 对象使用本地适配 | 非 MS 路线必须补充头尾小节位置，保留 KS 起点映射；两种 MS/Invert 顺序与原生结果对照 |
| O2Jam No Release | `Host/Mods`；转换后写入 ReleaseTimingDisabled，游玩由 Core 判定 | 原生 No Release 会替换 O2Jam 长条和小节判定元数据，不能直接调用其谱面替换 |
| MS No Release | `Host/Mods`；`IApplicableToDrawableRuleset<ManiaHitObject>` 注册自动尾判池 | 原生入口强转 `DrawableManiaRuleset` 且尾对象/绘制实现为私有类型；保留最小尾判适配，O2Jam 精确类型池不受影响 |
| Constant Speed、Cover/Hidden/FadeIn | `Host/Mods`；原生滚动接口及遮罩组件 | 避开原生 Mod 对具体 `DrawableManiaRuleset` 的强转，继续由原生组件绘制 |
| Perfect 设置 | `Host/Mods` 的 `SettingSource` 指向 `Presentation/AccessAndSettings` 控件 | 原生反射接口需要具体控件类型；这是声明式展示接入，玩法仍用原生 ManiaModPerfect 的失败条件 |

新增 Mod 应先确定它在哪个原生阶段生效，再核对是否改变对象类型、KS 映射、小节位置或计分路线。不要把原生类型强转失败作为重写整套玩法的理由。`check-scoring-boundary.ps1` 防止当前具体计分路线类型重新进入 Integration/Scoring；它是窄范围源码检查，完整跨层检查仍属于 A12。

设置页未选择导入路径时隐藏更新/清除按钮的整组，选择路径后由原生流式布局显示；操作是否可执行仍由曲库应用状态决定。设置会话初始化或同步收藏夹时暂时禁用按钮，不能绕过串行操作保护。

回放输入沿用原生 `FramedReplayInputHandler` 调度及 ManiaAction，宿主只定义 O2Jam 帧格式。当前归档仅写/读带 `o2lazer` 标记的 v5；重构前无标记 replay 属于测试实现，明确不兼容。旧成绩关联必须保留；任何重建谱面或存储设计都需先验证 ID 映射。Realm 直接类型集中在 `Host/Persistence/Realm`，但原生模型和两个 partial 组装点仍产生编译耦合；详见 [存储边界](realm-isolation.md) 和 [回放时序](replay-timing-boundary.md)。

音频使用 OJM 资源与原生 Track/Sample 后端。桥接层负责 OJN 到谱面音频事件和采样引用元数据的映射；`Host/Audio` 消费这些数据构造调度，并负责统一事件钟、预览/游玩播放策略、声道生命周期和限定 OJM 的原生采样适配。普通游玩与回放仍由原生命中触发 KS，BGM/KeySound 不受全局效果音量影响。暂停与跳转的边界见[音频契约](rate-mod-audio-readiness.md)；[音频诊断](audio-sync-diagnostics.md)仅用于观测。Native Mania 皮肤、对象池、UI 控件和动画在契约兼容时继续复用。

## 扩展时如何放置实现

| 新需求 | 首先修改 | 接入与验收 |
|---|---|---|
| O2Jam 判定或长条规则 | `O2Jam.Core` 及独立测试 | Integration 翻译结果；回放回退与对象池测试；表现不得写判定 |
| OJN/OJM 解码 | `O2Jam.Formats` 及格式夹具 | Integration 映射；真实样本有界验证；不把 OJN 通道类型加进 Core |
| 导入字段或存储 | `Host/Library` 契约与 `Host/Persistence/Realm` 实现 | 先定义身份/版本/旧成绩映射；临时库迁移与失败重试测试 |
| Mania Score 或 Mod | `ManiaScore`/`Host/Mods`，必要时 Integration 的玩法选择 | 原生可用性、转换、计分、回放与 MS 开关组合 |
| 等级、筛选、标签、设置或 HUD | 对应 `Presentation` 模块；查询/资格纯策略留 Integration | 比较原生扩展点；视觉、异步更新与本地化验收 |
| 音频/预览 | `Host/Audio` 和资源边界 | 保留原生时钟/轨道；测试暂停、寻址、变速、快速切曲及效果音量 |
| 补丁 | 服务功能的模块；登记在总安装器 | 证明原生缺口、限定 ruleset、验证目标签名及失败回滚、多 ruleset 载入 |
| 未来 BMS 等新 ruleset | 在第二消费者中验证可复用的宿主设施 | 各自格式结果接入原生 `WorkingBeatmap`；O2Jam 专用 Core/格式不强行公共化；共用补丁协调须处理版本和所有权 |

当前问题按 [A01–A16](architecture-audit.md#初始审查问题) 逐项解决；每项结束时更新路线图和必要测试。已有行为不能只因“旧代码”而删除：旧皮肤布局类型、规则集短名与键值、谱面/成绩身份及 v5 回放均涉及持久化契约。
