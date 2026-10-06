# O2Lazer

一个用于直接在 osu!lazer 中游玩原生 O2Jam 谱面的规则集，支持直接读取 `.ojn` 与 `.ojm` 文件。

[English](./README.md)

当前范围：2026-10-06 已按用户决定结束本次原版客户端对齐，其余差异暂缓；不代表完整复刻所有客户端功能或已验收所有实机场景。入口见[文档索引](docs/README.md)，收尾结果见[近期复核](docs/recent-changes-review.md)。

当前职责见[架构说明](docs/clean-rewrite-architecture.md)，待处理事项见[全项目审查清单](docs/architecture-audit.md)，阶段和验收见[重构路线图](docs/refactor-roadmap.md)。

## 设计定位

已明确选择的连续不对称窗口、判前 KS 停旧重播/判后禁用与 EX 完整记录/锁血/F 优先于客户端复刻。
这些是当前政策，不是等待恢复原版的临时补丁。

O2Lazer 将已有可靠依据的 O2Jam 判定、分数、生命、连击、Jam、药丸、长条及 OJM 事件行为作为玩法真值，
同时以原生 ruleset 的方式融入 osu!lazer。当 osu! 需要而 O2Jam 没有定义某项概念时，ruleset 会通过最接近的
mania／lazer 契约给出符合 O2Jam 语言的结果；非 MS 的准确率与字母评级即采用这一策略。对于 O2Jam 客户端
和加载器之间没有统一结论的应用生命周期行为，包括暂停与恢复，则沿用 lazer。仅当宿主的通用假设无法表达
O2Jam 的独立性时，才使用限定于本 ruleset 的兼容补丁，同时保留原生控件、布局、动画与交互习惯。
详见[架构决策顺序](docs/clean-rewrite-architecture.md#design-intent-and-decision-precedence)。

## 特性

- 支持经典与新版加密 OJN 文件，包含 EX、NX、HX 三种难度。
- 内存中解码 M30、OMC、OJM 的键音与背景音乐。
- 支持 7K 音符、长条、BPM 变化、键音与 BGM 事件。
- 显示原生 O2Jam 难度名称与等级。选歌界面依次显示 o2ma 编号、mania 星数、O2Jam 等级，替代 CS/AR/OD/HP；mania 星数跟随当前选择的 Mod 更新。编号比例条始终满格，等级比例条以 150 为满格，超过 150 仍显示实际等级。
- 仅在选择 O2Lazer 时，于原生星数选项下方增加“等级”排序与分组：排序按每个难度的 O2Jam 等级排列；分组采用 `[N, N+10)` 区间，从 `Lv.0 - 10` 到 `Lv.140 - 150`，随后为 `Over Lv.150`，每组沿用其等级除以 10 后对应的原生星数分组颜色。原生星数选项继续使用 mania 星数。
- 未选择 Mania Score 时，将选歌界面的原生难度范围条复用为整数 O2Jam 等级筛选（`Lv.0`–`Lv.100`，随后为不限）；滑块节点只显示数字，使等级 100 能完整容纳在原生控件内。选择 Mania Score 后恢复原生星数筛选，来回切换时两套范围各自保留原值。
- 将 OJN 内嵌封面作为谱面背景，并可在遇到无法读取的谱面时继续导入。
- EX、NX、HX 难度分别保存独立成绩显示。
- 基于字段校验和保守的目录提示，自动区分 CP949、GBK/CP936 与 UTF-8；不再仅凭 OJN 版本判断编码。
- 在谱面位置坐标中计算 O2Jam 风格的 COOL/GOOD/BAD/MISS，支持局内 BPM 变化、原生风格计分、血量、Jam、药丸及独立的 LN 首尾判定。
- 非 MS 的原生条形误差表按 O2Jam tick 映射，以 Mania OD7、1× 黄300整体宽度的一半作为早侧 COOL 基准，晚侧同刻度增加 1 tick，早／晚色区为 6／7、18／19、25／26 tick；BAD 色区和最终 BAD 标记使用原生 Mania 50 黄色，UR 数字仍采用原生毫秒统计。MS 的 HT／DC／DT／NC 判定窗口与误差条复用原生 Mania 速率补偿。
- 未开启 Mania Score 或 No Fail 时，EX 归零后血量锁零，分数、判定统计、准确率、Combo、Jam 与药丸继续记录；整曲完成后原生评级为 F，成绩/回放保留，未来个人记录排除 F。NX/HX、NF 回血和 MS 的原生 Mania 政策分别保留。
- 非 MS 准确率按 O2Jam 基础判定值（`COOL=200`、`GOOD=100`、`BAD=4`、`MISS=0`）计算，再映射到 osu! 通用字母评级区间。这些兼容指标不会替代 O2Jam 原始分数；选择 Mania Score 后则将两者都委托给原生 mania。
- 选歌与本地展开结算面板按当前或成绩记录的 Mod 使用原生 mania 星数。未选择 Mania Score 时，星数胶囊只显示 `Lv.N`，胶囊、同一 set 内其他难度的小胶囊和结算 ruleset icon 均按各自 `等级 / 10` 取色，set 内难度按等级排列，等级相同时再按 EX、NX、HX 区分；选择 Mania Score 后立即恢复原生星形、mania 星数、颜色和星数排序。基础模式切换直接读取 Realm 中带版本的信息，不读取谱面；只有变速或结构性 Mod 才重新计算。星数搜索与全局难度排序使用基础数值；新导入不再保存等级除以 10 的标签；使用“刷新谱面”可清除已有谱面的旧标签。
- 禁用 O2Lazer 的原生谱面编辑器入口，保护导入谱面；皮肤编辑器保持可用。OJM 键音不受原生“谱面打击音效”开关影响，也不受全局效果音量影响。
- 选歌界面继续显示 o2ma、SR 和等级；O2Lazer 的 Mod 选择界面右下角不再显示这三项，左侧星数胶囊与 BPM 保持显示。
- 提供固定曲库路径和优化后的增量更新；既有未变化谱面不会进入解析／写入批次，同时仍会处理已修改及已失去源文件的谱面。
- 复用 osu!mania 原生游玩区域和 stable 皮肤表现，同时保持 O2Jam 判定与计分状态独立。
- 支持重构版 replay 录制、播放，以及 O2Jam 专用 HUD／Playfield 皮肤编辑器层。
- 提供原生自动游玩，以及与 mania 一致的 No Fail、Easy、Half Time、Daycore、No Release、Sudden Death、Perfect、Hard Rock、Double Time、Nightcore、Fade In、Hidden、Cover、Flashlight、Accuracy Challenge、Random、Mirror、Mania Score、Classic、Invert、Constant Speed、Wind Up、Wind Down、Muted 和 Adaptive Speed。名称、英文描述、图标、排序、分数倍率与计表现分状态均与 mania 保持一致；Random 默认使用原生整曲随机，Mod 设置提供 Random、R-Random（含镜像轮转）与 S-Random（使用 O2Jam Panic 小节随机），算法名称与说明固定英文，共用原生 Seed 并随成绩与回放保存；此前逐音符 S-Random 隐藏入口但保留算法和回放身份，旧 O2Jam 普通随机身份也只作兼容，详见[随机契约](docs/column-randomisation.md)；O2 专用适配在复用原生 Mod 行为时保留准确的音符／长条类型、谱面位置判定与 OJM 音频。HT／DT 默认保持 BGM 与 keysound 音高，Adjust Pitch 设置同时作用于两者；DC／NC 对两条音频路径应用 mania 的变调规则，NC 也保留原生节拍音。动态变速 Mod 的画面流速与玩家触发的 keysound 会跟随实时速度。Constant Speed 替代原来的固定流速设置，不改变判定时机。Mania Score 会将游玩物件转换为原生 mania 物件，并把计分、连击、评级、血量、结算统计与 PP 委托给 mania；内置 OD／HP 调整默认均为 7，保持默认时可计表现分，修改后遵循 mania 的不计表现分策略。Easy、Hard Rock 与 Classic 在未选择 MS 时呈不可选灰色；强制选择或从其他模式带入其中任意一个都会同时实际开启 MS，关闭 MS 会移除这些 Mod，而只关闭这些依赖 Mod 时会保留 MS。未选择 Mania Score 时，包括 No Mod 在内的所有组合均不计表现分。

默认键位为 `S D F Space J K L`。

## 安装

当前 `master` 开发构建以 osu!lazer **2026.1005.0** 为目标；为保持 ruleset 身份，程序集版本仍为
**1.0.0**。第二个测试版为 [1.0.0-test2](https://github.com/sumiyuki727/O2Lazer/releases/tag/1.0.0-test2)。
下载发布 DLL 或使用匹配的 Game 与 Mania 二进制构建，退出 lazer 后替换
数据目录 `rulesets` 下的 DLL，再启动游戏。备份请放在 `rulesets` 目录之外，不要同时安装两个
O2Lazer 版本。现有导入与成绩关联预期保留，但存储迁移仍需明确验证。当前 replay 读取器接受带
`o2lazer` 标记的 v5；重构前无标记 replay 属于测试实现，明确不在兼容范围。

本版宿主的依赖核对、回归结果和实机验收范围见[2026.1005.0 适配记录](docs/lazer-20261005-compatibility.md)。

## 导入曲库

将每个 `.ojn` 与对应的 `.ojm`、`.omc` 或 `.m30` 放在同一目录。打开 **设置 -> O2Jam**，
选择固定显示的曲库路径，然后点击 **更新谱面**。更新会先通过分批并行指纹检查排除未变化的既有谱面，
因此只添加少量新谱面时不会再把全部既有谱面安排进解析／写入批次；位于不同路径但内容完全相同的 OJN
也会通过已保存的哈希在星级计算前识别。同一操作仍会更新已修改谱面、迁移元数据，并移除失去源文件的导入。
**清空谱面导入**只清理游戏内导入，不删除源文件。音频仍引用外部档案，请保留原始曲库。

按源文件夹生成收藏夹默认关闭；开启后随谱面更新同步，关闭时只删除此功能管理的收藏夹，不影响无关收藏夹。
选歌预览固定同时播放 BGM 和演奏键音。背景编排兼容的难度继承播放，不同背景编排的难度使用独立预览。
LN 只在头部播放键音，尾部保持静音。游玩内未判定物件的误触与首次命中会停止同 KS 的旧声道并从头播放；单点或长条头判定后不可再次触发，另一个未判定物件使用同 KS 仍可播放。暂停/恢复保持当前声道的位置；预览与背景并发不变。

## 游玩与皮肤选项

下落速度使用 mania 的视觉标尺，并同时显示 O2Jam 等价值。Constant Speed 在 BPM 变化时保持固定视觉时间范围，
但不改变判定；原设置开关已经删除，只有选择该 Mod 才会启用此行为。
O2Jam 长按视效默认关闭；开启后，松键的 LN 保持原色：最终 Cool/Good（包括药丸修正后的 Cool）继续裁切，
Bad/Miss 停止裁切并保留剩余长度继续下落。此过程不会延迟计分或维持按住光效。
独立的投皮修复选项负责延伸过长的 legacy LN body，并同步多帧动画。

当前规则依据参考实现与玩家验证，不代表已经完全复现原版客户端。
证据与边界详见[行为规格](docs/o2jam-behaviour-spec.md)及[原版功能覆盖检查](docs/original-client-feature-audit.md)。独立 Jam／药丸 HUD、中央默认判定文本、独立 KS/BGM 增益与专属成绩摘要等剩余对齐项暂缓；第三方皮肤的文字/贴图继续由皮肤决定。

## 搜索谱面

在 O2Lazer 选曲搜索框中，可以组合使用以下条件：

- `ln>50`：LN 占比严格大于 50%；`ln>=50` 包含恰好 50% 的谱面。
- `note>50`：单键占比大于 50%。占比按每个难度的对象数量计算：LN 数量 ÷（单键数量 + LN 数量），每条 LN 只计一次，不按时长或首尾判定数加权。
- 比例支持 `=`, `!=`, `<`, `<=`, `>`, `>=`、小数和可选的 `%`，例如 `ln>=25 ln<75`。
- `level>=50` 或 `lv>=50`：按原生 O2Jam 等级筛选。两个关键词均不区分大小写，支持 osu! 的比较符号（`=`, `!=`, `<`, `<=`, `>`, `>=` 及相应的 `:` 写法），可以组合，例如 `LEVEL>=50 lv<100`；等级搜索不受筛选条上限 100 的限制。
- `o2ma100`：只匹配完整编号标签 `o2ma100`，不匹配 `o2ma1000`、`o2ma1001`，不区分大小写。
- 裸数字 `100` 仍可搜索普通曲名、作者、难度等内容，但不会因为 `o2ma100` 编号、编号形式的文件名或内部导入标签而命中。

例如 `o2ma100 ln>50` 会筛选该编号下 LN 占比大于 50% 的难度。搜索使用已导入的元数据，不需要重新导入或解码谱面。

原生 `stars>5` 始终按 osu!mania 星级筛选，不受 MS 开关影响；例如 `stars>=3 stars<5 lv>=50` 可组合 mania 星级与 O2Jam 等级。

旧曲库升级后，请使用一次**刷新谱面**，补齐两种星级和版本信息，不会更换谱面 ID 或成绩关联。以后刷新时会跳过未变化且版本有效的条目。原生后台重算同样使用 mania 算法；切换 MS 只读取已有数值。未算出的 mania 星数内部使用 `-1` 哨兵；选歌 SR 行保留上一有效值或暂留空白，计算完成后再更新。

## 构建

编译需要 **.NET 10 SDK**（C# 14），测试需要 .NET 8 runtime。引用与目标版本一致的现成 lazer DLL，
构建过程不修改同级源码仓库。

```powershell
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
dotnet build osu.Game.Rulesets.O2Lazer.slnx -c Release "-p:OsuBinaryDirectory=$lazerBinaries"
./scripts/verify.ps1 -OsuBinaryDirectory $lazerBinaries
```

其他 DLL 路径、定向测试和可选本地诊断见[开发与测试说明](docs/development.md)，
模块边界见[重构架构](docs/clean-rewrite-architecture.md)。

## 致谢与许可证

当前 ruleset 是独立重构实现，不编译或包含已归档的 BMS 衍生旧实现；重构前项目仅作为行为参考单独保存。O2Jam 格式工作参考了 MIT 许可的 [O2MusicBox](https://github.com/SirusDoma/O2MusicBox)、[CXO2](https://github.com/SirusDoma/CXO2) 与公开的 Open2Jam 格式文档。本项目以 AGPL-3.0 许可发布，详见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
