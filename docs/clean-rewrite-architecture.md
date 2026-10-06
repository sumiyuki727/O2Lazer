# O2Lazer 当前架构

维护状态（2026-10-06）：现行层级与扩展指引；显式用户政策优先；六条精确例外及两个 partial 仍存在。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-06。本文件描述当前 `master` 的责任边界和新增功能指引，不用历史目录名或某次测试数表示架构已全部完成。[全项目审查和待处理问题](architecture-audit.md)、[路线图](refactor-roadmap.md)、[玩法行为规格](o2jam-behaviour-spec.md)分别负责风险、进度和游戏规则。

## Design intent and decision precedence

1. 已明确的用户契约优先于参考行为；连续不对称窗口、判后 KS 禁止重触发与 EX 完整记录/锁血/F 是有意选择，不为复刻原版撤销。
2. 在上述选择之外，有可靠依据的 O2Jam 时间、判定、长条、分数、Combo、Jam、药丸、生命和 OJM 事件语义优先。
3. O2Jam 没定义、而 osu! 需要的概念，使用最接近的原生 Mania/lazer 契约表达，例如非 MS 准确率与字母评级。
4. 无一致 O2Jam 权威的应用生命周期沿用 lazer，例如暂停与恢复。
5. 在满足需求时先使用原生实现。只有原生接口无法给出需要的行为或表现，才加局部适配/补丁；记录具体缺口、作用范围、失败影响和升级验证方式。尽量继续让原生控件执行动画、布局、输入和结果生命周期。

忠于原生体验不等于复制原生模式的每个内部中间值。跨模式进入 O2Lazer 时，目标 OJN 往往尚未选定；不能为即时变色猜测最终等级。当前等级色将 Lv/10 投影到原生 `StarRatingDisplay`，MS 使用 Mania 星数；两个模式均过滤不影响颜色的 `MaxCombo` 元数据更新，避免重启原生过渡。标题 SR 待计算时保留先前有效星数或空白，O2MA/LV 仍按原生行入场。

## 构建与依赖方向

2026-10-06 无 Mod 基线计算使用 Integration 的独立原生 Mania 投影，Host 管理一次性输入并调用原生难度算法；不为星级构造 gameplay 的音频/绘制/原始位置数据。Core/Formats 保持原职责，具体存储和事务不进入投影。Mod 计算仍走原游玩预处理与复制管线，详见[专用投影](library-persistence-contract.md#原生难度专用投影)。

```text
O2Jam.Formats ──> 纯解码结果
O2Jam.Core    ──> 纯玩法规则/状态
         \       /
       Integration ──> osu! 谱面、对象、判定翻译契约
             ↑
       Host / ManiaScore ──> 原生生命周期、音频、Mods、缓存、查询与资格
             ↑
        Presentation ──> 原生控件、等级/颜色、曲库、HUD/皮肤

Composition 组装各模块；Persistence 接入具体存储后端
```

图示表达数据使用关系，实际 C# 引用遵循下方的允许依赖矩阵。除两个独立项目外，其余区域编译在同一 ruleset 项目；语义检查约束源代码依赖，不产生程序集隔离。根目录的 `O2LazerRuleset` 和 `Composition/O2JamCompatibilityPatches` 是跨模块组装入口；`Host/Compatibility` 只承接原生兼容和 BMS 共存适配。

| 目录 | 应有责任 | 当前代表入口 |
|---|---|---|
| `O2Jam.Core/` | 音乐位置、BPM、判定窗口、长条状态、整局分数/Combo/Jam/药丸/生命；只依赖 .NET | `O2JamTimingMap`、`O2JamJudgementEngine`、`O2JamGameplayState` |
| `O2Jam.Formats/` | OJN/OJM/OMC/M30 字节到格式数据；不写数据库、不创建 osu 对象 | `OjnReader`、`OjmReader` |
| `Integration/Identity`、`Integration/Formats`、`Integration/Beatmaps` | ruleset 短名/键位契约、格式到可游玩谱面、采样引用与外部资源快照 | `O2LazerIdentity`、`OjnBeatmapFactory`、`O2JamExternalChartResources` |
| `Integration/Objects`、`Integration/Scoring` | 时间和判定转译、核心结果与原生结果关联；向宿主提供判定解析契约 | `O2JamJudgementBridge`、`IO2JamJudgementResolver`、`O2JamJudgementHistory` |
| `Host/Beatmaps` | 原生工作谱面、Skin/Track 构造、缓存与资源转移生命周期 | `O2JamWorkingBeatmap`、`O2JamWorkingBeatmapCache`、`O2JamWorkingBeatmapHook` |
| `Host/Library/Filtering`、`Host/Performance/Eligibility` | 原生曲库查询入口和 O2Jam PP 资格，读取难度/Mod 策略，不决定绘制 | `O2JamFilterCriteria`、`O2JamPerformanceEligibility` |
| `Host/Library`、`Host/Configuration` | 源快照、导入投影/版本与原生元数据载体、写入契约、操作会话、配置 | `O2JamSourceSnapshot`、`O2JamImportMetadata`、`O2JamImportService`、`IO2JamLibraryWriter`、`O2JamLibrarySettingsSession` |
| `Host/Audio`、`Host/Gameplay`、`Host/Replays`、`Host/Mods`、`Host/Difficulty` | 原生轨道、Drawable/对象池、计分协调、输入回放、Mod、难度缓存与计算 | `O2JamPreviewTrack`、`O2JamDrawableRuleset`、`O2JamScoreProcessor`、`O2JamDifficultyCalculator` |
| `Host/Persistence/Realm` | 当前 Realm 实现及原生存储接入 | `O2JamLibraryWriter`、`O2JamReplayPersistencePatch` |
| `ManiaScore/` | 与 O2Jam 核心并列的 Mania 玩法路线及其展示联动 | `O2JamManiaScoreBeatmapAdapter`、`ManiaScoreProcessorAdapter` |
| `Presentation/` | 等级/星数、难度颜色、曲库组织、资格标签、设置界面、皮肤和 HUD 展示 | `DifficultyLevels`、`LibraryBrowsing`、`Performance`、`GameplayFeedback` |
| `Composition/` 与根 ruleset 入口 | 组装服务、注册和安装各功能补丁 | `O2JamCompatibilityPatches`、`O2LazerRuleset` |
| `Resources/` | 本地化、音效、图标 | `Localisation/O2LazerStrings*.resx` |

`Host/Localisation` 是服务 UI 的本地化设施，不拥有玩法规则。补丁仍归服务的功能模块，总安装器只负责组装和失败策略。

刷新先发布可玩谱面，已提交批次可同时计算星级；全部投影提交后同步收藏夹，再补齐剩余星级。导入规划器不执行 Mania 算法；Host/Difficulty 的处理器及单线程有界流水线使用无 Realm 的存储契约，逐源计算原生 Mania 属性。可选批次契约只传普通身份、数值属性和逐源结果，最多十六源共享缓存读取/提交；具体模型只在 Realm 适配器的原生调用内存在，不进入队列或处理器。每源身份验证失败独立拒绝，原生事务异常/取消整批回滚，通知只在提交后发布。UI 用原生 Transform 平滑已确认计数，不影响事务和计算。投影与星数缓存各自判断完成，中断星数补算不触发完整谱面重写，详见[流水线契约](library-persistence-contract.md#准备星级与进度流水线)和[缓存批次](library-persistence-contract.md#星级缓存批次与分项计时)。

A04-1/2 的导入计划使用字节内容、全部三槽位身份/可玩性、原 Lv、源时间及独立缓存版本，不包含 Realm 对象或第二份音符模型。Host/Library 负责身份公式、源快照与缺失证明，并编解码原生 Tags 载体；该载体属于宿主桥，不是独立格式或未来数据库 schema。Realm 适配器共用新增/刷新赋值，三难度迁移及唯一正式 v5 匹配留在此处；明确集合迁移和对象移除复用原生 TransferCollectionReferences/模型清理，继续复用 CopyTo、DeepClone、RealmFileStore。已知身份/旧成绩及正式 v5 不因元数据版本变化重生成；A04-3 的源内容归组、稳定路径与通知/取消结果归 Host/Library，事务内原生所有者查询留在适配器。原生缓存通知独立重试，不引入永久 outbox。A04-4 的文件预留/模型两事务及故障恢复留在 Realm 适配器，Host/Library 仅传递取消令牌；SHA-256、文件校验/安全写入和零引用清理继续复用原生。A04-5 实机验收仍按[存储矩阵](library-persistence-contract.md#实施顺序与验收矩阵)处理。

批次查询和写入计时也留在存储适配器：当前使用原生 Ruleset 对象链接限定活动 beatmap，路径只查询单字段 AudioFile，旧 Path 回退与 set Hash 检查分开；内容复用原生文件主键和反向关联，最终核对完整路径、归属及歧义。新旧候选都要收集，已知源 ID 也不能覆盖当前的不同路径所有者。候选模型只活在本次事务内，同批修改必须即时可见，不将模型或 ORM 查询泄漏给 Library。Library 仍管理每批最多十六源的准备/提交编排；`lookup_mode=native-link-v1` 只标识日志口径，不成为新存储协议。此前平面父 Files 查询和拆分 set 实验均因实机回退撤回；新设计的真实只读备份比较已核对结果一致，查询中位数减少约 50–54%，完整客户端刷新仍待验收。原生 API 与分层正确不能替代性能证据，见[查询契约](library-persistence-contract.md#批次查询与写入计时)及[重设计验证](library-persistence-contract.md#原生链接查询重设计)。

### 允许依赖矩阵与例外

现有投影的基线按需/启动计算通过原生工作谱面的文件流读取已提交 OJN，避免玩法解码带来的 OJM/皮肤/音轨工作。Host/Difficulty 只发布一份线程内普通结果；Realm 层的可选 `O2JamNativeDifficultyPersistencePatch` 在原生星数事务核对内容身份并保存已有完整属性。后置处理重查当前缓存，提交时保留并行完成的槽位。Host 不调用 Persistence，Realm 模型与事务不进入结果契约；该设计仍使用原生宿主模型，不能视为存储后端已可直接替换。细节和原生缺口见[补算协调](library-persistence-contract.md#原生后台与后置补算协调)。

`scripts/architecture-policy.json` 是可执行规则；下表只列本仓库代码依赖，同层引用允许。Persistence 对应 `Host/Persistence`，不按历史 namespace 判断归属。

| 调用方 | 可引用的其他层 |
|---|---|
| Core | 无 |
| Formats | 无 |
| Integration | Core、Formats |
| Host | Core、Formats、Integration、ManiaScore |
| ManiaScore | Core、Formats、Integration、Host |
| Persistence | Core、Formats、Integration、Host、ManiaScore |
| Presentation | Core、Formats、Integration、Host、ManiaScore |
| Composition | 所有层 |

Integration 可创建原生谱面/判定对象，但不拥有工作谱面的 Track/Skin 生命周期，也不选择 MS 玩法或读取宿主难度缓存。曲库查询和资格入口使用原生 `IRulesetFilterCriteria`、`ModUtils`、`Mod.Ranked`，自身包含 O2Jam 元数据/MS 选择语义，故归 Host；它们不因返回数值或布尔值就变为纯接入契约。需要第二 ruleset 时先验证真实复用点，避免提前增加一套查询或资格框架。

当前保留六条精确例外，匹配调用文件、目标文件和类型，不能通配整个目录：

| 调用方 → 目标 | 原因 |
|---|---|
| `Host/Difficulty/Data/O2JamStarRatingMetadata` → `Presentation/DifficultyLevels/Policy/O2JamDifficultyRating` | 旧难度名/标签读取共用等级解析与 Lv/10 政策；目标不持有 UI 状态，避免复制公式。 |
| `Host/Mods/O2JamModRandom` → `Presentation/AccessAndSettings/O2JamRandomAlgorithmDropdown` | 原生 `SettingSource` 要求具体控件类型；原生枚举不能隐藏退役算法，子类只筛选菜单并保留已选旧值。 |
| `Host/Mods/O2JamModPerfect` → `Presentation/AccessAndSettings/O2JamPerfectHitSettingsCheckbox` | 原生 `SettingSource` 需要具体设置控件类型；失败规则继续使用原生 Perfect。 |
| `ManiaScore/Mods/O2JamModManiaScore` → `Presentation/AccessAndSettings/Icons/O2JamModIcons` | 原生 `Mod.Icon` 声明图标元数据，注册和绘制仍由原生 FontStore/ModIcon 执行。 |
| `Persistence/O2JamSettingsSubsection.Realm` → `Presentation/O2JamSettingsSubsection` | 原生依赖注入以同名 partial 组装具体后端，再交给无 Realm 的设置会话；存储重设计由 A04 处理。 |
| `Persistence/O2JamSongSelectRankPatch` → `Presentation/O2JamSongSelectRankPatch` | Realm 集合通知调用当前难度成绩选择策略；存储重设计由 A04 处理。 |

最后两项同时登记为跨层 partial 例外，限定准确的两个声明文件；新增第三个声明必须重新审查。例外消失也会让检查失败，要求删除过期许可。它们是仍存在的编译耦合，不意味着存储可直接替换。

`scripts/check-architecture.ps1` 从实际 MSBuild Compile/ReferencePath 获取源码和匹配宿主引用，通过 SDK 自带 Roslyn 解析类型及成员的真实声明位置；覆盖别名、全限定名称、推断类型和 partial 成员，并分别分析生产及包含音频诊断的配置。未分类源码、解析错误、未批准依赖或过期例外均使检查失败，报告写入 `.artifacts/architecture/dependencies.json`。工具不进入 ruleset DLL。矩阵约束跨层源码引用，同层功能之间的依赖及读写语义仍需代码审查；反射字符串、私有 IL 目标、外部原生库的传递依赖和运行期回调不属于这份静态报告，继续依靠补丁清单、定向测试和 A11 实机验收。

### 命名空间与持久化身份

独立模块的项目名、根命名空间和公开 API 一致：`O2Jam.Core` 只提供玩法规则，`O2Jam.Formats.Ojn` / `O2Jam.Formats.Ojm` 只提供解码结果。它们不引用宿主、存储或彼此。接入其他游戏时直接引用这两个项目；宿主构建仍将其合入单 DLL，ILRepack 保留这些 API 的可见性。源码使用方需更新 `using`，本次不保留误导边界的旧 `.Core` / `.Formats` 别名。

| 实现 | 命名空间 | 归属理由 |
|---|---|---|
| 独立判定与状态 | `O2Jam.Core` | 不含 osu! 语义 |
| 独立 OJN / OJM 解码 | `O2Jam.Formats.Ojn` / `.Ojm` | 不创建原生谱面或管理宿主缓存 |
| 工厂、格式映射、编码目录提示及外部资源缓存 | `osu.Game.Rulesets.O2Lazer.Integration.Formats.Ojn` / `.Ojm` | 将独立格式接入当前玩法、曲库与资源生命周期 |
| 谱面 Hash 生成 | `osu.Game.Rulesets.O2Lazer.Host.Library` | 是导入/成绩身份策略，不属于玩法内核 |
| Lv/10 换算和旧难度名解析 | `osu.Game.Rulesets.O2Lazer.Presentation.DifficultyLevels.Policy` | 是等级体验与宿主兼容策略，不改变判定 |

新增宿主类型原则上使用 `osu.Game.Rulesets.O2Lazer.<层>.<功能>`，与责任目录对应。同一 partial 类型的声明必须共用命名空间，例如目前设置页与 Realm 组装。既有 `.Beatmaps`、`.Objects`、`.Scoring`、`.Mods`、`.UI`、`.SongSelect` 等功能命名空间保留以控制公开 API 和反射改动范围；它们不是层级标识，不能凭 `using` 判定依赖方向，也不声称这些名称全部为持久化所必需。A12 检查按真实声明文件判断归属，新增代码必须遵守矩阵，不能靠共用 namespace 隐藏越层调用。

| 兼容契约 | 必须保留的身份 | 依据与验证 |
|---|---|---|
| ruleset 注册与旧成绩 | `osu.Game.Rulesets.O2Lazer.O2LazerRuleset`、程序集名、`o2lazer` 短名 | 原生 `RulesetInfo.InstantiationInfo` 保存实例化身份；身份与临时旧库迁移测试 |
| 旧 HUD 及现有可编辑组件 | `.UI.HudComponents.O2LazerComboCounter`、`.O2LazerJudgementDisplay`，以及 `.Skinning.Components.O2JamComboCounter` | 原生 `SerialisedDrawableInfo.Type` 写入具体类型；旧布局类型解析与组件枚举测试。表中的省略前缀均为 `osu.Game.Rulesets.O2Lazer` |
| Mod 与设置 | Mod 缩写、设置属性键、配置项、输入动作数值和键位 variant | 原生 `APIMod` 保存缩写与设置键，不靠 Mod 完整类型名；设置和回放回归 |
| 正式 v5 回放与谱面关联 | `o2lazer` 标记、版本、帧/成绩字段、Beatmap Hash/MD5 | 回放写入固定数据 DTO，不写 Core/Formats 类型名；导入与旧成绩关联回归 |

`check-module-boundary.ps1` 限定独立模块的命名空间与项目直接引用，并禁止宿主源码冒用独立模块命名空间。独立测试检查实际程序集引用与公开 API；宿主测试检查单 DLL 合并后的可见性和引用身份。这些检查不替代完整宿主依赖矩阵，也不等于客户端全流程验收。

## 玩法与原生宿主的接缝

Core 接收小节位置与普通规则配置，输出原始/药丸修正后的判定、长条状态和整局快照；它不认识 `HitObject`、Drawable、Mod、Realm 或 osu! 时钟。宿主提供谱面毫秒和输入，`O2JamJudgementBridge` 将它们转成核心所需位置并把最终判定映射为原生结果。Drawable 可以承载原生回调与延后父长条提交，但不能重新定义判定或分数；画面只消费已解析状态。内部 Combo 可为 -1，宿主显示和成绩分别投影。

### 零血状态与成绩结果边界

Core 的 `LifeLockedAtZero` 表达本局血量不能恢复，`HasFailed` 只表达是否结束播放。
无 NF 的 EX 归零后继续记录全部玩法状态，Host 不触发原生失败退场；PopulateScore
将原生结果写为 F/未通过，实时原生评级不永久锁 F，以支持回放倒退。
NX/HX、NF 和 MS 仍分别走既定停止、恢复与原生 Mania 路线。正式 v5/Realm 使用
已有 Rank 表达历史结果；Passed 为瞬时字段，读取时恢复。未来个人记录的过滤归
Host/存档投影，不进入 Core，不通过删除 F 原始成绩实现。详见[行为规范](o2jam-behaviour-spec.md#life)。

### 误差条的坐标与统计边界

`Integration/Scoring/O2JamHitErrorProjection` 从原生命中时间和物件小节位置取得
积分 tick 误差；它不依赖 Host、MS、HUD 或皮肤。`Presentation/GameplayFeedback`
持有固定显示窗口与原生组件适配，复用 `ManiaHitWindows` 的 OD7、1× Great
色区半宽作为早侧 COOL 基准，早/晚窗口为 6/7、18/19、25/26 tick。
Legacy 调整已有色块位置和宽度；Argon 固有轴长按同一 tick 刻度延长，并分别设定
早晚已有色块的长度，其他动画和 HUD 设置仍由原生处理。
不能把显示窗口赋给谱面物件，不能改全局 TimeOffset 或游戏时钟来让条形稳定；
不对称判定窗口仅由 Core 定义，Integration 把最大早/晚端点换算为原生毫秒生命周期
包络；显示层不重复判定。MS 保持原生表，UR 数字仍是原生毫秒统计。
BAD 在 Integration 统一映射为 Meh，显示层直接使用原生黄色，无取色补丁。临时误差条
监听已在用户验收后移除；历史证据和当前状态见[显示验收](hit-error-display-validation.md)。
具体原生缺口、限定范围和升级检查见[补丁清单](compatibility-patches.md#tick-误差条适配2026-10-05)。

### 长条的判定、提交与视觉边界

| 责任 | 所有者 | 当前接入点 |
|---|---|---|
| 头判/尾判、可开始/释放、拒绝头判后的尾 MISS | `O2Jam.Core` | `O2JamHoldJudgementEngine`、`O2JamHoldState`；`O2JamJudgementBridge` 转换原生结果 |
| 按键回调与结果提交 | `Host/Gameplay/Objects` | `O2JamDrawableHoldNote` 调用原生 Head/Tail/Body 的 `ApplyResult` 路径，父体结果留到 `CheckForResult`；若在释放回调立刻提交父体，Mania 对象池可能在同一帧移除仍被 `Update` 读取的 Head/Tail |
| 裁剪、色调、尾端与头部保留 | `Host/Gameplay/Objects` 内的原生 Drawable 适配 | `Update` 只读取已解析的 `O2JamHoldState` 和原生显示状态；`UpdateHitStateTransforms` 延长视觉寿命，不延后判定或计分。直接改动 Mania 的私有裁剪容器仍需在 Drawable 完成 |
| Legacy 皮肤 MISS 色调适配 | `Presentation/GameplayFeedback/Skinning` | 继续使用原生 Mania 皮肤组件，只消除内部独立灰化，由父长条统一决定开关效果。对象池预加载时 `HitObject` 尚未赋值，因此按 O2Jam 绘制对象类型取得绑定副本；身体皮肤的依赖对象为父长条，不是逻辑 Body。副本随皮肤释放，不能解绑原始长条状态。原生 Mania/MS 绘制对象不应用此色调适配 |

新增长条规则时先改 Core；仅当需要把规则结果提交给 osu! 时改桥接/Drawable。新增长条外观时只消费状态，不能在绘制路径调用判定。回放倒退必须恢复原生结果与 Core 历史，对象池复用后也要复测；现有过滤测试覆盖这些路径。

长条视觉开关读取当前游玩依赖中的 `O2JamRulesetConfigManager` 绑定值，进程级投影只作未注入配置时的回退。原生依赖容器缓存具体配置类型；不能以接口缓存的测试替代真实宿主注入。皮肤回归同时检查父对象和内部原生组件的实际颜色，避免父对象正常掩盖内部灰化。

谱面运行链为 `OjnReader` → `OjnDocument` → `OjnBeatmapFactory` → `O2JamBeatmap` → 原生可玩对象。`OjnDocument` 已是独立格式结果，不再复制第二套音符/时间中间模型。Integration 的 `O2JamExternalChartResources` 管理外部 OJN/OJM 懒读取、文件戳和格式缓存；`Host/Beatmaps/O2JamWorkingBeatmap` 保留原生 Beatmap、Skin、Track 绑定及转移，封面由内部原生工作谱面提供，工作谱面缓存/替换钩子同归 Host。解码结果的集合与索引在构造时冻结，封面及公开音频字节返回独立副本；BGM 通过不暴露底层数组的只读流读取，因此共享缓存可安全供多个只读消费者使用。导入则由 `Host/Library` 计划与批处理，经 `IO2JamLibraryWriter` 进入当前 Realm 后端；UI 设置页只提交操作并显示状态。

活动存储 OJN/封面的可用性检查、内部深度校验和原生 Add 修复属于 `Host/Persistence/Realm`；普通更新不提前读取全部存储副本。实际写入的可选校验观察器也留在该层，只复用原生修复结果通知缓存，不改变校验/安全写入。Host/Library 只消费 `CanReuseFiles` 跳过条件，不将它当成完整性证明，也不知道具体存储路径或文件行；文件修复不影响内容身份和有效 Mania 缓存。普通更新仍读取每源一次内容指纹，跳过判断复用该证明；Repair 仅是内部维护模式，没有新增 UI 按键。完整范围见[刷新分流](library-persistence-contract.md#普通刷新迁移与内部修复)。

2026-10-02 刷新准备阶段使用 Host/Library 的阶段与计数契约，Host/Configuration 负责本地化和原生进度通知，Realm 适配器只发布扫描结果。普通更新只检查存储可用性；内部 Repair 才提前核验存储字节，实际写入继续由原生 Add 校验和修复。该优化不改变身份或 Mania 缓存，也不新增持久化版本或 UI 补丁。

同轮源指纹及路径匹配归 Host/Library，复用成功扫描结果，未扫描的登记路径继续验证；准备/提交前的变化拒绝仍保留。该索引只保留指纹，不引入第二份谱面模型或整库字节缓存。读取、匹配、更新的进度分开报告，文件字节校验、模型事务及缓存通知仍各归原有组件。

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
| Random 设置 | `Host/Mods` 的 `SettingSource` 指向原生枚举下拉的子类 | 原生枚举不支持隐藏退役值；展示层只筛选菜单并保留已选旧值，算法位于 Integration |
| Perfect 设置 | `Host/Mods` 的 `SettingSource` 指向 `Presentation/AccessAndSettings` 控件 | 原生反射接口需要具体控件类型；这是声明式展示接入，玩法仍用原生 ManiaModPerfect 的失败条件 |

新增 Mod 应先确定它在哪个原生阶段生效，再核对是否改变对象类型、KS 映射、小节位置或计分路线。不要把原生类型强转失败作为重写整套玩法的理由。`check-scoring-boundary.ps1` 检查当前具体计分接缝，`check-architecture.ps1` 同时限制整个 Integration 对 Host/MS 的引用。

### 谱面变换与数据所有权

2026-10-05 新增的随机是具体消费者：`Integration/Beatmaps/Transforms/O2JamColumnRandomizer`
只接受纯位置/列数据，保留原版种子和保护规则；`O2JamRandomBeatmapTransform` 适配工作谱面，
`Host/Mods/O2JamModRandom` 使用原生下拉和 Seed。Formats 提供原始包位置，Integration
容器保留源位置侧信息并在 MS 对象替换时迁移引用。没有恢复通用变换接口，也没有新增
判定 Core 或 Drawable 职责。默认原生随机满足旧布局契约；原生洗牌序列和整局映射无法
表达原版 CRT 序列及 Panic 小节保护，故保留具体纯算法。R/S 的轮转、逐音符分配、40 ms
保护由不依赖 osu 类型的 `ColumnRandomizer` 承载，可供其他 ruleset 复用。菜单正常显示
Random/R-Random/S-Random（原 Panic）；旧逐音符 S-Random 隐藏但保留身份，退役 O2Jam 普通随机仅作旧值兼容；原生枚举不能筛选
旧值，故使用原生子类及具体 SettingControlType 边界例外。没有新增 Harmony 或存储字段。
持久化身份、原生缺口及验收边界见[随机契约](column-randomisation.md)。


格式层的 `OjnDocument` 是不可变的共享解码结果；`OjnBeatmapFactory` 将其映射到缓存源谱面。每次原生 `WorkingBeatmap.GetPlayableBeatmap` 经 `O2JamBeatmapConverter` 创建独立的可玩对象和集合，再按原生 Mod 阶段应用修改。不要把缓存源谱面的可变对象交给 Mod，也不要为了 Mod 复制另一套格式或核心谱面模型。原生转换器只复制谱面容器，O2Jam 转换器因此仍需复制音符、长条及采样列表，保留小节位置和 KS 映射。

Mirror 和 Random 默认算法直接使用原生 Mania 的 `IApplicableToBeatmap` 实现；其他随机选项仍沿用该原生阶段；MS 对象替换及 O2Jam Invert/No Release 使用 `IApplicableAfterBeatmapConversion`，在对象默认值和嵌套对象建立前接入。这些是宿主阶段，不归 Core 或 Formats；判定和 Drawable 不应再执行谱面变换。

A15 已删除未使用的 `IO2JamChartTransform<TChart>`，不增加替代接口或第二条调度管线。仓库、只读参考源码及当前已安装规则集未发现使用者；该类型原先公开，但未参与已有存储、皮肤或 replay 身份。无法据此保证未知第三方二进制没有引用它，若有此类调用方需重新适配。未来确实出现独立游戏或第二 ruleset 的变换需求时，先确认输入输出、所有权、应用顺序及随机种子/replay 契约，再从真实消费者提取最小接口；仅有未来扩展设想不足以保留抽象。

设置页未选择导入路径时隐藏更新/清除按钮的整组，选择路径后由原生流式布局显示；操作是否可执行仍由曲库应用状态决定。设置会话初始化或同步收藏夹时暂时禁用按钮，不能绕过串行操作保护。

回放输入沿用原生 `FramedReplayInputHandler` 调度及 ManiaAction，宿主只定义 O2Jam 帧格式。当前归档仅写/读带 `o2lazer` 标记的 v5；新写入携带 `judgement_version=20261005`，已有无规则版本 v5 测试回放按新规则重判，保存成绩不改写；重构前无标记 replay 属于测试实现，明确不兼容。旧成绩关联必须保留；任何重建谱面或存储设计都需先验证 ID 映射。Realm 直接类型集中在 `Host/Persistence/Realm`，但原生模型和两个 partial 组装点仍产生编译耦合；详见 [存储边界](realm-isolation.md) 和 [回放时序](replay-timing-boundary.md)。

音频使用 OJM 资源与原生 Track/Sample 后端。桥接层负责 OJN 到谱面音频事件和采样引用元数据的映射；`Host/Audio` 消费这些数据构造调度，并负责统一事件钟、预览/游玩播放策略、声道生命周期和限定 OJM 的原生采样适配。普通游玩与回放仍由原生命中触发 KS，BGM/KeySound 不受全局效果音量影响。暂停与跳转的边界见[音频契约](rate-mod-audio-readiness.md)；[音频诊断](audio-sync-diagnostics.md)仅用于观测。Native Mania 皮肤、对象池、UI 控件和动画在契约兼容时继续复用。

## 扩展时如何放置实现

原版对齐阶段已于 2026-10-06 按用户决定结束，其余差异暂缓；下述是未来新增需求的
归属指引，不是当前待实现列表。收尾审查与已知边界见[本轮复核](recent-changes-review.md)。

| 新需求 | 首先修改 | 接入与验收 |
|---|---|---|
| O2Jam 判定或长条规则 | `O2Jam.Core` 及独立测试 | Integration 翻译结果；回放回退与对象池测试；表现不得写判定 |
| OJN/OJM 解码 | `O2Jam.Formats` 及格式夹具 | Integration 映射；真实样本有界验证；不把 OJN 通道类型加进 Core |
| 导入字段或存储 | `Host/Library` 契约与 `Host/Persistence/Realm` 实现 | 先定义身份/版本/旧成绩映射；临时库迁移与失败重试测试 |
| Mania Score 或 Mod | `ManiaScore`/`Host/Mods`，必要时 Integration 的玩法选择 | 原生可用性、转换、计分、回放与 MS 开关组合 |
| 等级、筛选、标签、设置或 HUD | 对应 `Presentation` 模块；原生查询/资格入口归 Host | 比较原生扩展点；视觉、异步更新与本地化验收 |
| 音频/预览 | `Host/Audio` 和资源边界 | 保留原生时钟/轨道；测试暂停、寻址、变速、快速切曲及效果音量 |
| 补丁 | 服务功能的模块；登记在总安装器 | 证明原生缺口、限定 ruleset、验证目标签名及失败回滚、多 ruleset 载入 |
| 未来 BMS 等新 ruleset | 在第二消费者中验证可复用的宿主设施 | 各自格式结果接入原生 `WorkingBeatmap`；O2Jam 专用 Core/格式不强行公共化；共用补丁协调须处理版本和所有权 |

当前问题按 [A01–A16](architecture-audit.md#初始审查问题) 逐项解决；每项结束时更新路线图和必要测试。已有行为不能只因“旧代码”而删除：旧皮肤布局类型、规则集短名与键值、谱面/成绩身份及 v5 回放均涉及持久化契约。

新增源文件先选责任目录；若语义检查拒绝引用，先判断是否放错层、是否可直接使用原生入口或已有契约。只有具体原生接缝无法满足需求时才提出例外，并补充原因和验证依据；不能只扩大允许矩阵来通过检查。

旧 MD5 的收藏夹所有权判断属于 Realm 适配器，使用原生 Filter/Count 查询相关值并在变更前取得计数，复用 TransferCollectionReferences。事务路径/内容/set Hash 查找使用原生过滤查询、文件反向关联及主键，不逐批重建全库托管索引，也不保留跨事务 Realm 对象。外部所有者、大小写、待删除状态及批内新建/搬家后的冲突保护保持原有语义。
