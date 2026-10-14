# O2Lazer 文档索引

更新：2026-10-14。当前目标 Game/Mania 2026.1005.0、Framework 2026.921.1。
[本轮维护收尾](maintenance-closeout-2026-10-14.md)负责当前源码交付、1941 项本机过滤验证、
固定官方 CI 参考、1941 项完整远程通过及剩余清单；[客户端验收](current-build-validation.md)负责实际安装/备份身份、
Reason 回放退出补播、NoMS/MS 游玩及 Mod 视觉已获用户确认通过；
偶发选曲和原生 Realm 退出等待保留观察。两者分别记录自动证据与用户反馈。

源一致性/读取缓存、格式/回放预算、时间图/历史恢复、原生音频调度与资源清理已实施。
原生 PCM 峰值、宿主依赖告警、完整实机/设备矩阵仍有边界，不把格式预算或局部通过写成全部关闭。
[维护问题总表](documentation-audit-2026-10-08.md)与[路线图](refactor-roadmap.md)使用不同 R 编号；
原始复现保留在[审查报告](refactor-review-2026-10-06.md)，逐轮性能/失败证据留在专项历史页。

原版对齐阶段已经按用户决定结束，未选功能继续暂缓。[1.0.0-test3](release-1.0.0-test3.md)
按用户后续授权发布本轮维护快照，正式 DLL 排除测试 bridge 与可选计时探针；test2 的资产和历史验收保留。
未来新功能先依据层级和真实消费者定义范围。

## 如何选择文档

运行规则看玩法规范；改代码看架构和问题清单；写入/迁移看持久化契约；构建/测试看
开发说明。各页的历史日期、测试数与 Hash 仅证明对应版本，不是全局最新结果。
用户明确政策高于参考客户端；原版指令或资产名称不自动成为新功能需求。

| 文档 | 所负责的内容与状态 |
|---|---|
| [2026-10-08 文档审查](documentation-audit-2026-10-08.md) | 39 份既有文档的处理结果；源码未完成项、验证缺口及暂缓功能的合并清单。 |
| [2026-10-14 维护收尾](maintenance-closeout-2026-10-14.md) | 当前源码/正式产物、1941 项过滤回归、CI 固定参考与实际远程状态、剩余范围。 |
| [当前构建与客户端验收](current-build-validation.md) | 各次正式/验收包、历史回归和实际控制/读取/重启；最终 NoMS/MS 游玩、回放和 Mod 视觉通过，偶发选曲/退出等待观察及完整矩阵保留。 |
| [R03 / R04 验收记录](r03-r04-acceptance.md) | 两轮真实曲库更新、预览控制、正式回放导入导出和重启；NoMod/MS 新成绩保存与重启、回放前后跳转和数据保留；隔离异常验证和未覆盖边界。 |
| [2026-10-06 维护更新](maintenance-update-2026-10-06.md) | 收藏归属与开关通知、超长 LN 补绘、无 rg 的 CI 检查及本轮验证。 |
| [2026-10-06 重构后审查与待确认清单](refactor-review-2026-10-06.md) | 独立审查基线和候选方案；R01 已修复，后续状态见页首及本轮文档审查。 |
| [全项目分层审查与问题清单](architecture-audit.md) | 现行问题清单；原版范围已结束；系统/依赖问题保持。 |
| [O2Jam 音频同步可选诊断构建](audio-sync-diagnostics.md) | 可选操作指南；音频诊断默认关闭，与已移除的误差条监听分开。 |
| [Ruleset 共存与补丁协调](ruleset-compatibility-design.md) | 通用协调器、只读诊断、BMS 专属边界、第三方适配流程及本轮验收收尾。 |
| [BMSRuleset 共存验证](bms-coexistence.md) | 现行共存边界；指定 BMS 两种独立载入顺序通过，完整客户端链路边界保留。 |
| [O2Lazer 当前架构](clean-rewrite-architecture.md) | 现行层级与扩展指引；显式用户政策优先；六条精确例外及两个 partial 仍存在。 |
| [随机轨道变换契约](column-randomisation.md) | 现行随机契约；三项入口；旧算法隐藏保留，随机工作已结束。 |
| [补丁清单与失败策略](compatibility-patches.md) | 现行补丁与原生缺口清单；当前 26 项；EX 锁血/F 使用原生结果接口，没有新增补丁。 |
| [构建、验证与维护](development.md) | 现行构建/验证指引；当前结果与旧测试/部署记录分开维护。 |
| [DPJAM 压缩包行为对照](dpjam-client-comparison.md) | 历史辅助证据；不覆盖韩服主参考或后续明确用户政策。 |
| [误差条显示与验收记录](hit-error-display-validation.md) | 现行几何契约及历史证据；旧监听/旧安装 Hash 不表示当前运行版。 |
| [韩服 O2JamO2 / Classic 与 O2Lazer 行为对照](korean-client-comparison.md) | 主参考与差异记录；已选对齐阶段结束，其余差异暂缓，不宣称完全复刻。 |
| [osu!lazer 2026.1005.0 适配记录](lazer-20261005-compatibility.md) | 版本专项历史证据；宿主目标仍为 2026.1005.0，后续 EX 安装不由本页旧 Hash 表示。 |
| [A04：曲库与成绩的持久化身份](library-persistence-contract.md) | 现行身份/更新/恢复契约；F 原始记录保留；未来个人记录按 Rank 排除 F，性能实验另标历史。 |
| [本地化模块与新增语言](localisation.md) | 现行本地化指引；随机七项固定英文是用户例外，仍统一走资源入口。 |
| [O2Jam gameplay behaviour specification](o2jam-behaviour-spec.md) | 现行玩法规范；剩余原版项暂缓；EX 完整记录/锁血/F 已实现。 |
| [O2JamO2 安装包行为对照](o2jamo2-client-comparison.md) | 历史主程序证据；修正前项目列仅为快照，当前行为以玩法规范为准。 |
| [O2JamO2 本地对照环境](o2jamo2-local-play.md) | 历史本机对照操作指南；本轮不启动客户端/服务端，也不继续部署 Classic。 |
| [原版客户端功能与实现覆盖检查](original-client-feature-audit.md) | F01–F18 观察与范围表；已选方案结束；其余暂缓，归属建议不是当前开发队列。 |
| [特殊随机与零血处理的静态追踪](original-random-and-failure-analysis.md) | 原版算法/指令历史证据；随机与 EX 新契约现状分别见专项规范。 |
| [产品表现与曲库功能归属](product-layer-inventory.md) | 现行表现功能归属；锁血/计分和存档资格不归产品绘制层。 |
| [变速 Mod 与 OJM 音频契约](rate-mod-audio-readiness.md) | 现行音频/速率契约；预览及回放补播、原生后台音量衰减。 |
| [预览采样补播](preview-sample-restoration.md) | 同曲难度切换保留声部、有界衔接和原生 Track 中途补播。 |
| [回放采样补播](replay-sample-restoration.md) | 基于实际命中与撤销记录的跳转恢复、生命周期及验收边界。 |
| [OJN 输入资源预算](ojn-resource-limits.md) | 审查 R03 的 OJN 子项；源字节和事件计数、所选难度读取、快照/写入保护及验证边界。 |
| [正式回放成绩校验](replay-score-validation.md) | 审查 R04；仅外部导入入口检查范围、枚举及统计容量，保留已有成绩和回放语义。 |
| [回放归档资源预算](replay-resource-limits.md) | 审查 R03 的回放读写子项；共用预算、输入认领、失败保分、原生保存／导出和验证边界。 |
| [本地 lazer 测试桥](lazer-test-bridge.md) | 显式测试构建专用；启动、原生退出、模式和选曲控制；正式构建排除。 |
| [当前 Realm 存储边界](realm-isolation.md) | 现行具体存储边界；直接类型隔离不等于后端已可无修改替换。 |
| [O2Lazer 重构路线图](refactor-roadmap.md) | 现行阶段状态；原版对齐已结束，暂缓功能不再作为自动推进项。 |
| [Remix O2Jam 与 O2Lazer 对照](remix-client-comparison.md) | 历史交叉证据；不覆盖韩服主参考及后续用户选定规则。 |
| [OJM 源一致性与资源保护方案](ojm-source-and-resource-plan.md) | R02 失效保护、读取缓存及原生完成调度已合入；有限功能/听感通过，完整性能/设备未穷举。历史对照与未完成边界单列。 |
| [音频增益验证](audio-volume-validation.md) | R08 基础 KS/短自动音/流式 BGM 原生增益矩阵已补齐；174 项过滤回归含新增 21 项，实际听感/设备和远程 CI 另列。 |
| [OJM 资源读取边界](ojm-resource-limits.md) | R03 non-seek/单输出/eager/Lazy 累计与并发字节保护已实施；消费者副本及原生 PCM 峰值风险保留。 |
| [回放输入与玩法时间边界](replay-timing-boundary.md) | 现行输入/时间边界；复用原生推进、撤销与录制；Rank 为持久化结果标记。 |
| [零血与失败：原版证据及当前差异](zero-life-and-failure-analysis.md) | 现行 EX 契约及原版历史对照；完整记录/锁血/曲终 F 已实现，不给未反馈场景补记验收。 |
| [原版对齐阶段收尾与构建规则复核](recent-changes-review.md) | 原版对齐阶段的历史收尾验证与暂缓边界，不代表最新部署。 |
| [曲库更新版本冻结复核](library-refresh-release-review.md) | 2026-10-06 曲库冻结版本的历史清理、层级检查、过滤验证与维护风险。 |
| [1.0.0-test3 发布记录](release-1.0.0-test3.md) | 第三个测试版的音频、共存和资源保护维护；正式 DLL 排除 bridge/计时探针，既有观察项保留。 |
| [1.0.0-test2 发布记录](release-1.0.0-test2.md) | 冻结 release 的能力、安装和验证范围；不包含后续 master 增量。 |

## 顶层维护文件

[English README](../README.md)与[中文 README](../README.zh-CN.md)提供相同能力/安装说明；
[AGENTS.md](../AGENTS.md)约束原生优先、只读参考、文本及过滤测试；
[第三方说明](../THIRD-PARTY-NOTICES.md)保留来源；许可证仍为仓库 LICENSE。
每次功能/宿主变化更新相应契约与当前进度，不在所有文档复制逐轮日志。
有价值的历史报告继续保留，不把其中的修正前状态改写成从未存在的行为。

当前未新增个人记录页或服务器存档；EX F 记录保留，未来聚合排除 F 的政策在存储
契约维护。原版剩余展示/混音/特殊模式项暂缓，独立客户端外围不自动进入本 ruleset。
