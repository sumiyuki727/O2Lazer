# 构建、验证与维护

维护状态（2026-10-06）：现行构建/验证指引；当前结果与旧测试/部署记录分开维护。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-06。当前 `master` 以 osu!lazer **2026.1005.0** 的 Game 与 Mania 二进制为目标，宿主项目 `net10.0`，独立 Core/Formats 项目 `net8.0`；需要 .NET 10 SDK，以及运行独立测试所需的 .NET 8 runtime。`D:/osu`、`D:/osu-framework`、`D:/rulesets` 与 `D:/o2lazer-clean-rewrite` 仅作只读参考。不要混用不同发行版本的 Game/Mania DLL。

## 2026.1005.0 宿主适配

2026-10-06 08:44 JST 最新通知版统一更新计数为去重后的谱面源数，首次更新阶段同样从零过渡至已确认数量。1,064 项常规与 132 项迁移/恢复过滤回归及分层检查通过，已备份安装；后端文件结果统计不变。详见[存储契约](library-persistence-contract.md#更新通知的合并呈现)。

2026-10-06 更新通知呈现版：准备阶段合并为“检查谱面”，读取与匹配连续推进；隐藏空更新阶段，星级首次显示从零过渡至已确认数量。后端流程仍使用回退后的较快实现，1,062 项常规过滤回归及生产语义分层检查通过。定义与验收范围见[存储契约](library-persistence-contract.md#更新通知的合并呈现)。

此前按用户要求于 2026-10-06 08:02 JST 恢复原生难度专用投影版本（DLL `3ECBFD7B…`，此前实测 274 秒），后续仅增加上述进度呈现。部分数字分组和细分计时源码已撤回；回退构建及 132 项迁移/恢复过滤回归通过。文件完整校验与原生难度投影继续保留，数据库未回退。历史性能证据见[存储契约](library-persistence-contract.md#部分数字文件名候选查询)。

历史实验：2026-10-06 部分数字文件名分组查询通过 1,215 项过滤回归及生产语义分层检查；已授权的停机备份只读对照显示两组查询中位数减少约 13% / 23%，但实机整轮从 274 秒变为 286 秒。该方案及其专用冻结基线/比较夹具已撤回，原始报告保留；未将查询改善认定为整轮收益。后续 benchmark 仍必须先询问用户。

2026-10-06 当前基线星级计算改为[原生难度专用投影](library-persistence-contract.md#原生难度专用投影)：从已验证 OJN 直接构造一次性 Mania 谱面，省去游玩样本/小节/源位置数据及第二轮音符复制，仍由原生算法处理默认值与 LN tick。Mod/游玩路径不变，缓存版本不变。1,213 项过滤回归（1,060/130/23）及分层检查通过；日志 projection_mode=native-mania-v2，比较时合看 decode_ms+calculate_ms。未运行 benchmark，整轮提速待正常客户端日志确认。

上述数字文件名查询候选已于用户同意后完成真实停机备份的临时只读对照，并于 2026-10-06 01:01:22（JST）备份安装。有完整数字组的 16 源查询中位耗时 180.1→135.6 ms（约减少 24.7%），无组样本 193.4→192.7 ms；全部匹配结果与冻结 native-link-v1 一致。它不代表整轮提速或实际批次覆盖率，客户端完整性能仍待正常日志确认。明细、备份与 Hash 见上述[候选记录](library-persistence-contract.md#数字文件名候选查询待性能确认)。新的显式对照也属于 Benchmark/LocalDiagnostics，后续再次运行仍须单独同意。

2026-10-06 A04 数字文件名查询候选已构建：仅把完整十个数字文件名的后缀条件合为原生 LIKE[c]，进入候选字典前恢复具体后缀核对，完整路径/归属/冲突保护保持。1,205 项过滤回归（1,052/130/23）及分层检查通过；客户端仍使用之前 DLL。新日志模式为 native-link-digit-v2，增加 path_predicates/path_wildcard_groups；目前只获功能证据，尚无性能结论。部署前先经用户同意执行真实备份只读对照，AGENTS.md 的 benchmark 许可仍适用，见[候选及验证边界](library-persistence-contract.md#数字文件名候选查询待性能确认)。

以下是 2026-10-05 的专项验证/部署历史，当前回归总数见后面的维护状态。

正式版依赖与源码 master 的依赖不同：本轮按安装版发布标签同步 Game/Mania 2026.1005.0
和 Framework 2026.921.1。1339 项过滤回归、26 项补丁安装、BMS 双顺序及全部分层检查通过。
构建路线、原生变更、依赖审计与客户端验收边界见[本轮适配记录](lazer-20261005-compatibility.md)。
适配 DLL 已于 2026-10-05 17:18:55（JST）停机备份安装；客户端验收待进行，无需清空谱面。

## EX 零血完整记录

2026-10-05 用户选定的 EX 零血规则已实现：血量锁零，继续记录原始玩法状态，曲终
原生 F/未通过，保留历史成绩/回放；未来个人纪录聚合排除 F。Core 的血量锁定与
结束游玩意图区分，Host 使用原生结果字段，正式 v5/Realm 无新字段或版本。详情见
[零血契约与证据](zero-life-and-failure-analysis.md)及[存储接口](library-persistence-contract.md#ex-零血成绩与未来个人记录)。

EX 实施当轮 145 项过滤验证通过：Core 状态 33、宿主计分/挑战/NF/回滚/回放 67、临时 Realm
正式回放 23、MS 22；全部源码与语义分层检查通过，无新增例外或补丁。报告在
`.artifacts/zero-life-analysis/tests/ex-zero-life-*.trx`。实机验收重点为归零后计分、锁血、
Jam/药丸、整曲 F、回放倒退、NF/MS。没有运行 benchmark 或修改客户端真实库。

测试 DLL 已于 2026-10-05 19:23:50（JST）停机备份安装，安装记录为
`.artifacts/install-ex-zero-life.json`；构建与安装 SHA-256 一致，客户端验收待进行。

## 当前维护与验证状态

原版客户端对齐阶段已按用户决定结束，其余差异暂缓。当前运行契约为连续不对称
判定、严格 Jam/原始药丸与头判、判前 KS 停旧重播/判后禁用、三项 RD 菜单、tick
误差条、MS 原生速率窗口，以及 EX 完整记录/锁血/F。详见[收尾审查](recent-changes-review.md)。
原版证据不替代显式用户政策；未逐项反馈的视觉/听觉场景不会因阶段关闭自动记为通过。

2026-10-06 原版对齐收尾当轮过滤验证：Formats 42、Core 91、检查器 15、宿主常规 1052、迁移/恢复
127、正式 replay 导入 23，共 1350；另有两个独立 BMS 顺序，共 1352 项通过，无跳过。
全部源码/语义分层检查通过，六条精确例外、两项 partial 保持。证据快照位于
`.artifacts/recent-changes-review`，本轮不安装 DLL、不改真实库、不运行 benchmark。
上述 EX 部署属于历史记录；当前曲库更新冻结、安装与验证见 [最新复核](library-refresh-release-review.md)。
算法菜单、音频与误差条的历史部署分别见对应专项页。

导入流水线、写入/查询字段口径与旧性能实验统一保留在[存储契约](library-persistence-contract.md)。
当前准备提前一批，单线程计算已提交源，收藏夹同步后补齐；缓存事务最多十六源，
进度只平滑已提交计数。A04 完整客户端矩阵与后段性能边界仍在，不因原版阶段结束关闭。

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

架构检查从 MSBuild 获取实际源码、预处理符号与 ReferencePath，用 SDK 自带 Roslyn 分析生产和音频诊断配置。Game/Mania 版本必须匹配 `Directory.Build.props`；完整报告为 `.artifacts/architecture/dependencies.json`。矩阵和六条精确例外见[架构约定](clean-rewrite-architecture.md#允许依赖矩阵与例外)，可执行策略为 `scripts/architecture-policy.json`。新增源码必须归属已知层，解析错误、越层、额外 partial 声明和过期例外均失败。

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

所有测试命令必须带 `--filter`。Benchmark 与全曲库扫描不属于常规验证；benchmark 按 AGENTS.md 先询问并取得用户同意。真实谱面兼容测试应从 `E:/o2jam` 选有限、可复现的代表样本，不复制私有谱面入仓库。边界测试可证明状态/对象行为，但无法替代客户端里的皮肤、动画、音量、暂停或多 ruleset 载入验收。

本地化修改使用 `--filter 'FullyQualifiedName~O2JamLocalisation'`，同时覆盖资源规则及原生语言绑定。A13 定向验证为 26 项本地化和 66 项相关契约，共 92 项；新增 16 项测试已由原有 `.Normal.` 过滤器纳入下方基线。动态文本与固定名称的区分见[本地化指南](localisation.md#文本所有者与固定字符串)。

历史基线及每轮部署见[存储契约](library-persistence-contract.md)、
[宿主适配](lazer-20261005-compatibility.md)和[当前审查](recent-changes-review.md)。
不把旧测试数量、旧安装 Hash 或某轮未完成的实测作为当前验证结果。

## 可选本地诊断

### 历史合成库对照

历史合成夹具已由真实停机备份对照替代并删除。2026-10-02 经用户同意运行的报告仍保存在 `.artifacts/test-results/native-set-query-comparison.trx`：6,000 个 O2Lazer set、2,000 个其他 set，每个三个难度、两个虚拟文件引用；拆分方案约 256 ms，联合基线约 325–328 ms，仅后移归属约 386–390 ms。它只测 set 候选查询，未代表大量实际文件引用；后续实机 3.55 秒的回退证明该模型不足，不能作为提速或部署依据。原始样本与撤回记录见[性能证据](library-persistence-contract.md#下一轮查询候选的验证边界)。

### 真实停机备份查询对照

`O2JamLegacyLibraryMigrationTest.ReadOnlyQueryBenchmark.cs` 属于显式 `Benchmark` / `LocalDiagnostics`，普通回归继续排除 `LocalDiagnostics`。只在用户明确同意后按精确方法名执行，编译不会运行它。`O2JAM_QUERY_BENCHMARK_REALM` 必须指向仓库 `.artifacts/client-backups` 中已有的停机备份，拒绝客户端原库路径；复制到独立临时目录后用原生 Realm 的 `IsReadOnly=true` 打开，不通过 RealmAccess 打开该副本，避免启动清理或迁移。成功对照前后核对原备份和副本 SHA-256。

准备阶段只在副本读取所有者/路径普通值，从外部目录最多枚举 8,192 个 OJN 路径，并读取最多 32 份、每份不超过 16 MiB 的 OJN；不用自动目录编码采样，不读 OJM、不写谱面或成绩。16 个未登记路径与 16 个已登记源，各预热一次、轮换顺序测三次；测量完整查找而非仅 set_query，逐次核对路径 ID、内容所有者和 set Hash 与冻结的联合基线一致。准备/查询共用 120 秒预算，超时不启动下一查询；不能中断正在执行的原生查询。活动源或样本不足时明确给出未得结论，不能把退出码零或 skipped 当作性能通过。

```powershell
# 用户同意后单独执行；先完成普通构建，按本地已有停机备份选路径。
$env:O2JAM_QUERY_BENCHMARK_REALM = Join-Path $PWD '.artifacts/client-backups/a04-5-split-set-lookup-20261002-170259/client.realm'
$env:O2JAM_QUERY_BENCHMARK_CORPUS = 'E:/o2jam'
& .artifacts/dotnet/dotnet.exe test osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj `
  -c Release --no-build "-p:OsuBinaryDirectory=$lazerBinaries" `
  --filter 'FullyQualifiedName=osu.Game.Rulesets.O2Lazer.Tests.Normal.Clean.O2JamLegacyLibraryMigrationTest.CompareLookupOnReadOnlyClientBackup' `
  --logger 'trx;LogFileName=readonly-query-comparison.trx' --results-directory .artifacts/test-results
```

本次成功对照约 6 秒完成，实际库 426,151 个文件引用，查询中位数减少约 50–54%；完整样本、最初全待删除备份的准备失败及限制见[原生链接重设计](library-persistence-contract.md#原生链接查询重设计)。旧路径兼容由功能测试覆盖，该备份旧回退记录为零；未登记路径可能是副本。该只读测量不包括写入、提交及客户端竞争，不能换算成整轮更新速度。936 项功能回归及全部分层检查通过后，最终构建已于 20:03:34（JST）备份安装；Hash、停机备份和未完成验收同见该记录，不恢复数据库或要求清除谱面。

### 既有本地诊断与客户端验证

2026-10-03 难度缓存协调通过 949 项过滤功能回归（819/109/21）及实际 BMS DLL 的两种载入顺序检查，分层检查没有新增例外。当前基线难度计算仅读取已提交 OJN，原生后台在同一星数事务保存完整 MaxCombo/版本，后置处理重查缓存后跳过已完成源。正常日志新增本阶段完成耗时和实际计算/跳过计数；Mod 难度预处理及音频生命周期保持原路径。具体行为、原生缺口、可能剩余的同时计算及性能边界见[补算协调](library-persistence-contract.md#原生后台与后置补算协调)。本轮未运行 benchmark；实际提速仍需客户端日志确认。

刷新已改为先提交可玩谱面、同步收藏夹，再补算缺失的 Mania 星数/MaxCombo；完整缓存直接跳过。原生按需计算仍可处理尚未补算的选中谱面，导入投影与缓存版本不再相互阻挡。819 项宿主常规、76 项临时库与 21 项正式 replay（916 项）分次过滤通过，最后 55 项定向复测及分层检查也通过；测试版已安装。开发顺序、取消/恢复、存储身份保护和验证结果见[星数延后计算](library-persistence-contract.md#星数延后计算)。原分流版的客户端日志已确认两轮完成：首次 568 秒（5,075 源需重写），第二轮 17 秒（0 源需重写）；该记录不能代替新顺序的首次导入实测。

生产音频追踪默认关闭；仅为诊断构建时设置 `-p:O2JamSyncDiagnostics=true`，参见[音频诊断指南](audio-sync-diagnostics.md)。对应测试属于 `LocalDiagnostics`/`[Explicit]`，只用精确方法名与本机输入运行。相关环境变量：`O2JAM_CORPUS_PATH`（外部曲库）、`O2JAM_REPLAY_DIAGNOSTIC_PATH`（已有 v5 replay）、`O2JAM_DIAGNOSTIC_REALM`（只读诊断库）、`O2JAM_DIAGNOSTIC_SKIN`（皮肤 GUID）、`O2JAM_ENCODING_AUDIT_PATH`（可选审计输出）。不上传私人数据库、回放或受版权保护的谱面。

A04-5 的有限真实库验证只依赖外部源路径，不需要客户端数据库或私人 replay。先构建，再单独运行以下精确过滤器；固定三个样本及当前证据见[存储契约](library-persistence-contract.md#a04-5-活动文件与有限真实样本)。不要将过滤器替换成整个 Corpus/LocalDiagnostics 类别。

```powershell
$env:O2JAM_CORPUS_PATH = 'E:/o2jam'
& .artifacts/dotnet/dotnet.exe test osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj `
  -c Release --no-build "-p:OsuBinaryDirectory=$lazerBinaries" `
  --filter 'FullyQualifiedName~O2JamReplayImportTest.BoundedRealSourcesSurviveRefreshMediaAndFormalReplayRestart'
```

BMS 共存诊断使用 `O2JAM_BMS_RULESET_PATH` 指向另行安装/构建的 DLL。两个载入顺序分别用 `O2JamBmsCompatibilityTest.BmsFilteringAndStarsSurviveO2LazerInitialisation` 与 `BmsFilteringAndStarsWorkWhenLoadedLast` 精确过滤、分进程运行；另有 `O2JamRulesetCoexistenceTest.SharesOverlappingDifficultyStatisticsPatchWithBmsHarmony`。这些测试已在 2026.1005.0 宿主与已安装 BMS 2026.920.0.0 上重新验证，还覆盖 O2Jam replay 读取；版本、结果和限制见[共存矩阵](bms-coexistence.md)。它们不保证未来第三个 ruleset 或全部客户端流程。

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

2026-10-02 刷新分流后的相关过滤回归累计 904 项：宿主常规 813 项及新增会话场景 1 项、迁移/恢复 69 项、正式 replay 21 项；最后定向重跑队列/源流程 26 项及实际后端修复 1 项，不重复累计。普通更新保留恢复时间戳后的内容变更检测，缺失文件自动进入原生 Add，深度修复仅供内部维护调用。所有边界及真实依赖检查通过，无新增例外。DLL 已备份安装，版本、Hash 和备份位置见[当前安装记录](library-persistence-contract.md#当前分流验证与安装)；客户端连续两次刷新及完整 A04-5 验收仍待用户确认，本轮未提交。


2026-10-05，COOL 基准和 MS 变速窗口经用户确认后，临时 UR 监听源码、构建
开关和专用诊断测试已移除。本轮 BAD 配色和相关 HUD/变速过滤回归 120 项，
BMS 独立双顺序各一项、检查器 15 项通过；历史报告及客户端状态见
[误差条显示验收](hit-error-display-validation.md)。


2026-10-05 后续：Core 改为早含/晚不含的连续区间 `[-6,7)`、`[-18,19)`、
`[-25,26)`；Integration 使用更大的毫秒包络保留输入与生命周期，Presentation
按早侧 OD7 黄300半宽、晚侧同刻度多 1 tick 调整原生色区。396 项过滤回归通过，
无新增分层例外。旧未标记 v5 测试回放按新规则重判；新写入增加 `judgement_version`。
安装和验收以[当前显示报告](hit-error-display-validation.md)为准。
