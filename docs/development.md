# 构建、验证与维护

更新：2026-10-01。当前 `master` 以 osu!lazer **2026.921.0** 的 Game 与 Mania 二进制为目标，宿主项目 `net10.0`，独立 Core/Formats 项目 `net8.0`；需要 .NET 10 SDK，以及运行独立测试所需的 .NET 8 runtime。`D:/osu`、`D:/osu-framework`、`D:/rulesets` 与 `D:/o2lazer-clean-rewrite` 仅作只读参考。不要混用不同发行版本的 Game/Mania DLL。

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

脚本先运行模块、存储、计分三项源码检查，再运行 `scripts/check-architecture.ps1` 的真实类型/成员依赖分析；随后对格式、核心、检查器和宿主常规测试使用明确 `--filter`，并将旧库迁移与 replay 导入放在独立测试进程。独立项目/程序集边界及合并后的公开 API 另由测试验证。Realm 生命周期混跑曾产生原生事务断言，临时结算测试还需避免把原生通知调度交给已结束的 NUnit 异步上下文；不要把这些过滤器合为一次无筛选运行。测试报告写入忽略的 `.artifacts/test-results`。2026-09-30 A12 验证：Formats 41、Core 55、检查器 15、宿主常规 752、旧库迁移 5、replay 导入 8，合计 876 项通过；这是自动化基线，不等于实机端到端验收。

架构检查从 MSBuild 获取实际源码、预处理符号与 ReferencePath，用 SDK 自带 Roslyn 分析生产和音频诊断配置。Game/Mania 版本必须匹配 `Directory.Build.props`；完整报告为 `.artifacts/architecture/dependencies.json`。矩阵和五条精确例外见[架构约定](clean-rewrite-architecture.md#允许依赖矩阵与例外)，可执行策略为 `scripts/architecture-policy.json`。新增源码必须归属已知层，解析错误、越层、额外 partial 声明和过期例外均失败。

单独查看依赖报告：

```powershell
./scripts/check-architecture.ps1 -OsuBinaryDirectory $lazerBinaries
# 调查尚未满足矩阵的修改时可使用 -ReportOnly；解析错误仍失败。
```

`.github/workflows/architecture.yml` 为 push/PR 配置了独立模块、源码边界及语义检查器测试。完整宿主依赖分析需要仓库变量 `OSU_BINARY_ARCHIVE_URL` 指向匹配版本的可信 Game/Mania 二进制 ZIP，归档必须只有一个包含这两个 DLL 的目录。变量未设置时 CI 摘要明确报告宿主分析未执行；不能把独立测试成功视为全宿主检查成功。本轮在本机匹配二进制上完成全图检查，尚未配置该远程变量或执行远程 CI。检查器是开发工具，不增加 ruleset 运行依赖。

单项验证示例：

```powershell
dotnet test osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj -c Release `
  "-p:OsuBinaryDirectory=$lazerBinaries" `
  --filter 'FullyQualifiedName~O2JamDifficultyTransitionTest'
```

所有测试命令必须带 `--filter`。Benchmark 与全曲库扫描不属于常规验证，执行前需明确决定。真实谱面兼容测试应从 `E:/o2jam` 选有限、可复现的代表样本，不复制私有谱面入仓库。边界测试可证明状态/对象行为，但无法替代客户端里的皮肤、动画、音量、暂停或多 ruleset 载入验收。

本地化修改使用 `--filter 'FullyQualifiedName~O2JamLocalisation'`，同时覆盖资源规则及原生语言绑定。A13 定向验证为 26 项本地化和 66 项相关契约，共 92 项；新增 16 项测试已由原有 `.Normal.` 过滤器纳入下方基线。动态文本与固定名称的区分见[本地化指南](localisation.md#文本所有者与固定字符串)。

2026-10-01 A04-2 更新完整脚本基线：Formats 41、Core 55、检查器 15、宿主常规 797、旧库迁移 20、正式 replay 导入 21，合计 949 项通过；全部源码/语义分层检查通过。临时库覆盖投影/媒体、版本和源快照保护，以及三难度/旧 set Hash/搬家、成绩与文件、空槽位、MD5 集合和缺失/歧义保护；正式 v5 通过原生导入器验证唯一一致身份及其他 ruleset 同哈希保护。详细边界见[存储设计与验收](library-persistence-contract.md#实施顺序与验收矩阵)。当前 AutoMapper 告警保留；不把本轮自动化当成客户端或两事务文件恢复验收。

## 可选本地诊断

生产音频追踪默认关闭；仅为诊断构建时设置 `-p:O2JamSyncDiagnostics=true`，参见[音频诊断指南](audio-sync-diagnostics.md)。对应测试属于 `LocalDiagnostics`/`[Explicit]`，只用精确方法名与本机输入运行。相关环境变量：`O2JAM_CORPUS_PATH`（外部曲库）、`O2JAM_REPLAY_DIAGNOSTIC_PATH`（已有 v5 replay）、`O2JAM_DIAGNOSTIC_REALM`（只读诊断库）、`O2JAM_DIAGNOSTIC_SKIN`（皮肤 GUID）、`O2JAM_ENCODING_AUDIT_PATH`（可选审计输出）。不上传私人数据库、回放或受版权保护的谱面。

BMS 共存诊断使用 `O2JAM_BMS_RULESET_PATH` 指向另行安装/构建的 DLL。两个载入顺序分别用 `O2JamBmsCompatibilityTest.BmsFilteringAndStarsSurviveO2LazerInitialisation` 与 `BmsFilteringAndStarsWorkWhenLoadedLast` 精确过滤、分进程运行；另有 `O2JamRulesetCoexistenceTest.SharesOverlappingDifficultyStatisticsPatchWithBmsHarmony`。这些测试在 2026.921.0 宿主与已安装 BMS 2026.920.0.0 上还覆盖 O2Jam replay 读取；版本、结果和限制见[共存矩阵](bms-coexistence.md)。它们不保证未来第三个 ruleset 或全部客户端流程。

## 依赖与提交边界

### 宿主依赖审计 A16

2026-10-01 核对当前宿主二进制和依赖清单：`osu.Game.dll` 为 2026.921.0.0，`osu!.deps.json` 声明 AutoMapper 13.0.1，其程序集身份为 13.0.0.0。原生 `RealmObjectExtensions` 实际引用 `MapperConfiguration` 的单参数构造；O2Lazer 刷新已有谱面元数据时通过原生 `Detach()` 复制结果。测试项目的 AutoMapper 包用于满足这个运行依赖，本仓库没有另建 Mapper，也没有把它合入交付 DLL。客户端仍使用自己的 AutoMapper 副本，因此不能把风险描述为“仅测试环境存在”。`PrivateAssets=all` 只限制包依赖传播，不是安全隔离。

NU1903 对应 [GHSA-rvv3-g6hj-g44x](https://github.com/advisories/GHSA-rvv3-g6hj-g44x)：深层对象图映射可能因无默认递归深度限制而耗尽栈，公告的修复版本为 15.1.1 与 16.1.1。原生 Beatmap/BeatmapSet 脱管映射已有 `MaxDepth(1/2)`，O2Lazer 使用固定的原生模型，不接收任意类型对象图；这是当前使用范围的依据，不代表已证明整个宿主不受影响。没有运行使进程栈溢出的攻击样本。

[AutoMapper 15 升级说明](https://docs.automapper.io/en/stable/15.0-Upgrade-Guide.html)明确改变 `MapperConfiguration` 构造签名并增加许可要求。只替换测试包不能同步修改现有宿主的构造调用；移除包也会缺少原生 Realm 的运行依赖。本轮保留 13.0.1 和可见告警，不加 `NoWarn`、不关闭审计、不为绕开告警复制原生脱管实现。A16 保留为宿主依赖风险，等待兼容宿主更新或明确的后端替换方案后处理。

在线审计命令（先构建/还原当前项目，确保 assets 与引用一致）：

```powershell
dotnet list osu.Game.Rulesets.O2Lazer.slnx package --vulnerable --include-transitive --format json --no-restore
```

本次查询覆盖解决方案 8 个项目已还原的 NuGet 依赖图，唯一报告为 AutoMapper 13.0.1。它不覆盖通过 `<Reference>` 引用的整个 lazer 二进制依赖清单，也不等于所有客户端依赖安全。原始包报告与宿主构造引用证据保存在忽略的 `.artifacts/dependency-audit/a16-*.json`。先前的 NU1900 是历史查询失败，本次查询成功；以后查询失败仍应记录为审计不完整，不能解释为没有漏洞。

93 项定向过滤回归通过：曲库写入、结算/选歌成绩、计分、会话和 replay 契约 80 项，独立进程旧库迁移 5 项及正式 replay 导入 8 项。仅使用临时库，未修改用户真实库或更换包版本。这些验证证明当前路径兼容，不能证明漏洞已修复。

宿主升级时先核对 Game/Mania 版本、宿主依赖清单、AutoMapper 实际调用 API 和测试包，再查询全部已还原包的公告；最后按上述分进程规则复测 Realm/成绩/replay。不要把单独升级测试依赖得到的成功当作已验证当前客户端。

源码责任见[当前架构](clean-rewrite-architecture.md)，本轮待处理项见[审查清单](architecture-audit.md)，阶段跟踪见[路线图](refactor-roadmap.md)，补丁必要性与失败策略见[补丁清单](compatibility-patches.md)。分层重构基线已提交为 `2ae294c`；之后按问题项分别检查 diff、测试、资源键、持久化身份、私有数据和生成产物。保留旧 HUD/皮肤序列化契约与 v5 replay 格式；源码目录和命名空间的整理不得悄悄改变它们。

独立项目公开 API 使用 `O2Jam.Core`、`O2Jam.Formats.Ojn` 与 `O2Jam.Formats.Ojm`。格式到原生谱面的工厂/缓存使用 `osu.Game.Rulesets.O2Lazer.Integration.Formats.*`；引用这两组 API 需分别导入命名空间。新宿主类型按层和功能命名；既有功能命名空间及必须保留的序列化身份见[命名约定](clean-rewrite-architecture.md#命名空间与持久化身份)，不要把历史 `.UI` 或 `.Scoring` 当作单一责任层。
