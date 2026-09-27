# O2Lazer 当前架构

更新：2026-09-27。本文件描述当前 `master` 的责任边界和新增功能指引，不用历史目录名或某次测试数表示架构已全部完成。[全项目审查和待处理问题](architecture-audit.md)、[路线图](refactor-roadmap.md)、[玩法行为规格](o2jam-behaviour-spec.md)分别负责风险、进度和游戏规则。

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

箭头表达推荐的数据使用方向，不保证现有 C# 引用严格满足该图。除两个独立项目外，其余区域编译在同一 ruleset 项目；目前有 [A05–A07、A12](architecture-audit.md#初始审查问题) 的横向/反向依赖。`O2LazerRuleset` 是 ruleset 入口，`Host/Compatibility/O2JamCompatibilityPatches` 汇总补丁安装；目录移动本身不产生编译隔离。

| 目录 | 应有责任 | 当前代表入口 |
|---|---|---|
| `O2Jam.Core/` | 音乐位置、BPM、判定窗口、长条状态、整局分数/Combo/Jam/药丸/生命；只依赖 .NET | `O2JamTimingMap`、`O2JamJudgementEngine`、`O2JamGameplayState` |
| `O2Jam.Formats/` | OJN/OJM/OMC/M30 字节到格式数据；不写数据库、不创建 osu 对象 | `OjnReader`、`OjmReader` |
| `Integration/Formats`、`Integration/Beatmaps` | 格式数据到可游玩谱面、原生 WorkingBeatmap 与转换边界 | `OjnBeatmapFactory`、`O2JamWorkingBeatmap` |
| `Integration/Objects`、`Integration/Scoring` | 输入和时间转译、核心结果到原生 Judgement/ScoreProcessor；原生 `ApplyResult` 提交 | `O2JamJudgementBridge`、`O2JamScoreProcessor` |
| `Integration/Library/Filtering`、`Integration/Performance/Eligibility` | 曲库查询语义和 PP 资格策略，不决定绘制 | `O2JamFilterCriteria`、`O2JamPerformanceEligibility` |
| `Host/Library`、`Host/Configuration` | 导入应用流程、写入契约、操作会话、配置 | `O2JamImportService`、`IO2JamLibraryWriter`、`O2JamLibrarySettingsSession` |
| `Host/Audio`、`Host/Gameplay`、`Host/Replays`、`Host/Mods`、`Host/Difficulty` | 原生轨道、Drawable/对象池、输入回放、Mod、难度缓存与计算 | `O2JamPreviewTrack`、`O2JamDrawableRuleset`、`O2JamDifficultyCalculator` |
| `Host/Persistence/Realm` | 当前 Realm 实现及原生存储接入 | `O2JamLibraryWriter`、`O2JamReplayPersistencePatch` |
| `ManiaScore/` | 与 O2Jam 核心并列的 Mania 玩法路线及其展示联动 | `O2JamManiaScoreBeatmapAdapter`、`ManiaScoreProcessorAdapter` |
| `Presentation/` | 等级/星数、难度颜色、曲库组织、资格标签、设置界面、皮肤和 HUD 展示 | `DifficultyLevels`、`LibraryBrowsing`、`Performance`、`GameplayFeedback` |
| `Resources/` | 本地化、音效、图标 | `Localisation/O2LazerStrings*.resx` |

`Host/Localisation` 是服务 UI 的本地化设施，不拥有玩法规则。`Host/Compatibility` 是安装清单，不把所有补丁变为同一功能。旧 namespace 为持久化/反射兼容暂未全部迁移；判断边界以项目引用和真实调用为准。

## 玩法与原生宿主的接缝

Core 接收小节位置与普通规则配置，输出原始/药丸修正后的判定、长条状态和整局快照；它不认识 `HitObject`、Drawable、Mod、Realm 或 osu! 时钟。宿主提供谱面毫秒和输入，`O2JamJudgementBridge` 将它们转成核心所需位置并把最终判定映射为原生结果。Drawable 可以承载原生回调与延后父长条提交，但不能重新定义判定或分数；画面只消费已解析状态。内部 Combo 可为 -1，宿主显示和成绩分别投影。

谱面运行链为 `OjnReader` → `OjnDocument` → `OjnBeatmapFactory` → `O2JamBeatmap` → 原生可玩对象。`O2JamWorkingBeatmap` 负责外部 OJN/OJM 的宿主加载、封面、皮肤和音轨接入；缓存位于 Integration。解码结果集合和字节数组尚不是深度不可变，不能把共享对象交给可修改的消费者。导入则由 `Host/Library` 计划与批处理，经 `IO2JamLibraryWriter` 进入当前 Realm 后端；UI 设置页只提交操作并显示状态。

普通 O2Jam 分数从 Core 历史构造；Mania Score 在转换谱面后交给原生 Mania 规则与计分适配。Mod 属性、玩法选择和预览/游玩音频均由宿主处理，不能让 Core 了解 MS。PP 资格策略为“选中 MS 且所有展开的 Mod 均原生 Ranked”；计算入口、资格判定、标签展示分属不同模块。

回放输入沿用原生 `FramedReplayInputHandler` 调度及 ManiaAction，宿主只定义 O2Jam 帧格式。当前归档仅写/读带 `o2lazer` 标记的 v5；重构前无标记 replay 属于测试实现，明确不兼容。旧成绩关联必须保留；任何重建谱面或存储设计都需先验证 ID 映射。Realm 直接类型集中在 `Host/Persistence/Realm`，但原生模型和两个 partial 组装点仍产生编译耦合；详见 [存储边界](realm-isolation.md) 和 [回放时序](replay-timing-boundary.md)。

音频使用 OJM 资源与原生 Track/Sample 后端。桥接层负责 OJN 到谱面音频事件的映射；`Host/Audio` 从这些事件构造调度，并负责统一事件钟、预览/游玩播放策略、声道生命周期和限定 OJM 的原生采样适配。普通游玩与回放仍由原生命中触发 KS，BGM/KeySound 不受全局效果音量影响。暂停与跳转的边界见[音频契约](rate-mod-audio-readiness.md)；[音频诊断](audio-sync-diagnostics.md)仅用于观测。Native Mania 皮肤、对象池、UI 控件和动画在契约兼容时继续复用。

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
| 未来 BMS 等新 ruleset | 在第二消费者中验证可复用的宿主设施 | O2Jam 专用 Core/格式不强行公共化；共用补丁协调须处理版本和所有权 |

当前问题按 [A01–A16](architecture-audit.md#初始审查问题) 逐项解决；每项结束时更新路线图和必要测试。已有行为不能只因“旧代码”而删除：旧皮肤布局类型、规则集短名与键值、谱面/成绩身份及 v5 回放均涉及持久化契约。
