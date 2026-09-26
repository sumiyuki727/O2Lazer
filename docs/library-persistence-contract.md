# A04：曲库与成绩的持久化身份

更新：2026-09-26。本页记录现有实现及重构时必须保持的行为，不把当前 Realm 模型当作未来存储协议。只检查源码和临时数据库，不读取或清理客户端真实库。

## 身份与版本现状

| 对象 | 当前写入位置与来源 | 用途及稳定性 |
|---|---|---|
| ruleset | 原生 `RulesetInfo.ShortName = o2lazer` | 区分 O2Lazer 与其他 ruleset；旧程序集版本更新后仍须解析到同一 ruleset。 |
| 源 OJN | `BeatmapSetInfo.Files` 中的 OJN 文件；`RealmFile.Hash` 为源字节 SHA-256 | 内容寻址；不是外部路径，也不是谱面难度身份。 |
| 外部路径 | `BeatmapInfo.Metadata.Source` 存目录，set 文件名存 OJN 文件名 | 查找、刷新、移动检测和缺失标记；路径改变本身不应换谱面 ID。 |
| set | 原生 `BeatmapSetInfo.ID`；`Hash` 为 `SHA256("o2lazer:" + 排序后的难度 MD5)` | 同源可玩难度的集合；旧版 set hash 是排序后 MD5 的直接拼接，迁移时原位更新。 |
| 难度 | 原生 `BeatmapInfo.ID`；`MD5Hash = MD5(源字节 + 难度编号)`；`Hash = SHA256(小写源哈希 + ":" + 难度编号)` | ID 是本地关联键，Hash 是成绩与原生缓存的匹配键。旧版三难度曾共享源文件 SHA-256；同源迁移时原位分开。 |
| 成绩 | 原生 `ScoreInfo.ID`、`BeatmapInfo` 关联与 `BeatmapHash` | `BeatmapHash` 必须与关联谱面的 Hash 一致；旧成绩 ID 和成绩内容不得因谱面元数据重算而替换。 |
| 正式回放 | `ScoreInfo.Files` 与 `ScoreInfo.Hash`，归档内 `ruleset=o2lazer`、schema v5、谱面 Hash/MD5 | v5 是正式契约；重构前无标记测试 replay 不兼容。文件归属和成绩 ID 不能因后端迁移丢失。 |
| 文件夹收藏夹 | 原生 `BeatmapCollection.BeatmapMD5Hashes` | 根据活动谱面和外部目录同步；用户自建收藏夹不归本功能清理。 |

元数据版本目前分散在 `BeatmapMetadata.Tags`：`o2lazer-clean:2` 标记导入元数据，`o2lazer-encoding:2` 标记文本解码，`o2lazer-source-size:<字节数>` 辅助快速刷新；Mania 星级与最大连击另有算法版本标签。`LastLocalUpdate` 与源长度用于跳过未变文件，源 Hash 保存在原生文件存储。没有独立的 O2Lazer 数据库 schema 版本，也没有后端中立的源/set/难度记录。

## 已确定的更新规则

| 输入变化 | 当前行为 | 重构约束 |
|---|---|---|
| 同一源字节，重算标签、星级或编码 | 原位更新 set/难度，并将旧共享 Hash 的已关联成绩改为独立难度 Hash | 保留 set ID、难度 ID、成绩 ID、原生成绩关联；不得将 EX/NX/HX 的成绩串到另一难度。 |
| 源文件搬家，字节不变 | 刷新服务按长度和 SHA-256 找到旧 set，更新外部路径 | 保留原 IDs 和成绩；重复副本只保留一个活动 set。 |
| 同一路径的 OJN 字节改变 | 新建 set/难度，旧 set 标记 `DeletePending`；写入后旧成绩暂时关联旧谱面；下次启动时原生 Realm 清理会移除待删除 set 和谱面对象，成绩记录及旧 `BeatmapHash` 留下 | 用户确认旧成绩不得自动挂到新版谱面。即使只改元数据，当前也按源字节变化处理。历史成绩不会出现在新版谱面排行榜中；用户确认只保留成绩记录和旧 Hash，无需继续打开旧谱面。 |
| 源文件缺失或用户清理 O2Lazer 曲库 | 将本 ruleset 的 set 标记 `DeletePending`，原生启动清理随后移除这些 set 和谱面对象 | 不标记成绩删除；成绩的旧 `BeatmapHash` 保留。用户确认无需在客户端继续打开旧谱面。 |

2026-09-26 的临时 Realm 回归已验证：改动 OJN 的一处可玩物件后，新谱面获得不同 ID/Hash，旧 set 待删除，同一会话内旧成绩的 ID、连击和旧谱面关联保持不变，新谱面没有继承成绩。临时库模拟重启还验证了原生 `RealmAccess` 清理会移除旧 set/谱面，成绩 ID、连击和旧 `BeatmapHash` 仍在，`ScoreInfo.BeatmapInfo` 变为 null。现有测试也覆盖同源旧 set 的单难度迁移、源文件搬家和缺失标记。完整三难度迁移、重复副本竞争与失败回滚尚未形成同等强度的临时库证据。

## 写入重设计前必须解决的缺口

1. **字段所有权与版本**：`refreshMetadata` 只重算标题、作者、星级、部分标签、路径和 Hash；同源但算法变更时，难度名、等级载体、BPM、长度、物件数、背景等不会统一重算。新写入契约应逐字段定义来源、版本与原位更新条件，并明确不可自动覆盖的用户字段。
2. **身份判定**：旧版共用源 Hash、新版按难度 Hash、set hash 改版、原生 `BeatmapInfo.ID` 和 `ScoreInfo.BeatmapHash` 必须一同映射。仅凭路径、标签或一个 MD5 不能安全迁移。旧版存在无可玩物件但有块的难度，新版规划器只保留可玩难度，迁移时要决定如何保留其历史记录。
3. **刷新与重复**：当前快速跳过只比较时间戳和长度，若两者未变而字节变了，显式刷新也可能跳过；相同内容的搬家与同时存在的副本走不同分支。要在准确性和大曲库扫描成本之间明确校验策略。
4. **事务与文件**：`WriteBatch` 的 Realm 模型更改共用一笔事务，但原生 `RealmFileStore.Add` 在事务内把字节写入磁盘；数据库回滚不等于文件系统回滚。新后端需要为文件暂存、失败重试、孤儿文件回收定义边界。
5. **后端替换**：当前 `IO2JamLibraryWriter` 表示导入动作，不是数据模型或迁移协议。先形成源、set、难度、成绩和文件的后端中立身份/版本表，再在临时数据库验证更新、移动、重复、缺失和失败重试；不直接对用户库试迁移。

相关实现：[当前 Realm 边界](realm-isolation.md)、[整体路线图](refactor-roadmap.md)、[问题 A04](architecture-audit.md#初始审查问题)。
