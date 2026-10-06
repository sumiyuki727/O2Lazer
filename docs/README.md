# O2Lazer 文档索引

更新：2026-10-06。当前目标 osu!lazer 2026.1005.0 / Framework 2026.921.1。
原版客户端对齐阶段按用户决定结束，未选差异暂缓。这是选定范围的结束，不表示全项目
重构、所有原版功能或全部客户端验收均已完成。当前曲库更新版本的审查、清理和验证见
[版本冻结复核](library-refresh-release-review.md)；安装信息以该页与存储契约最新记录为准。
原版对齐的 1352 项验证和 EX 部署属于 [历史收尾复核](recent-changes-review.md)。

## 如何选择文档

运行规则看玩法规范；改代码看架构和问题清单；写入/迁移看持久化契约；构建/测试看
开发说明。各页的历史日期、测试数与 Hash 仅证明对应版本，不是全局最新结果。
用户明确政策高于参考客户端；原版指令或资产名称不自动成为新功能需求。

| 文档 | 所负责的内容与状态 |
|---|---|
| [全项目分层审查与问题清单](architecture-audit.md) | 现行问题清单；原版范围已结束；系统/依赖问题保持。 |
| [O2Jam 音频同步可选诊断构建](audio-sync-diagnostics.md) | 可选操作指南；音频诊断默认关闭，与已移除的误差条监听分开。 |
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
| [变速 Mod 与 OJM 音频契约](rate-mod-audio-readiness.md) | 现行音频/速率契约；保留已确认 KS 行为及小型 KS 中段尾音限制。 |
| [当前 Realm 存储边界](realm-isolation.md) | 现行具体存储边界；直接类型隔离不等于后端已可无修改替换。 |
| [O2Lazer 重构路线图](refactor-roadmap.md) | 现行阶段状态；原版对齐已结束，暂缓功能不再作为自动推进项。 |
| [Remix O2Jam 与 O2Lazer 对照](remix-client-comparison.md) | 历史交叉证据；不覆盖韩服主参考及后续用户选定规则。 |
| [回放输入与玩法时间边界](replay-timing-boundary.md) | 现行输入/时间边界；复用原生推进、撤销与录制；Rank 为持久化结果标记。 |
| [零血与失败：原版证据及当前差异](zero-life-and-failure-analysis.md) | 现行 EX 契约及原版历史对照；完整记录/锁血/曲终 F 已实现，不给未反馈场景补记验收。 |
| [原版对齐阶段收尾与构建规则复核](recent-changes-review.md) | 原版对齐阶段的历史收尾验证与暂缓边界，不代表最新部署。 |
| [曲库更新版本冻结复核](library-refresh-release-review.md) | 最新代码清理、层级检查、过滤验证与冻结后的维护风险。 |

## 顶层维护文件

[English README](../README.md)与[中文 README](../README.zh-CN.md)提供相同能力/安装说明；
[AGENTS.md](../AGENTS.md)约束原生优先、只读参考、文本及过滤测试；
[第三方说明](../THIRD-PARTY-NOTICES.md)保留来源；许可证仍为仓库 LICENSE。
每次功能/宿主变化更新相应契约与当前进度，不在所有文档复制逐轮日志。
有价值的历史报告继续保留，不把其中的修正前状态改写成从未存在的行为。

当前未新增个人记录页或服务器存档；EX F 记录保留，未来聚合排除 F 的政策在存储
契约维护。原版剩余展示/混音/特殊模式项暂缓，独立客户端外围不自动进入本 ruleset。
