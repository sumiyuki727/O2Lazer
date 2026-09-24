# BMSRuleset 共存验证

验证日期：2026-09-24。目标宿主为 osu!lazer 2026.921.0；实际安装并参与测试的 BMSRuleset 程序集版本为 **2026.920.0.0**。`D:/rulesets/BmsRuleset` 只读源码检出仍对应较早的 2026.804 配置，不能代替已安装 DLL 的行为依据。升级 BMS 或宿主后须重新执行本页矩阵。

## 补丁边界

两个 ruleset 都合入 Harmony 时，同一原生方法可能被两份 Harmony 运行时改写。`Host/Compatibility/O2JamBmsHarmonyCompatibility` 是**明确限定 BMSRuleset** 的兼容适配器：BMS 已加载时，O2Lazer 将共用方法的钩子注册到 BMS 的 Harmony；BMS 后加载时，在程序集载入事件中补注册。回滚按 O2Lazer 自己的 Harmony ID 撤销，不撤销 BMS 的 ID。该类不是未来所有 ruleset 的通用补丁协调器；添加第三个 ruleset 时需重新审视运行时、所有者和载入顺序。

参与补注册的 O2Lazer 入口：`BeatmapTitleWedge.DifficultyDisplay.updateCountStatistics`、`DifficultyIcon.getRulesetIcon`、`Player.ImportScore`、`ScoreImporter.GetScore`、`ScoreImporter.CreateModel`、`ScreenStack.Push`。这些仍是宿主私有方法；原生的 `DifficultyIcon` 对社区 ruleset 的负在线 ID 返回问号，原生 replay 导入也没有满足 O2Jam 存档格式的公开扩展点，故当前无法移除相应补丁。`O2JamWorkingBeatmapHook` 使用不同的 manager 入口，BMS 的包装器使用自己的缓存入口；测试验证两者未互相替换。

## 当前版本的测试矩阵

两个顺序**分别在独立测试进程**运行。测试使用临时目录、临时 Realm 和合成 BMS/OJN 数据，不触碰客户端用户库。

| 顺序 | BMS 谱面与星级 | 单曲结算入口 | O2Jam replay 读取 | 共用补丁 |
|---|---|---|---|---|
| BMS → O2Lazer | 18 个物件；星级前后均 `0.23321105733995243` | 原生 `SoloResultsScreen` 被替换为 BMS 自定义结算页 | 通过 | 统计、图标及 `ScreenStack.Push` 均使用 BMS Harmony |
| O2Lazer → BMS | 同上 | 同上 | 通过 | BMS 载入后补注册，验证通过 |

安装版 BMS 没有旧源码中的 `BmsDifficultyIconPatcher`；因此原测试要求它在原生 `DifficultyIcon` 内返回 `BmsRulesetIcon` 是过时断言。新测试以安装版在 O2Lazer 初始化前的实际图标类型为基线，并要求之后不改变。此结论只针对原生 `DifficultyIcon` 入口。安装版 BMS 的单曲结算页使用自己的 `BmsResultDifficultyIcon`，其中直接创建 `BmsRulesetIcon`；如果结算时显示 `?`，说明可能落回了原生结算页，不能据原生图标基线认定为 BMS 设计。实际发现 O2Lazer 的编辑器入口保护和 BMS 的结算页替换都补丁到 `ScreenStack.Push`；现在两者共享 BMS Harmony，并在双顺序测试中断言推入原生单曲结算页会被替换。安装修复版后，用户已在客户端确认 BMS 单曲结算图标显示正常。

可复现命令（`$lazerBinaries` 指向匹配的 2026.921.0 安装）：

```powershell
$env:O2JAM_BMS_RULESET_PATH = 'D:/osu-lazer/rulesets/osu.Game.Rulesets.BmsRuleset.dll'
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
$project = 'osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj'
dotnet test $project -c Release "-p:OsuBinaryDirectory=$lazerBinaries" --filter 'FullyQualifiedName~O2JamBmsCompatibilityTest.BmsFilteringAndStarsSurviveO2LazerInitialisation'
dotnet test $project -c Release "-p:OsuBinaryDirectory=$lazerBinaries" --filter 'FullyQualifiedName~O2JamBmsCompatibilityTest.BmsFilteringAndStarsWorkWhenLoadedLast'
```

首次诊断暴露了 O2Lazer → BMS 顺序下 O2Jam 回放落回原生 `LegacyScoreDecoder.Parse` 的问题。补注册后两种顺序的回放读取均通过，BMS 单曲结算图标也已完成客户端验收。旧 BMS 成绩/replay 的完整导入、真实客户端里 BMS 与 O2Jam 的完整游玩→结算→回放链路，以及未来 BMS/宿主版本仍需单独验收；这张矩阵不声称覆盖这些流程。
