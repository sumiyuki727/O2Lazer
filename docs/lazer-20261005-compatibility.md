# osu!lazer 2026.1005.0 适配记录

维护状态（2026-10-06）：版本专项历史证据；宿主目标仍为 2026.1005.0，后续 EX 安装不由本页旧 Hash 表示。
文档职责与最新状态入口见[索引](README.md)。

验证日期：2026-10-05。目标为已安装的正式 `2026.1005.0-lazer`，不是当日源码 master 或 tachyon。
正式版 Game/Mania 的 ProductVersion 指向提交 `9a10d935b05a3b896f4bd811d620f2023331db93`，
与 `D:/osu` 已抓取的同名发布标签一致。该只读源码检出当前 master 在发布标签之后，不能直接按
master 的依赖表构建正式版适配。

## 依赖与修改

| 项目 | 已验证的正式版依赖 | 本轮处理 |
|---|---|---|
| osu.Game / osu.Game.Rulesets.Mania | 2026.1005.0.0 | `Directory.Build.props` 的 OsuBase 更新为 2026.1005 |
| ppy.osu.Framework | 2026.921.1 | ruleset 本地二进制引用路线的 Framework 包同步升级 |
| ppy.osu.Game.Resources | 2026.918.0 | 测试继续引用同一客户端的资源 DLL |
| Realm / SQLitePCLRaw.bundle_e_sqlite3 | 20.1.0 / 3.0.5 | 保持宿主兼容版本 |
| AutoMapper / MessagePack | 13.0.1 / 3.1.8 | 保持宿主 API / 回放注解依赖 |

以上同时核对了 `osu!.deps.json`、程序集版本与发布标签中的 csproj。该标签中的 Framework
是 2026.921.1，资源是 2026.918.0；当日 master 已使用较新的 Framework/资源组合。
更新宿主时应首先确认安装版 ProductVersion 对应的标签，再核对完整依赖，而不是按 Game
版本推断所有包版本。NuGet 替代路线仍仅适用于匹配 Game/Mania 包已发布的情况；本轮构建和
回归使用安装版二进制，没有将包路线作为已验证的部署依据。

本轮运行时代码修改仅为这两处依赖升级，没有修改独立 Core/Formats 或判定、音频、计分规则，
没有新增适配器、跨层依赖或 Harmony 接缝。ruleset 程序集身份仍为 1.0.0，正式回放仍为 v5，
导入身份和 O2Lazer 缓存版本规则不变。

## 原生路径与补丁复核

比较此前参考提交 `6408b4e0e46a9f4ea1b8d263afccd1df799604e4` 与正式版标签，
Mania 玩法源码、选图难度组件及现有主要补丁入口没有相关接口变更。
此次 `Skin` 移除委托颜色接口；项目已经通过原生皮肤契约使用配置，未依赖被移除的接口。
Form 控件调整搜索用 FilterTerms，项目继续继承原生控件，没有复制旧筛选行为。
BackgroundMusicManager 的变化属于 RankedPlay 背景音乐，不是 O2Lazer 预览/游玩音轨。

现有 26 项补丁的安装成功由 `O2JamManiaScoreTest.EntryIsImplementedAndDefaultsToRankedSeven`
验证，包括可选项；定向组件测试继续覆盖其行为与其他 ruleset 隔离。本次发布没有补齐
[补丁清单](compatibility-patches.md)记录的原生扩展缺口，因此没有仅为升级重写或删除这些适配。
未来宿主新增公开接口时仍须逐项重新判断必要性。

## 自动验证与限制

先执行 167 项补丁、原生设置/皮肤、误差条、随机和身份定向测试，再执行 `scripts/verify.ps1`：

| 过滤范围 | 通过数 |
|---|---:|
| 独立 Formats | 42 |
| 独立 Core | 89 |
| 语义检查器自身测试 | 15 |
| 宿主常规（玩法、音频、UI、Mod、身份等） | 1045 |
| 临时库旧谱面迁移/恢复，独立进程 | 127 |
| 正式 replay 导入，独立进程 | 21 |
| 合计 | 1339 |

上述均无失败或跳过。另以实际安装的 BMSRuleset 2026.920.0.0 分进程验证 BMS → O2Lazer
和 O2Lazer → BMS，各 1 项通过，包括星级、结算替换、图标、共用补丁及 O2Jam replay。
两次均确认 tick 误差条适配安装成功，详见[共存边界](bms-coexistence.md)。初始 167 项
与常规测试重叠，不额外加入合计。

模块、存储、计分源码边界及完整语义检查全部通过：编译错误、分层违规和过期例外均为 0。
既有六条精确类型例外、两项跨层 partial 保持。测试报告及依赖/架构证据快照位于忽略的
`.artifacts/host-compatibility/2026.1005.0`；没有运行 benchmark、全曲库扫描或写入真实用户库。

在线检查当前已还原 NuGet 图仍报告 AutoMapper 13.0.1 的既有 NU1903。
新版宿主仍依赖原构造 API，本轮不替换它、不屏蔽告警；A16 风险与限制继续见
[宿主依赖审计](development.md#宿主依赖审计-a16)。此查询不覆盖整个客户端二进制依赖图。

自动检查不能代替客户端渲染/听觉验收。安装后应小范围复测跨模式及 HR/MS 颜色过渡、
SR 保留前值、随机菜单及 Seed（后续入口调整见[随机契约](column-randomisation.md)）、KS 暂停/回放跳转、长条视效和 tick 误差条；BMS 完整
游玩链路仍沿用原共存文档的验收边界。无需清空谱面或重建数据库。

## 部署

适配 DLL 已于 2026-10-05 17:18:55（JST）在 lazer 关闭后备份安装到
`D:/osu-lazer/rulesets/osu.Game.Rulesets.O2Lazer.dll`，安装前后校验 Hash 一致。
SHA-256：`1E8FA311936DD8BAF134BE6C1F84DC1BBBA499ACD36A9639024DA6FE9FAE9E60`，大小 3,559,424 字节。
旧 DLL 备份位于 `D:/o2lazer/.artifacts/client-backups/lazer-20261005-20261005-171855`，安装记录为
`.artifacts/install-lazer-20261005.json`。未操作用户库或替换客户端依赖；新版实机验收仍待进行。
