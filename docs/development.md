# 开发与验证

更新：2026-10-14。当前目标是 osu!lazer/Game/Mania 2026.1005.0、Framework 2026.921.1。
源码、正式构建、已安装测试包及实际验收范围见[当前构建验收](current-build-validation.md)和
[维护收尾](maintenance-closeout-2026-10-14.md)。生产层级与新增功能入口见
[架构约定](clean-rewrite-architecture.md)；行为以[玩法规范](o2jam-behaviour-spec.md)及专项契约为准。

## 2026.1005.0 宿主适配

Game/Mania 的程序集版本与官方发行标签不是同一个字段。
当前验收宿主对应[官方 2026.1005.0-lazer](https://github.com/ppy/osu/releases/tag/2026.1005.0-lazer)，
提交为 9a10d935；正式参考与本机 Game、Mania、Resources、Framework 字节一致。
不要用版本名称相近的 Tachyon 包或最新发行替代当前引用。

生产修改只在 D:/o2lazer 的 master。D:/osu、D:/osu-framework、D:/rulesets、
D:/o2jam-lazer 和 D:/o2lazer-clean-rewrite 仅作只读参考；构建使用现成 DLL，不构建参考仓库。
宿主更新时按[补丁清单](compatibility-patches.md)逐项复核公开入口及私有接缝，再确认目标版本。

## 当前维护与验证状态

源一致性/读取缓存、回放和 OJN/OJM 预算、时间图/判定历史、原生音频准备调度、
资源清理及旧设置兼容已实施。预算只约束其负责的输入和物化字节，不是全进程 PCM 限额。
最新一次 Reason HX 回放退出后预览补播通过用户听感确认；
Mania→O2Lazer 偶发选曲滞留仍保留观察，不把未复现写成彻底修复。

维护审查 R01–R12 与路线图 R00–R12 是两套编号。
当前剩余清单见[文档审查](documentation-audit-2026-10-08.md#最新维护顺序2026-10-14)；
暂缓原版功能、SQL/服务器存档和第二真实 ruleset 消费者不自动成为本轮实施队列。
旧诊断方案/逐轮测量仅在[源与资源历史](ojm-source-and-resource-plan.md)保存，
曲库身份、事务与性能记录仅在[存储契约](library-persistence-contract.md)维护。

### 测试版客户端控制

只有显式设置 O2LazerTestBridge=true 才编入测试桥；普通 Debug/Release 和正式发布排除。
[测试桥指南](lazer-test-bridge.md)说明原生选图、模式、预览、启动及普通退出边界。
它没有完整 Player/replay 自动控制或真实声音采集；BMS 自定义选歌页也不在其原生 Solo 边界内。
不得把控制命令接受、日志或时钟就绪当作听感/动画验收。

## EX 零血完整记录

EX 零血锁定、完整计分/回放和曲终 F 为已选契约，NF/MS 保留其并列原生语义。
未来个人记录聚合排除 F，原始成绩保留；没有增加 v5/Realm 字段。
[零血契约](zero-life-and-failure-analysis.md)保存原版证据、实现和历史验证；
早期 DLL 安装与旧测试数只证明对应快照。

## 构建

需要 .NET 10 SDK；独立 Core/Formats 测试也使用 .NET 8 运行时。
在仓库根目录运行：

```powershell
$lazerBinaries = Join-Path $env:LOCALAPPDATA 'osulazer/current'
dotnet build osu.Game.Rulesets.O2Lazer.slnx -c Release "-p:OsuBinaryDirectory=$lazerBinaries"
```

普通输出在 osu.Game.Rulesets.O2Lazer/bin/Release/net10.0/osu.Game.Rulesets.O2Lazer.dll。
构建合入 Core、Formats、Harmony，安装只需该 DLL；客户端正常退出后替换，备份置于 rulesets 之外。
程序集版本由 ruleset csproj 控制，宿主兼容版本由 Directory.Build.props 控制。
编译不是发布新 tag；私有验证输出与发布资产须分别记录。

可用 OsuGameProjectPath、OsuManiaAssemblyPath 指向匹配 DLL；
虽然属性名含 Project，它现在不接受项目路径。UseLocalOsu=false 仅在匹配 NuGet 引用可用时使用。
独立模块验证不需要 lazer 安装。交付检查可用 dotnet --artifacts-path 将输出置于独立忽略目录，
防止覆盖已冻结的 DLL/报告；随后核对源码输入、引用、资源和正式包中测试桥排除情况。

## 过滤验证

```powershell
./scripts/verify.ps1 -Scope Portable
./scripts/verify.ps1 -Scope Host -OsuBinaryDirectory $lazerBinaries
# 或一次运行两个范围：
./scripts/verify.ps1 -OsuBinaryDirectory $lazerBinaries
```

| Scope | 验证范围 | 引用要求 |
|---|---|---|
| Portable | 模块/存储/计分源码边界；Formats、Core、架构检查器 | 不需要宿主 DLL |
| Host | 同样的源码边界、实际语义依赖图；常规宿主、Realm、replay、旧设置四组 | 匹配的 Game/Mania/Resources |
| All | 上述七组及实际依赖图 | 同 Host |

常规过滤器排除 Benchmark、LocalDiagnostics、Isolated。
Realm 迁移、正式 replay 导入各自启动进程；八项旧设置兼容仅按方法名称显式选择，
继续排除 Benchmark/LocalDiagnostics，不与常规 Realm 生命周期混跑。
Host 构建显式关闭测试桥和同步诊断；后续组复用首组构建。
-NoBuild 只用于同配置、同引用的既有构建，不能靠传属性改变已经生成的 DLL。
任一 dotnet 失败终止后续组，并恢复调用者目录。报告在忽略的 .artifacts/test-results。

架构分析从 MSBuild 获取实际 Compile/ReferencePath，使用 SDK Roslyn 核对类型/成员归属、
预处理配置、精确例外与 partial；不能用目录或历史 namespace 代替真实依赖。
Game/Mania 版本必须匹配 Directory.Build.props；策略为 scripts/architecture-policy.json。

```powershell
./scripts/check-architecture.ps1 -OsuBinaryDirectory $lazerBinaries
dotnet test osu.Game.Rulesets.O2Lazer.Tests/osu.Game.Rulesets.O2Lazer.Tests.csproj -c Release "-p:OsuBinaryDirectory=$lazerBinaries" --filter 'FullyQualifiedName~O2JamDifficultyTransitionTest&TestCategory!=Benchmark&TestCategory!=LocalDiagnostics&TestCategory!=Isolated'
```

所有 dotnet test 必须带 --filter。新 benchmark/真实库诊断必须按 AGENTS.md 单独获授权，
不混入常规测试。当前七组 1941 项的具体数量和参考身份见[收尾记录](maintenance-closeout-2026-10-14.md)。
这些功能测试不证明设备首音、吞吐量或完整玩家视觉矩阵。

### GitHub 完整宿主 CI

[architecture.yml](../.github/workflows/architecture.yml) 先调用 Portable，再在存在参考 URL 时调用 Host。
配置两个仓库变量：OSU_BINARY_ARCHIVE_URL 和 OSU_BINARY_ARCHIVE_SHA256。
URL 指向可信 ZIP（官方 Windows 完整 nupkg 也是 ZIP），摘要必须是对应归档的 64 位 SHA-256。
先校验下载字节，再解压；归档恰好一个目录同时含 Game、Mania、Resources。
缺摘要、摘要不符、缺文件或版本不符直接失败；缺 URL 时摘要明确列出未执行的宿主范围。

本轮固定[官方完整包](https://github.com/ppy/osu/releases/download/2026.1005.0-lazer/osulazer-2026.1005.0-lazer-full.nupkg)，
官方 SHA-256 为 d23d47d63ccb9c7d157b25cf5ac465d0e4d4cb17bab67c83f1ac112afd3e6cfa。
更新宿主时先重新核对发布身份和实际引用，再同步两个变量。
本轮变量已配置，七组 1941 项完整远程回归已通过；具体提交、运行链接和报告核对见[收尾记录](maintenance-closeout-2026-10-14.md)。

摘要读取实际 TRX 总数/通过/失败/未执行数。
只上传七组 TRX 与语义报告，保留 14 天；允许指定的隐藏 .artifacts 路径，
失败也保存已有结果，不上传宿主参考包或其他私人证据。

## 可选本地诊断

Benchmark、LocalDiagnostics、BMS/私有库和真实停机备份对照都不属于常规验证。
先准备输入数量、读取/修改边界、截止时间、退出和恢复方案，再向用户取得该轮授权。
以前一次授权或普通功能测试时间，均不能替代新 benchmark 的授权或性能证据。

### 历史合成库对照

合成查询/操作计数仅证明对应夹具。历史数据与当轮停止条件见[曲库契约](library-persistence-contract.md)，
不能据函数调用次数推算磁盘/客户端吞吐；新的计时对照仍须另获授权。

### 真实停机备份查询对照

仅使用已确认停机备份的临时副本，限定源/查询数量、截止时间与退出条件；
不打开或写入正在使用的客户端 Realm，不清理真实源文件。具体历史方案见曲库契约，
旧授权和过去的秒数不成为新一次实库对照的依据。

历史受控对照、失败与恢复保留在[源与资源方案](ojm-source-and-resource-plan.md)及
[曲库契约](library-persistence-contract.md)。不复制私有谱面/数据库入 Git，不强制结束客户端，
不自动确认存在后台任务的危险退出窗口。桥的限制由[控制指南](lazer-test-bridge.md)负责。
验证 Source/帧 Hash/数据身份只证明对应数据，不推导实际可听声音或所有 Mod/设备行为。

## 依赖与提交边界

### 宿主依赖审计 A16

当前引用宿主自己的共享库。AutoMapper 13.0.1 的原生单参数 MapperConfiguration
与公告修复版本 API 不同，不能只在 ruleset 内替换共享 DLL。
2026-10-13 联网审计记录 AutoMapper 一条、ImageSharp 3.1.11 五条公告；
完整版本、主公告、边界及实际引用见[依赖复查](current-build-validation.md#依赖复查与未关闭风险)。
PrivateAssets=all 不是安全隔离；NuGetAudit=false 的离线构建不是“没有告警”。
不隐藏告警、不运行攻击样本，不用输入预算冒充原生解码器/全宿主安全保证。

提交前核对公开资源/旧枚举身份、正式包排除测试桥、分层、明确过滤的测试、
文档链接和已安装/备份 Hash。源、自动证据、真实客户端反馈及版本范围分别记录。
构建产物和 .artifacts 不提交；保留上一已推送版本及各阶段备份作为回退依据。
