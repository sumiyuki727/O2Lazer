# 原版客户端功能与实现覆盖检查

维护状态（2026-10-06）：F01–F18 观察与范围表；已选方案结束；其余暂缓，归属建议不是当前开发队列。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-05。对照对象为用户指定的 `E:\Downloads\O2JamO2.zip` 与
`E:\Downloads\O2Jam-Classic.zip`。当前项目基线是 `master` 工作目录，包括 `be54260`
之后尚未提交的修改；不能只用该提交复现本文所述状态。初次检查仅整理文档；随后按
用户要求补齐 F05 的 3.5、4.5 显示档位，并重新构建，通过 19 项定向测试。

此前的[韩服客户端对照](korean-client-comparison.md)主要核对判定、计分与音频。
本文扩展到玩法路线、界面反馈、设置、曲库、教程、任务、结果与客户端外围功能。
当前判定、音频的准确契约仍以[行为规范](o2jam-behaviour-spec.md)为准。

2026-10-05 用户确认后续讨论范围：优先核对 F07 特殊随机与 F14 零血/失败。
音量曲线 C07、独立混音设置 F15 和专属成绩摘要 F16 暂缓；不把独立客户端的外围
功能或已明确选择的 lazer 接入行为纳入本次对齐。这段为当时的讨论范围。两项已完成所选方案；
2026-10-06 其余项目暂缓，不依据未确认行为继续修改规则。

## 证据范围

- 两份主程序的身份、哈希与时间字段沿用韩服对照报告。Classic 的 `OTwo.exe` 与
  `AngelO2Jam.exe` 是同一数据，不能当作两份独立证据。
- 本轮重新读取两份主程序的指令清单和 CP949 文本。Classic 主程序从安装包在内存中
  解压，核对柜目录长度及 MD5；没有运行或另行落盘其可执行文件。
- 读取已部署 O2JamO2 的 `Interface1.opi`、`Playing1.opi`，收集资源名称；同时对照
  当前 Formats、Core、Integration、Host、Presentation、ManiaScore 与只读原生源码。
- 指令调用和配置读写可以证明实现路径；资源或字符串只能证明程序包含对应内容。
  本轮没有启动客户端，不能据此认定动画、遮挡范围、每个模式的完整规则均已实测。
- 用户此前已确认 O2JamO2 可游玩及 KS 策略的听感。那次验收不覆盖本文新列出的功能；
  Classic 仍没有运行验收。独立本机服务端的奖励、任务或排名实现不是原官方服务器证据。

本轮派生材料在忽略目录 `.artifacts`：两份 `*-cp949-ui-strings.json`、
`Interface1-resource-names.json`、`Playing1-resource-names.json`。原版资产不进入交付 DLL。

## 覆盖清单

“明确缺口”表示当前没有该功能；不表示已经决定加入。“已有差异”表示存在同类能力，
但契约或表现不同。“待核对”不能升级为已确认 bug。“范围选择”需要先决定 custom
ruleset 是否承担该能力。

| 编号 | 功能 | 原版证据与当前关系 | 结论 |
|---|---|---|---|
| F01 | Jam、药丸及进度反馈 | 原版包含 Jam 数字、仪表和晋级特效。当前 Core 已有 Jam、连 COOL、药丸等快照，专用 HUD 和相应状态反馈尚未实现 | 明确缺口；不需重做内核 |
| F02 | 游玩中央的判定名称与皮肤 | 原版有 `Note_Cool`、`Note_Good`、`Note_Bad`、`Note_Miss`。当前统计名称为 O2Jam，但中央判定仍使用 Mania 组件和贴图映射 | 已有差异；默认文本与统计名称没有统一 |
| F03 | 音符外观与导引选项 | 两份客户端读写 NoteType、NoteTail；O2JamO2 有四档导引及两种音符类型资源，并提供 F6 外观切换提示。当前用 Mania 皮肤，没有对应组合切换入口 | 同类外观可换肤实现；原版选项未移植 |
| F04 | Classic 长条绘制模式 | Classic 读写 LongnoteMode，游玩初始化读取该值，长条绘制有条件分支；还存在 LongCombo 数字资源。当前只有既定的 O2Jam LN 视效开关及 legacy body 修复 | 部分覆盖；具体模式和 LongCombo 含义待核对 |
| F05 | 滚动倍率与显示标尺 | Classic 的结果显示路径包含 0.5、1.5、2.5、3.5、4.5 档。当前滚速连续可调，O2Jam 等价显示表已补齐 3.5、4.5，仍为最近档位投影 | 显示缺口已补齐；实际屏幕速度对应尚未验收 |
| F06 | Dark、Hidden、Sudden 等技能 | 两份客户端有这些技能名称，Classic 另有 Reverse Dark。当前使用原生 Mania HD/FI/Cover，可提供遮挡，但范围、渐变和连击变化未按原版验收 | 部分覆盖；不能以同名判断等价 |
| F07 | 普通与特殊随机 | RD 菜单为 Random、R-Random、S-Random（原 Panic）；逐音符 40 ms 实现隐藏但保留身份，名称/说明固定英文；旧 O2Jam 普通随机仅保留兼容，Seed／回放契约不变 | 已实现并定向验证；实际随机游玩待验收 |
| F08 | 曲风元数据及查询 | 原版选曲有 Dance、Techno、Hip_Hop 等曲风路径。当前 Reader 跳过 OJN 头偏移 12 的字段，公开元数据和导入投影没有 Genre | 明确信息缺口；不是仅缺筛选控件 |
| F09 | 专辑、候选曲目及随机选曲 | 原版有 Album、随机等级区间及 Classic 的候选曲目集合。当前有 lazer 查询、等级筛选、排序、分组及可选目录收藏夹 | 部分可由原生承载；没有原版专辑/候选集合语义 |
| F10 | 新手教程 | 两份客户端有 Tutorial 状态、专用曲目加载和提示资源。当前可把教程 OJN 当普通谱面读取，但没有教程引导流程 | 明确缺口；读入文件不等于支持教程 |
| F11 | 等级任务、解锁及进阶 | 两份客户端含等级任务条件，涉及 ALL COMBO、COOL/GOOD 占比、Jam、生命和技能。当前无任务定义、判定或进度档案 | 范围选择；原版完整解锁还涉及服务端 |
| F12 | Classic 3Key | Classic 有 3Key/7Key 选择限制、独立键位配置及游玩按键效果分支。当前只注册 7K，谱面、回放输入验证也固定七列 | 明确玩法路线缺口；不表示应立即加入 |
| F13 | Couple／Live Battle 等 | O2JamO2 有 Couple 场地；Classic 有 P2P 左右仪表、胜负、BP 等资源和场景路径。当前复用单人 Mania 场地，没有这些规则 | 范围选择；lazer 房间并不等于原版对战规则 |
| F14 | 零血、失败与退场 | 用户选定 EX 零血后完整记录、血量锁零、曲终原生 F；成绩保留，未来个人记录排除 F。NX/HX/NF/MS 按各自契约；原版部分冻结作为有意差异保留 | [新契约已实现](zero-life-and-failure-analysis.md)，145 项定向验证与分层检查通过；客户端验收待完成 |
| F15 | KS 与 BGM 独立音量设置 | 两份原版分别读写 NoteKeyVolume、NoteBGVolume。当前两路都跟随 lazer 主音量/音乐音量，无各自的用户增益控件 | 明确设置缺口；与暂缓的分贝曲线 C07 不同 |
| F16 | O2Jam 专属成绩摘要 | 当前保存原生成绩与判定统计，Core 的 MaximumJamCombo 等没有专门归档/结算字段。原版有独立结果/名次资源，但本轮未完整还原字段含义 | 当前覆盖不足；不能据资源名认定某项原版结果字段 |
| F17 | 原版主题、场景与附属特效 | 原版有 OPI/OPA 场景、Avatar、2D/3D 效果及 EQ 开关。当前使用 Mania/Stable 皮肤和 lazer HUD，不加载这些资源格式 | 已有差异；大部分为宿主/皮肤范围选择 |
| F18 | 暂停、重试、预览、回放 | 当前遵循 lazer 生命周期，预览本地混合 BGM+KS，正式 v5 回放支持跳转。Classic 包有远程 WMA 预览接口及结果 Retry 路径 | 已有差异；原版完整运行契约仍待核对 |

## 需要优先补齐的展示与信息

### F01：有玩法状态，缺少让玩家看见它的通道

`O2JamGameplaySnapshot` 暴露 JamProgress、JamCombo、MaximumJamCombo、
ConsecutiveCoolProgress、Pills、Life 等状态。当前皮肤新增组件主要是 Combo 与 LN 适配，
没有 Jam/药丸组件。玩家不能完整观察晋级、连 COOL 得药丸或 BAD 被救回。

原版 O2JamO2 `Playing1.opi` 含 `Jam_gauge_bar.ojs`、`Jam_gauge_text.ojs`、
`Note_JamNum.ojs`、`Playing_Effect_Jam.oja`；主程序在 `0x45a1f0` 与
`0x4dfe4e` 引用晋级特效及 Jam 数字。Classic 有左右 Jam/Life 仪表及 Jam 效果路径。
药丸的具体图案/动画未逐帧验收，不能据此选定资源或动画参数。

最小实现应在 Presentation 增加可皮肤化组件，读取当前局的状态源；颜色、布局、
显隐与晋级动画不进入 Core。尤其不能让绘制再次判断何时发药丸。

### F02：统计翻译没有覆盖游玩判定组件

当前 `O2LazerRuleset.GetDisplayNameForHitResult()` 已将 Perfect/Good/Ok/Miss
映射为 COOL/GOOD/BAD/MISS，统计适配会过滤多余 Mania 判定项。但原生
`DrawableManiaJudgement` 的默认组件为 `DefaultManiaJudgementPiece`；它与 Argon
继承的 `TextJudgementPiece` 直接使用 `Result.GetDescription().ToUpperInvariant()`。
因此该默认文本路径仍是 PERFECT/GOOD/OK/MISS，不读 ruleset 的上述翻译。

Argon 与 Triangles 的上述判定名称由 `OsuSpriteText` 渲染，不是整词图片素材。
Argon Pro 沿用文字组件，但原生会隐藏 Great/Perfect 判定；这项显隐策略也应保留。

Legacy 路径则读取 `mania-hit300g`、`mania-hit200`、`mania-hit100`、`mania-hit0`
对应贴图，贴图写什么取决于皮肤。这不是判定算法出错；也不能说任意皮肤都一定显示
同一文字。当前 O2JamSkinTransformer 只包装 legacy LN 部件，没有改中央判定组件。

若决定统一名称，应仅为非 MS 的默认文本提供本地化适配，保留原生动画、池和皮肤
选择；第三方 legacy 贴图另定契约。不要全局修改 HitResult 名称或影响 Mania/BMS。
MS 中央判定与结果必须继续表达实际 Mania 规则。

### F08：曲风信息在格式出口已经丢失

原版选曲分支引用 Dance/Techno/Hip_Hop/Classical 等名称，O2JamO2 地址例如
`0x447e17`、`0x4c0489`。当前 `OjnReader.readHeader()` 在 EncodingVersion 后执行
`_ = reader.ReadInt32()`；`OjnMetadata` 没有 Genre，Library 投影也没有补充来源。

若加入，应先在 Formats 保留原始字段，再由 Integration/Host 映射到可检索元数据，
Presentation 显示本地化名称。未知取值仍需保留，不能以某个中文译名作为存储身份。
当前用源目录生成收藏夹并不等于恢复曲风，手动标签也不能补回解码时丢弃的原值。
具体数值到类别的完整映射仍需单独核对。

## 不能直接套用同名原生功能的项目

### F05：滚动倍率不是 DT/HT 的音频速率

Classic 在 `0x4c413b/0x4c41a3/0x4c420b/0x4c4273/0x4c42db` 选择
X 0.5/1.5/2.5/3.5/4.5 显示。当前 ScrollSpeed 是 Mania 时间范围标尺，配置范围
1–40、默认 8；`GetO2JamSpeedMultiplier()` 将 `scrollSpeed/8` 投影到
`[0.5,1,1.5,2,2.5,3,3.5,4,4.5,5,6,8]` 的最近值。2026-10-05 按用户要求补齐
3.5、4.5 显示档位，滚速 28、36 现在分别显示这两个值；没有修改实际滚速、判定或
声音速率。原版各倍率的实际屏幕速度与默认判定线高度尚未测量，不能宣称当前
等价标尺已精确复原。

调整视觉滚速不应改判定/BPM/声音速率。原版任务中的“1 倍速”“0.5 倍速”等也应
先核对是滚动要求，而不是自动翻译成 lazer DT/HT。现有原生上下滚动与 Constant
Speed 是项目扩展，不能反推原版一定具备相同选项。

### F06/F07：遮挡与随机算法需要分别验收

O2JamO2 技能名称在 `0x5aa254–0x5aa27c`：Dark、Sudden、Hidden、Panic、
Random、Mirror。Classic 的 `0x64c86c` 是 Reverse Dark，`0x64c89c` 是
SuperRandom，随后是 Random、Mirror。这些名称不能证明两版本同名技能参数一致，
随后专项追踪确认七键下的 Panic / SuperRandom 共享同一算法；三键不能由此类推。

当前 HD/FI/Cover 复用 Mania；HD/FI 还继承原生的连击驱动覆盖变化。
O2JamO2 普通音符绘制分支 `0x527838–0x52789e` 则直接按模式判断纵坐标：
模式 1 只绘制到 240，模式 2 从 240 绘制，模式 3 只绘制 204–275 区间。
这些是原版局部坐标，不能直接当成 lazer 屏幕像素；该分支没有读取 Combo 来改变阈值。
技能码到这些模式的完整对应及其他分支仍需核对，但已经不能宣称当前遮挡策略完全相同。

**不能把原版 Sudden 技能直接等同于 lazer Sudden Death 的一击失败条件**；原版的遮挡高度、
透明度、出现/消失区间及 Dark/Reverse Dark 含义需继续跟踪绘制路径。现有原生组件
可承担经确认相同的部分，只有参数或策略不能表达的缺口才另做适配。

RD 默认保留原生 Mania 的整局固定置换，菜单显示 Random、R-Random、S-Random。
新 S-Random 使用原 Panic 算法；R 轮转和隐藏的旧逐音符 S 是[用户选择的扩展](column-randomisation.md)，
旧普通随机身份只作兼容。Panic 按原始小节重排，LN 与边界邻近保护区共用映射，和弦保持
一对一。种子初始化、名称对应与 Classic 七键算法已闭合；具体证据、分层、算法身份
及旧回放默认值见[专项追踪与实现契约](original-random-and-failure-analysis.md)。
纯算法在 Integration，Host 只接 Mod 设置，判定 Core 不新增职责。自动化已覆盖保护边界、
种子、回放、MS/Invert/DT；实际客户端的随机谱面和 KS 仍待验收。

### F03/F04：外观选项与长条规则分开

两份客户端的 NoteType/NoteTail 配置读写分别见 O2JamO2 `0x43b239/0x43b27e`
和 Classic `0x43a63e/0x43a683`。Classic 检查 NoteType 为 0/1、NoteTail 为
0–3；O2JamO2 的资源包括两种 NoteType、导引 Long/Middle/Short/NoUse，F6
提示为四档外观切换。导引具体如何绘制尚未实测，不能把 NoteTail 配置当成 LN 尾判定。

Classic 在 `0x56fb17–0x56fb20` 读取 LongnoteMode 到游玩对象，在
`0x56e372–0x56e38a` 按该值选择额外长条绘制路径；场景还读取同一设置选择
LongNote_Effect，含 Note_LongCombo/Note_LongComboNum 资源。这里已经证明有独立
绘制模式，但没有证明其改变计分、增加长条 tick，或与当前 LN 视效开关完全相同。
不应仅凭 LongCombo 资源名为内核增加连击。

## 可选玩法路线与独立客户端功能

### F10/F11：教程和任务不是单张谱面解码

两份客户端都保留 Tutorial.ojm 与专用 Tutorial 状态；O2JamO2 `0x4ae7f2`、
Classic `0x4f0a48` 有教程音频加载引用。当前导入教程文件只会生成普通可玩谱面，
不包含按键教学、分步引导或教程成功条件。

两份客户端任务文本还包含：指定等级以上 ALL COMBO、COOL 占比、GOOD 占比、
0/10/20 Jam、满生命、限定滚速及技能。例如 O2JamO2 `0x5a5614–0x5a5a0c`
与 Classic `0x647350–0x647748`。这里的 COOL/GOOD 比例与项目准确率公式不是
同一个量，不能拿当前 Accuracy 阈值直接满足任务。

这些条件的离线判定可以未来独立设计；完整等级解锁、经验奖励和官方档案仍需服务端
规则证据。任务政策宜独立消费玩法事件/摘要，UI 留 Presentation，进度存储走既定
持久化边界；不把账号等级、联网或任务页面加入 Core。

### F12/F13：3Key 和对战需要独立设计

Classic 不只是留下“3Key”文字：有独立 3Key/7Key 键位保存/重置提示，
场地构建在 `0x4dc9a3` 与 `0x4dca3c` 选择对应按键效果资源，另有两侧 P2P
3Key/7Key 效果、BP、胜负与仪表。3Key 的源谱面列转换、冲突处理和解锁条件尚未
完整还原，不能直接减 ColumnCount 或只加一个键数 Mod。

当前 GameplayVariants、StageDefinition、OJN 可演奏列以及 replay action 验证均以
七列为契约。3Key 可以作为未来路线，但需连同谱面身份、Mod、星级与回放一起设计。
Couple/P2P 则还要定义玩家输入、生命/比分归属与胜负规则；原生 lazer 多人房间提供
联机设施，不自动提供这些玩法。不能只画两个 Mania 场地就称为原版对战。

商城、Avatar、Gem/现金、音乐购买下载、好友/公会、频道、发行入口、活动与网页等
内容在原版确实存在，但当前项目没有承诺重做独立客户端。普通选歌、换肤、键位配置、
截图、暂停/重试等已有原生宿主设施，不应为复刻菜单重新实现。

## 成绩、档案与生命周期

F16 当前 `PopulateScore()` 在原生流程后补 Combo/MaxCombo，并为锁血成绩输出 F/未通过；v5 摘要包含原生分数、
准确率、统计、Mods、评级和暂停，不保存 Jam 最大值/药丸消耗/最终生命等专属字段。
这些状态仍可在当前局或回放重演中计算，但重演值不能自动当成旧成绩的持久化真值。
原版 `Result_EachResult`、`Result_Imgs_Rank`、`Result_WinLose` 只证明有结果/名次
表现；“最大 Jam 原版一定在结算显示”尚未取得完整字段证据。

项目的 O2Jam 加权准确率、SS–F、星级、PP 资格、UR、MS 及大量 lazer Mods 都是
明确的接入/扩展政策。原版存在名次资源，不表示项目的 SS–F 等于原版评级；也不能
因本轮未找到某种统计就断言原版没有它。MS 开启时整套 native Mania 玩法本就不是
O2Jam 原规则。lazer Mod 对原始分数的倍率也应与 Core 原始累积分区分。

F14 的[零血专项对照](zero-life-and-failure-analysis.md)已确认：O2JamO2 普通场景的
EX/NX/HX 分流具有房间条件；Classic 普通零血先报告网络事件。两客户端最高 Combo/
计数更新有差异，剩余药丸还会让 state 6 BAD 跳过计分/断连回调。修改前 Core 的冻结
并不包含原生判定次数；原生失败动画与 NF 恢复是接入政策。初次静态审查的 36 项过滤基线通过。
随后用户选定 EX 完整记录、锁血、曲终 F 与未来个人记录排除政策，已按分层实现，
145 项过滤测试及分层检查通过。原版剩余通过/服务端语义只保留为证据待核对，不再
作为实现新规则的前提；当前新规则的真实客户端验收仍待完成。

F15 原版 NoteKeyVolume 的读写见 O2JamO2 `0x50cb6e/0x50cc81`、Classic
`0x55147e/0x551591`；NoteBGVolume 分别见 `0x50ccee/0x50cdd1` 与
`0x5515fe/0x5516e1`。当前 KS 的资源宿主绕过效果音量，再叠主音量和音乐音量，
BGM 也走音乐混音器；没有单独 KS/BGM 设置。若未来增加独立增益，仍必须保留用户
要求的不受全局效果音量/打击音效开关影响，且不自动启用暂缓的 C07 分贝/声像曲线。

F18 暂停续播、回放跳转、全局预览生命周期按用户需求复用 lazer。Classic 的
`mms://.../o2jam_soundtrack/%d.wma` 与 Player.dll 支持远程 soundtrack 路径，
不代表所有版本/所有预览都只用远程音频。原版结果有 Retry 资源及房间等待者限制，
当前复用 lazer 重试，不需要复制房间权限逻辑。原版是否具备当前同等的回放跳转功能
没有取得证据，不能将项目新功能当成“与原版回放不一致”的 bug。

已触发 KS 跳转进其中段无法恢复尾音的局限、判后 KS 不再触发的用户选定策略，以及
连续不对称判定与原版整数位置的边界差异仍沿用既定契约，不因本次检查改变。

## 已暂缓项目的归属参考

2026-10-06 用户决定停止继续补齐其余差异，原版对齐阶段结束。下表只保留将来重新
讨论时的所有者建议，不代表现在的实施顺序；F07 与 F14 的已选方案不需要重做。

| 顺序 | 对象 | 建议所有者与原生复用原则 |
|---|---|---|
| 1 | F01/F02：Jam/药丸反馈、判定名称 | Presentation；读状态、复用原生池/动画/皮肤接口，MS 保持原生 |
| 2 | F14：零血与失败链路 | 用户选定的新 EX 契约已实现；验收完整记录、锁血、F、回放回滚与 NF/MS。原版证据不再决定是否复刻冻结 |
| 3 | F08/F05：曲风数据、滚速标尺 | Formats 保留信息；Host 元数据/配置承接；Presentation 展示，不改玩法时钟 |
| 4 | F06/F07/F04：技能与特殊随机、Classic LN 模式 | 先核对参数/算法；优先原生 Mod 和 drawable 参数，不能凭名称复刻 |
| 5 | F16/F15：专属成绩摘要、独立混音设置 | 先敲定档案字段与混音目标；Host/Persistence 与 Host/Audio 各自负责 |
| 6 | F10/F11/F12/F13：教程、任务、3Key、对战 | 用户确认产品范围后单独路线设计，不扩大当前判定重构的隐含范围 |

F03/F09/F17 可以继续由原生皮肤、查询、收藏夹与宿主提供同类能力。是否需要原版
外观预设、专辑语义或场景特效，应由具体玩家需求决定，不能以“原版有”作为重写
lazer 的理由。完整 OPI/OPA 导入也不应成为补 Jam/药丸 HUD 的前置条件。

## 初次静态检查与保留验证边界

初次静态检查完成两版本文本/指令路径、O2JamO2 资源名称与当前实现的静态交叉核对。
没有运行 benchmark、测试套件、真实曲库更新/清空，也没有修改数据库、游戏配置、
客户端二进制或原版资源。

保留而暂不继续的运行核对包括原版技能遮挡参数、特殊随机与 LN 冲突、Classic 3Key 转换、
LongnoteMode、精确滚速及零血时序。此前连续不对称判定安装版也仍需要用户实际验收；
本次功能检查不代替那项验收。未来重新启动某项时按 F 编号独立确认需求，避免将所有缺口
一次性并入判定内核。当前阶段关闭及验证边界见[收尾复核](recent-changes-review.md)。
