# 回放归档资源预算

更新：2026-10-08。属于独立审查 **R03 的回放读写子项**，不是路线图的同名阶段。
本页对应工作区实现；后续已安装验收 DLL，已测范围见[验收记录](r03-r04-acceptance.md)，未推送或更新 release。
读取预算、保存输出预算及附件失败与保分隔离已实现；[OJN 输入预算](ojn-resource-limits.md)也已实现；[R04 成绩数值校验](replay-score-validation.md)已实现；OJM 剩余资源边界独立评估。

## 职责与原生缺口

`Host/Replays/O2JamReplayArchive` 负责正式 v5 编解码和归档策略，不引用 Realm。
`Host/Persistence/Realm/O2JamReplayPersistencePatch` 只接原生导入、文件存储和成绩模型。
Core 判定、Integration 的玩法历史、音频补播及原生回放推进不参与归档预算。

原生 legacy 解码器不能读取 O2Lazer v5，也没有可直接应用于这份 JSON/GZip 的总量预算。
适配继续使用原生 `ArchiveReader.GetStream()`、文件存储、通知及
`JsonSerializableExtensions.CreateGlobalSettings()`。原生 JSON 解析器承担语法、默认值、
snake_case、类型转换与读取深度检查；本项目在原生解析／输出过程中计量资源。

`O2JamReplayJsonReader` 继承 `JsonTextReader`，同时覆盖现有 Payload 使用的标量读取入口。
仅覆盖 `Read()` 不够：原生序列化器的整数、浮点数、字符串、布尔和日期读取会走独立的
typed read 路径，详见 [Newtonsoft.Json 的 JsonTextReader 源码](https://github.com/JamesNK/Newtonsoft.Json/blob/13.0.3/Src/Newtonsoft.Json/JsonTextReader.cs)。
写入由 `O2JamReplayJsonWriter` 继承 `JsonTextWriter`，覆盖现有 Payload 的实际标量及容器输出。
读写共用 `O2JamReplayJsonBudget` 计量容器、文本与 token；原生默认值省略仍生效。
以后增加 Payload 字段或转换器时，需要检查它的实际读写入口并补边界测试；不要假定
任意新转换器或 byte[] 类型自动受现有集合计量覆盖。

## 当前预算

集中定义在 [O2JamReplayResourceLimits](../osu.Game.Rulesets.O2Lazer/Host/Replays/O2JamReplayResourceBudget.cs)。
它们是读写共用的归档政策，不是 osu 原生标准、v5 格式常量或播放时长上限。

| 项目 | 上限 | 计量范围 |
|---|---|---|
| 编码文件 | 64 MiB | 读取含签名和未消费尾部；保存含压缩器结束时写出的尾部 |
| JSON 字节 | 128 MiB | 读取计解压后的 UTF-8；保存计送入压缩器的 UTF-8；未压缩读取同样检查 |
| 每个数组／对象 | 1,000,000 项 | Frames、Actions、Pauses、统计字典及未知容器；对象按出现的属性计数，重复属性也计数 |
| 整份归档 | 16,000,000 个 token | 读取不因重复属性或替换集合重置；标量 typed reads 也计数；写入按原生实际输出计数 |
| 单个文本值／属性名 | 1,048,576 个 UTF-16 字符 | 读取在 token 生成后、业务赋值前检查；写入在转义输出前检查；不是原始 UTF-8 token 字节数 |
| JSON 深度 | 沿用当前原生默认 64 | 读取仍由原生深度检查负责；写入预防输出不可读的深度；没有使用旧候选的 32 层限制 |

未知字段仍允许。动作仍校验 0–6，预算不增加去重规则或把合法重复动作改写成七项。
负的有限帧时间、同时间帧、离线 UserId=-1、Rank.F、允许的可选字段及规则版本政策保留。
不截断帧，不增加时长上限，不修改 schema v5 或判定版本。

两次有界抽样使用已有停机备份的临时只读副本，仅取最多 32 条成绩中的 16 个回放附件。
初次抽样最大压缩体 163,037 bytes、解码体 1,737,880 bytes，其中早期测试格式不能作为
正式 v5 兼容证据。随后按日期降序抽取近期附件，最大压缩体 149,283 bytes、解码体
1,346,835 bytes，**16/16 能被新读取器恢复**。没有修改或打开客户端实际 Realm。
这是现有样本的余量证据，不证明全部长回放均兼容；24 小时时间跨度使用合成少帧回放验证，
不等于录制 24 小时或验证百万帧的实际体验。

## 读取与失败行为

1. 从原生流读取两字节签名，再通过前缀缓冲恢复给解码器，无需 Seek，也支持短读取。
2. 编码和解码边界各放一个计量流，只解压一次；原生序列化器直接消费 JSON reader。
   去掉存储读取的整段 MemoryStream/ToArray，以及 `ReadToEnd()` 的整段字符串副本。
   集合追加前计数，没有额外完整 JSON 扫描或新的 UTF-8 全量缓冲。
3. 恰好达到字节上限且 EOF 可以通过；读取一个哨兵字节确认超限后停止。
   GZip 解码结束后，剩余编码字节只计量并丢弃，不再次解压或保存。
4. 已识别 `version=5` 与 `ruleset=o2lazer` 的无效或超限外部输入，在创建 ScoreInfo 和
   写入文件前停止，不回落 legacy 解码。未知、其他 ruleset、旧版或未标记外壳保持未认领，
   后续由既有原生／其他 ruleset 导入器处理。
5. 已存 O2Lazer 回放超限时返回不可播放；成绩、原附件及关联保留。外部拒绝不会删除源文件。
   通过原生 SimpleNotification 显示 `O2LazerStrings.ReplayReadLimitExceeded`，中英文资源同步。

## 保存与导出

保存接缝位于 [O2JamReplayPersistencePatch.Saving](../osu.Game.Rulesets.O2Lazer/Host/Persistence/Realm/O2JamReplayPersistencePatch.Saving.cs)。
保留原有 `Player.ImportScore` prefix/postfix，没有增加全局保存或导出补丁目标。

1. 仅处理 O2Lazer 本局成绩。原生 `DrawableRuleset.ReplayScore != null` 的观看回放分支
   保持直接返回，不改 Hash、不导入成绩、不重复追加附件；其他 ruleset 保持原路径。
2. 在原生成绩克隆前尝试编码。复用原生序列化设置直接写出 UTF-8/GZip，不先生成完整
   JSON 字符串、UTF-8 byte[] 或全部 FrameData/Actions 副本，也不回读附件进行第二次验限。
   原生 Player 已停止录制，因此可以逐项投影稳定的帧集合；非法帧与空录制不会产生不可播放的附件。
3. 编码与 JSON 输出各自计量；帧数、暂停数在复制前检查，动作／字典在遍历或输出时检查。
   压缩结束的尾部同样受限；任一编码或关闭失败，`TryCreate` 的输出均为 null，部分缓冲不发布。
4. 成功仍以原生 SHA-256 作为成绩 Hash。失败时保留已有非空 Hash；新成绩原本没有 Hash
   时使用原生 ID 生成 `o2lazer-score-{ID:N}` 本地身份，避免无回放成绩无法被原生 ScoreManager
   按 Hash 查找，或不同失败录制相互覆盖。没有新增数据库字段或第二套成绩模型。
5. 原生 Player 继续克隆、导入和回写成绩身份。原生成绩导入失败仍传播，不能提前通知“成绩已保存”。
   导入成功后才在原生更新线程调度 `ScoreManager.AddFile`；编码失败只跳过附件。
   调度、文件写入和通知消费者异常不重新使已导入成绩的任务失败；既有附件不删除。
   原生文件写入事务承担关联回滚，进程中断或物理文件残留仍按原生 FileStore 清理机制处理。
6. 超限／其他附件错误分别使用 `ReplaySaveLimitExceeded`／`ReplaySaveFailed` 的原生通知，
   中英文资源同步。若宿主私有成绩服务接缝不可访问，只能记录附件失败；不能承诺此时还能通知。

原生 `LegacyScoreExporter.ExportToStream` 已能复制已有附件，因此继续直接复用，未新增导出编码器。
流式 GZip 输出的压缩字节可以不同于旧写入器；其 v5 内容及规则版本不变，Hash 对应本次实际附件。
旧文件不重新编码、不改写；原生导出复制的附件与已保存字节一致。

## 限制与后续工作

- 字节预算限制解析器可消费的数据；单文本预算是在原生 token 已生成后检查，不能宣称
  token 分配前已限制到 1 MiB。原生缓冲、业务对象、已有 byte[] 及帧转换也占内存，
  这些预算不是进程峰值内存保证。若需要更严格的 token 分配保护，应另评估原生缓冲机制，
  不悄悄增加第二套 JSON 扫描器。
- 原生 ZIP reader 可能在调用本模块前物化条目。本预算不覆盖上游分配，也不能保证未认领
  数据在其他导入器中同样有界；不能为此修改所有 ruleset 共用的 ZIP 政策。
- 只在归档导入／恢复和本局保存时检查；没有加入游玩逐帧、音频 seek、预览、曲库写入或模式切换路径。
  有新增 token/字节计量成本；本轮未运行计时 benchmark，不承诺具体提速或零等待变化。
- 输出预算不限制原生 ReplayRecorder 在本局累计的帧，也不覆盖原生 Player 在调用保存接缝
  前的 Score.DeepClone。归档仍需要保存压缩输出及交接 byte[]；不是整个录制／保存过程的峰值内存保证。
  正常原生调度的附件完成和失败已测试；进程退出、中途销毁 Player 等完整生命周期仍须客户端验证。
- [OJN 总量预算](ojn-resource-limits.md)和 [R04 外部成绩元数据校验](replay-score-validation.md)已分别实现；
  没有夹正或重算旧成绩。OJM 的载荷/缓冲保护与整包上限政策仍需区分评估，R03 不作全部关闭。

## 验证

针对安装版 Game/Mania 2026.1005.0、Framework 2026.921.1；所有测试带 `--filter`。

| 范围 | 通过 | 内容 |
|---|---:|---|
| 回放读写预算、正式 v5 兼容、本地化 | 130 | 字节／token 上限两侧及恰好上限、压缩收尾、短读取、各集合和 typed 标量、空／非法录制不发布、默认省略及 null token、负时间／重复动作／长时间跨度 |
| 临时 Realm 正式回放导入／保存／重载 | 46 | 读取失败无新增数据；保存失败保分且可查找、原生文件写入失败回滚附件、等待原始导入完成、Player 保存成功／失败、观看回放不重复保存、原生导出字节一致、既有附件保留及 F 结果回归 |
| BMS 两种独立载入顺序 | 2 | 现有过滤／星数／图标／结算和正式 O2Lazer 回放接缝；新增实际 Player 保存成功、编码失败保分及观看回放保护，共用各自 Harmony 运行时 |

共 178 项通过，0 失败、0 跳过；这是本次最终定向集，不重复累计前一读取子项的 131 项。
语义分层检查：0 编译错误、0 越层违规、0 过期例外，
仍为六条精确例外、两个跨层 partial；没有新增补丁安装目标。
构建仍有既有 AutoMapper 13.0.1 的 NU1903 告警，没有更换宿主依赖。
本次日志在 `.artifacts/replay-write-budget/`，前一读取子项日志在 `.artifacts/replay-read-budget/`；
有界样本探针在 `.artifacts/replay-size-probe/`，
均不纳入 Git。未安装 DLL、运行完整客户端验收、修改真实库或执行 benchmark。
