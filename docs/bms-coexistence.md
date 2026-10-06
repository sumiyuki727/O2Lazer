# BMSRuleset 共存验证

维护状态（2026-10-06）：现行共存边界；指定 BMS 两种独立载入顺序通过，完整客户端链路边界保留。
文档职责与最新状态入口见[索引](README.md)。

首次验证：2026-09-24；本轮复核：2026-10-05。当前目标宿主为 osu!lazer 2026.1005.0；实际安装并参与测试的 BMSRuleset 程序集版本为 **2026.920.0.0**。`D:/rulesets/BmsRuleset` 只读源码检出仍对应较早的 2026.804 配置，不能代替已安装 DLL 的行为依据。升级 BMS 或宿主后须重新执行本页矩阵。

2026-10-05 新版宿主下重新运行两种独立进程载入顺序，均通过本页矩阵所列检查。
26 项 O2Lazer 补丁也全部安装成功；没有新增共存适配。
完整升级记录及回归边界见[宿主适配](lazer-20261005-compatibility.md)。

## 补丁边界

2026-10-05 tick 误差条适配版通过下方两个独立进程载入顺序检查，且两者都确认
`O2JamHitErrorMeterPatch.IsInstalled` 为真。新增接缝只涉及两种原生误差表加载与
显示坐标，不与当前 BMS 共用补丁目标重叠；这不能替代所有自定义 HUD 的实机验收。

2026-10-03 星级缓存批次版再次通过下方两种实际 DLL 载入顺序检查。新改动只调整 O2Lazer 存储契约与原生事务调用粒度，没有新增 Harmony 补丁或改变 BMS 写入；仍需下方客户端全链路验收。

2026-10-03 文件校验观察器版再次通过下方两种实际 DLL 载入顺序的筛选功能测试。新观察器只匹配当前 O2Lazer 写入的线程/实例/Hash，不修改原生 Add 结果，未给 BMS 增加钩子协调；这些检查不替代真实双 ruleset 完整游玩链路验收。

2026-10-03 新增可选 O2Lazer 原生难度持久化适配后，使用实际安装 DLL 再次以两个独立进程检查两种载入顺序，均通过。新增 StarRating setter 适配只消费同线程、已验证的 O2Lazer 基线结果，不修改其他 ruleset 的原生写入；另有临时 Realm 的其他规则集拒绝测试。此结果不替代下方仍待完成的完整客户端链路。

两个 ruleset 都合入 Harmony 时，同一原生方法可能被两份 Harmony 运行时改写。`Host/Compatibility/O2JamBmsHarmonyCompatibility` 是**明确限定 BMSRuleset** 的兼容适配器：BMS 已加载时，O2Lazer 将共用方法的钩子注册到 BMS 的 Harmony；BMS 后加载时，在程序集载入事件中补注册。回滚按 O2Lazer 自己的 Harmony ID 撤销，不撤销 BMS 的 ID。该类不是未来所有 ruleset 的通用补丁协调器；添加第三个 ruleset 时需重新审视运行时、所有者和载入顺序。

参与补注册的 O2Lazer 入口：`BeatmapTitleWedge.DifficultyDisplay.updateCountStatistics`、`DifficultyIcon.getRulesetIcon`、`Player.ImportScore`、`ScoreImporter.GetScore`、`ScoreImporter.CreateModel`、`ScreenStack.Push`。这些仍是宿主私有方法；原生的 `DifficultyIcon` 对社区 ruleset 的负在线 ID 返回问号，原生 replay 导入也没有满足 O2Jam 存档格式的公开扩展点，故当前无法移除相应补丁。`O2JamWorkingBeatmapHook` 使用不同的 manager 入口，BMS 的包装器使用自己的缓存入口；测试验证两者未互相替换。

## 当前版本的测试矩阵

两个顺序**分别在独立测试进程**运行。测试使用临时目录、临时 Realm 和合成 BMS/OJN 数据，不触碰客户端用户库。

| 顺序 | BMS 谱面与星级 | 单曲结算入口 | O2Jam replay 读取 | 共用补丁 |
|---|---|---|---|---|
| BMS → O2Lazer | 18 个物件；星级前后均 `0.23321105733995243` | 原生 `SoloResultsScreen` 被替换为 BMS 自定义结算页 | 通过 | 统计、图标及 `ScreenStack.Push` 均使用 BMS Harmony |
| O2Lazer → BMS | 同上 | 同上 | 通过 | BMS 载入后补注册，验证通过 |

安装版 BMS 没有旧源码中的 `BmsDifficultyIconPatcher`；因此原测试要求它在原生 `DifficultyIcon` 内返回 `BmsRulesetIcon` 是过时断言。新测试以安装版在 O2Lazer 初始化前的实际图标类型为基线，并要求之后不改变。此结论只针对原生 `DifficultyIcon` 入口。安装版 BMS 的单曲结算页使用自己的 `BmsResultDifficultyIcon`，其中直接创建 `BmsRulesetIcon`；如果结算时显示 `?`，说明可能落回了原生结算页，不能据原生图标基线认定为 BMS 设计。实际发现 O2Lazer 的编辑器入口保护和 BMS 的结算页替换都补丁到 `ScreenStack.Push`；现在两者共享 BMS Harmony，并在双顺序测试中断言推入原生单曲结算页会被替换。安装修复版后，用户已在客户端确认 BMS 单曲结算图标显示正常。

可复现命令（`$lazerBinaries` 指向匹配的 2026.1005.0 安装）：

```powershell
$env:O2JAM_BMS_RULESET_PATH = 'D:/osu-lazer/rulesets/osu.Game.Rulesets.BmsRuleset.dll'
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
$project = 'osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj'
dotnet test $project -c Release "-p:OsuBinaryDirectory=$lazerBinaries" --filter 'FullyQualifiedName~O2JamBmsCompatibilityTest.BmsFilteringAndStarsSurviveO2LazerInitialisation'
dotnet test $project -c Release "-p:OsuBinaryDirectory=$lazerBinaries" --filter 'FullyQualifiedName~O2JamBmsCompatibilityTest.BmsFilteringAndStarsWorkWhenLoadedLast'
```

首次诊断暴露了 O2Lazer → BMS 顺序下 O2Jam 回放落回原生 `LegacyScoreDecoder.Parse` 的问题。补注册后两种顺序的回放读取均通过，BMS 单曲结算图标也已完成客户端验收。旧 BMS 成绩/replay 的完整导入、真实客户端里 BMS 与 O2Jam 的完整游玩→结算→回放链路，以及未来 BMS/宿主版本仍需单独验收；这张矩阵不声称覆盖这些流程。
