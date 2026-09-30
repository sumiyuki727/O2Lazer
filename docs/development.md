# 构建、验证与维护

更新：2026-09-30。当前 `master` 以 osu!lazer **2026.921.0** 的 Game 与 Mania 二进制为目标，宿主项目 `net10.0`，独立 Core/Formats 项目 `net8.0`；需要 .NET 10 SDK，以及运行独立测试所需的 .NET 8 runtime。`D:/osu`、`D:/osu-framework`、`D:/rulesets` 与 `D:/o2lazer-clean-rewrite` 仅作只读参考。不要混用不同发行版本的 Game/Mania DLL。

## 构建

在仓库根目录运行：

```powershell
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
dotnet build osu.Game.Rulesets.O2Lazer.slnx -c Release "-p:OsuBinaryDirectory=$lazerBinaries"
```

输出为 `osu.Game.Rulesets.O2Lazer/bin/Release/net10.0/osu.Game.Rulesets.O2Lazer.dll`。构建将 Core、Formats 与 Harmony 合入单一 ruleset DLL；仅安装该 DLL，关闭游戏后替换数据目录 `rulesets` 内的旧版，备份放在该目录之外。构建源代码不等于新 release/tag；程序集版本由 ruleset csproj 的 `Version` 控制，宿主兼容版本由 `Directory.Build.props` 控制。

可用 `OsuGameProjectPath`、`OsuManiaAssemblyPath` 指向分开放置的匹配 DLL；属性名虽含 `Project`，现在只接受 DLL。`UseLocalOsu=false` 是匹配 NuGet 包存在时的替代引用路线。独立 Core/Formats 测试不需要 lazer 安装。

## 过滤验证

```powershell
./scripts/verify.ps1 -OsuBinaryDirectory $lazerBinaries
```

脚本先运行 `scripts/check-storage-boundary.ps1` 与 `scripts/check-scoring-boundary.ps1`，随后对格式、核心、宿主常规测试使用明确 `--filter`，并将旧库迁移与 replay 导入放在独立测试进程。计分检查防止 Integration/Scoring 再次引用具体玩法协调器/MS Mod，不是完整的跨层依赖分析。Realm 生命周期在同一 test host 中混跑曾产生原生事务断言，所以不要把这些过滤器合为一次无筛选运行。报告写入忽略的 `.artifacts/test-results`。2026-09-26 验证基线：Formats 39、Core 54、宿主常规 718、旧库迁移 5、replay 导入 8，合计 824 项通过；这是自动化基线，不等于实机端到端验收。之后各问题项的定向验证记在审查清单中。

单项验证示例：

```powershell
dotnet test osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj -c Release `
  "-p:OsuBinaryDirectory=$lazerBinaries" `
  --filter 'FullyQualifiedName~O2JamDifficultyTransitionTest'
```

所有测试命令必须带 `--filter`。Benchmark 与全曲库扫描不属于常规验证，执行前需明确决定。真实谱面兼容测试应从 `E:/o2jam` 选有限、可复现的代表样本，不复制私有谱面入仓库。边界测试可证明状态/对象行为，但无法替代客户端里的皮肤、动画、音量、暂停或多 ruleset 载入验收。

## 可选本地诊断

生产音频追踪默认关闭；仅为诊断构建时设置 `-p:O2JamSyncDiagnostics=true`，参见[音频诊断指南](audio-sync-diagnostics.md)。对应测试属于 `LocalDiagnostics`/`[Explicit]`，只用精确方法名与本机输入运行。相关环境变量：`O2JAM_CORPUS_PATH`（外部曲库）、`O2JAM_REPLAY_DIAGNOSTIC_PATH`（已有 v5 replay）、`O2JAM_DIAGNOSTIC_REALM`（只读诊断库）、`O2JAM_DIAGNOSTIC_SKIN`（皮肤 GUID）、`O2JAM_ENCODING_AUDIT_PATH`（可选审计输出）。不上传私人数据库、回放或受版权保护的谱面。

BMS 共存诊断使用 `O2JAM_BMS_RULESET_PATH` 指向另行安装/构建的 DLL。两个载入顺序分别用 `O2JamBmsCompatibilityTest.BmsFilteringAndStarsSurviveO2LazerInitialisation` 与 `BmsFilteringAndStarsWorkWhenLoadedLast` 精确过滤、分进程运行；另有 `O2JamRulesetCoexistenceTest.SharesOverlappingDifficultyStatisticsPatchWithBmsHarmony`。这些测试在 2026.921.0 宿主与已安装 BMS 2026.920.0.0 上还覆盖 O2Jam replay 读取；版本、结果和限制见[共存矩阵](bms-coexistence.md)。它们不保证未来第三个 ruleset 或全部客户端流程。

## 依赖与提交边界

当前测试项目为匹配宿主二进制而引用 AutoMapper 13.0.1；NuGet 报 NU1903 已知漏洞，相关代码不打包进 ruleset DLL。网络不可用时漏洞审计另报 NU1900；不要把它解释为“没有漏洞”，也不要未经二进制兼容验证擅自替换主版本。宿主升级时重查这两项与其他依赖。

源码责任见[当前架构](clean-rewrite-architecture.md)，本轮待处理项见[审查清单](architecture-audit.md)，阶段跟踪见[路线图](refactor-roadmap.md)，补丁必要性与失败策略见[补丁清单](compatibility-patches.md)。分层重构基线已提交为 `2ae294c`；之后按问题项分别检查 diff、测试、资源键、持久化身份、私有数据和生成产物。保留旧 HUD/皮肤序列化契约与 v5 replay 格式；源码目录和命名空间的整理不得悄悄改变它们。
