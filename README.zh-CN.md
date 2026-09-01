# O2Lazer

一个用于直接在 osu!lazer 中游玩原生 O2Jam 谱面的规则集，支持直接读取 `.ojn` 与 `.ojm` 文件。

[English](./README.md)

## 特性

- 支持经典与新版加密 OJN 文件，包含 EX、NX、HX 三种难度。
- 内存中解码 M30、OMC、OJM 的键音与背景音乐。
- 支持 7K 音符、长条、BPM 变化、键音与 BGM 事件。
- 显示原生 O2Jam 难度名称与等级。选歌界面依次显示 o2ma 编号、mania 星数、O2Jam 等级，替代 CS/AR/OD/HP；mania 星数固定显示已保存的 mania 星级，不受 MS 开关影响。编号比例条始终满格，等级比例条以 150 为满格，超过 150 仍显示实际等级。
- 仅在选择 O2Lazer 时，于原生星数选项下方增加“等级”排序与分组：排序按每个难度的 O2Jam 等级排列；分组采用 `[N, N+10)` 区间，从 `Lv.0 - 10` 到 `Lv.140 - 150`，随后为 `Over Lv.150`，每组沿用其等级除以 10 后对应的原生星数分组颜色。原生星数选项继续使用 mania 星数。
- 将 OJN 内嵌封面作为谱面背景，并可在遇到无法读取的谱面时继续导入。
- EX、NX、HX 难度分别保存独立成绩显示。
- 基于字段校验和保守的目录提示，自动区分 CP949、GBK/CP936 与 UTF-8；不再仅凭 OJN 版本判断编码。
- 在谱面位置坐标中计算 O2Jam 风格的 COOL/GOOD/BAD/MISS，支持局内 BPM 变化、原生风格计分、血量、Jam、药丸及独立的 LN 首尾判定。
- 选歌主星级与结算星级在非 MS 时显示 OJN 等级除以 10，MS 时显示已保存的 mania 星级；结算使用成绩记录中的 Mod。原生 `StarRating` 存储 mania 星级以供搜索与排序，O2Jam 星级独立存储。MS 在 mania 计分实现前仍保持隐藏占位状态。
- 禁用 O2Lazer 的原生谱面编辑器入口，保护导入谱面；皮肤编辑器保持可用。OJM 键音不受原生“谱面打击音效”开关影响，也不受全局效果音量影响。
- 提供固定曲库路径、增量刷新以及删除全部已导入 O2Jam 谱面的功能。
- 复用 osu!mania 原生游玩区域和 stable 皮肤表现，同时保持 O2Jam 判定与计分状态独立。
- 支持重构版 replay 录制、播放，以及 O2Jam 专用 HUD／Playfield 皮肤编辑器层。
- 提供原生自动游玩，以及与 mania 一致的 No Fail、Half Time、Daycore、No Release、Sudden Death、Perfect、Double Time、Nightcore、Fade In、Hidden、Cover、Flashlight、Accuracy Challenge、Random、Mirror、Invert、Constant Speed、Wind Up、Wind Down、Muted 和 Adaptive Speed。名称、英文描述、设置、图标、排序、分数倍率与计表现分状态均与 mania 保持一致；O2 专用适配在复用原生 Mod 行为时保留准确的音符／长条类型、谱面位置判定与 OJM 音频。HT／DT 默认保持 BGM 与 keysound 音高，Adjust Pitch 设置同时作用于两者；DC／NC 对两条音频路径应用 mania 的变调规则，NC 也保留原生节拍音。动态变速 Mod 的画面流速与玩家触发的 keysound 会跟随实时速度。Constant Speed 替代原来的固定流速设置，不改变判定时机。未选择 Mania Score 时，所有组合（包括 No Mod）均显示不计表现分；选择后按 mania 原生模组资格显示。Mania Score 的选择 UI 目前已隐藏。实际 mania 计分／PP 计算仍待实现。

默认键位为 `S D F Space J K L`。

## 安装

当前独立重构预发布标签为 **1.0.0-test**；为保持 ruleset 持久化身份兼容，程序集版本仍为
**1.0.0**。该版本面向 osu!lazer **2026.804.2**。构建或获取兼容的
`osu.Game.Rulesets.O2Lazer.dll`，退出 lazer 后替换数据目录中 `rulesets` 下的 DLL，再启动游戏。
备份请放在 `rulesets` 目录之外，不要同时安装两个 O2Lazer 版本。
持久化 ruleset 身份保持不变，已有导入和成绩关联可以保留。重构前 replay 不再支持，但不会删除已有成绩记录。

## 导入曲库

将每个 `.ojn` 与对应的 `.ojm`、`.omc` 或 `.m30` 放在同一目录。打开 **设置 -> O2Jam**，
选择固定显示的曲库路径，然后点击 **更新谱面**。更新会导入新增和已修改的谱面，并移除失去源文件的导入；
跳过的未变化谱面也计入进度。**清空谱面导入**只清理游戏内导入，不删除源文件。音频仍引用外部档案，请保留原始曲库。

按源文件夹生成收藏夹默认关闭；开启后随谱面更新同步，关闭时只删除此功能管理的收藏夹，不影响无关收藏夹。
选歌预览固定同时播放 BGM 和演奏键音。背景编排兼容的难度继承播放，不同背景编排的难度使用独立预览。
LN 只在头部播放键音，尾部保持静音。

## 游玩与皮肤选项

下落速度使用 mania 的视觉标尺，并同时显示 O2Jam 等价值。Constant Speed 在 BPM 变化时保持固定视觉时间范围，
但不改变判定；原设置开关已经删除，只有选择该 Mod 才会启用此行为。
O2Jam 长按视效默认关闭；开启后，松键的 LN 保持原色：最终 Cool/Good（包括药丸修正后的 Cool）继续裁切，
Bad/Miss 停止裁切并保留剩余长度继续下落。此过程不会延迟计分或维持按住光效。
独立的投皮修复选项负责延伸过长的 legacy LN body，并同步多帧动画。

当前规则依据参考实现与玩家验证，不代表已经完全复现原版客户端。
证据与边界详见[行为规格](docs/o2jam-behaviour-spec.md)。独立 Jam／药丸 HUD 组件、进一步的预览性能优化仍待完成。

## 搜索谱面

在 O2Lazer 选曲搜索框中，可以组合使用以下条件：

- `ln>50`：LN 占比严格大于 50%；`ln>=50` 包含恰好 50% 的谱面。
- `note>50`：单键占比大于 50%。占比按每个难度的对象数量计算：LN 数量 ÷（单键数量 + LN 数量），每条 LN 只计一次，不按时长或首尾判定数加权。
- 比例支持 `=`, `!=`, `<`, `<=`, `>`, `>=`、小数和可选的 `%`，例如 `ln>=25 ln<75`。
- `level>=50` 或 `lv>=50`：按原生 O2Jam 等级筛选。两个关键词均不区分大小写，支持 osu! 的比较符号（`=`, `!=`, `<`, `<=`, `>`, `>=` 及相应的 `:` 写法），可以组合，例如 `LEVEL>=50 lv<100`；等级搜索不受比例条上限 150 的限制。
- `o2ma100`：只匹配完整编号标签 `o2ma100`，不匹配 `o2ma1000`、`o2ma1001`，不区分大小写。
- 裸数字 `100` 仍可搜索普通曲名、作者、难度等内容，但不会因为 `o2ma100` 编号、编号形式的文件名或内部导入标签而命中。

例如 `o2ma100 ln>50` 会筛选该编号下 LN 占比大于 50% 的难度。搜索使用已导入的元数据，不需要重新导入或解码谱面。

原生 `stars>5` 始终按 osu!mania 星级筛选，不受 MS 开关影响；例如 `stars>=3 stars<5 lv>=50` 可组合 mania 星级与 O2Jam 等级。

旧曲库升级后，请使用一次**刷新谱面**，补齐两种星级和版本信息，不会更换谱面 ID 或成绩关联。以后刷新时会跳过未变化且版本有效的条目。原生后台重算同样使用 mania 算法；切换 MS 只读取已有数值。尚未计算的 mania 星级显示为未计算值（`-1`），等待处理完成。

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
