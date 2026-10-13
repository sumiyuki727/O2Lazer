# 正式回放的成绩元数据校验

更新：2026-10-08。对应独立审查 **R04**，不是路线图的 R04 阶段。
当前工作区已实现并完成定向验证；后续已安装验收 DLL，已测范围见[验收记录](r03-r04-acceptance.md)，未提交、推送或更新 release。

## 责任与原生缺口

[O2JamReplayArchive.TryReadMetadata](../osu.Game.Rulesets.O2Lazer/Host/Replays/O2JamReplayArchive.cs)
在解析正式 v5 后、返回用于新建成绩的元数据前校验。责任仍归 Host/Replays，不引用 Realm。
[原生存储接缝](../osu.Game.Rulesets.O2Lazer/Host/Persistence/Realm/O2JamReplayPersistencePatch.cs)
继续负责唯一谱面匹配、构建 ScoreInfo 和原生成绩导入，没有新增补丁或数据库字段。

只读参考源码中的 ScoreImporter、ScoreInfo 没有可直接复用的统一范围校验。
原生 legacy 解码/标准化会重算部分成绩，不能用它替代 v5 中已录制的 O2Jam/MS 数据。
继续复用原生序列化设置、数值类型转换、枚举定义、Mod 兼容验证、文件存储和事务。
原生参考版本与目标二进制的差异见[独立审查范围](refactor-review-2026-10-06.md#审查范围与证据强度)。

## 校验规则

| 字段 | 接受范围 | 依据 |
|---|---|---|
| Accuracy | 有限数且在 `[0,1]` | 保存的比例；NaN、正负无穷和越界不进入新成绩 |
| TotalScore / TotalScoreWithoutMods | 非负 `long` | 只检查保存总分，不限制 Core 的判定分数增量 |
| MaxCombo | 非负 `int` | 保存最大连击，区别于 live Combo 的 `-1` 哨兵 |
| Rank | 原生 ScoreRank 已定义值 | `F=-1` 合法；不能按整数非负判断评级 |
| Statistics / MaximumStatistics 的键 | 原生 HitResult 已定义值 | 不按当前 O2Jam/MS 使用子集建立白名单；包括原生已有的忽略、奖励等结果 |
| 两种统计的值 | 非负 `int`，各字典合计不超过 `int.MaxValue` | 原生后台成绩处理使用 `int` 求和；用 `long` 在同一次检查中防止合计溢出 |

原生 JSON 数值转换先拒绝超出目标 int/long 的表示；本模块检查可被转换但语义越界的值。
集合、字节、token 与深度保护继续见[归档预算](replay-resource-limits.md)，不再复制一套结构扫描。
统计合计上限是容量约束，不要求统计数等于谱面音符数、MaxCombo 或 MaximumStatistics。
不验证成绩真伪，不重算 Mod 分数、准确率或评级，不把越界数值夹到合法范围。

## 保留的语义与失败边界

- schema v5、judgement_version、谱面身份和 Hash 公式不变。
- 省略可选字段继续使用原生序列化设置的既有缺省值：数值为 0、Rank 为 D、统计为空。
  UserId 省略后实际为 0；显式离线 `-1` 也接受，未借本次校验更改身份默认值。
  统计的 null/空对象仍接受并归一为空字典。
- MS/NoMS、NF、EX 曲终 F、负的有限帧时间、暂停记录及 Core/live Combo=-1 不受本次非负规则限制。
- 已识别为 O2Lazer v5 的无效元数据使原生 CreateModel 返回 null，在谱面查询、文件写入和
  模型提交前停止，不改用其他 ruleset 解码。未认领的外壳继续原有导入链。
- 外部拒绝保留原 `.osr` 和已有成绩/附件/关联；语义错误不冒充资源超限通知，沿用原生导入失败结果。
- 已存成绩观看使用原生持久化的 ScoreInfo，TryReadScore 不重新导入归档中未使用的成绩字段。
  若帧/格式及资源预算合法，旧归档中不参与播放的异常 Accuracy/MaxCombo 不使有效帧失效；
  不重写已有成绩、评级、附件或 Hash。这不是旧库数值迁移方案。

本次检查只发生在外部元数据导入入口：少量标量比较和两种统计字典的一次遍历，
没有额外帧遍历、回读文件、Hash、谱面解码、Realm 查询或事务。
本局保存、已存观看、预览、音频 seek 与模式切换不新增此项检查。
功能和计数证据不提供客户端毫秒数或吞吐保证；未运行 benchmark。

## 验证

目标 Game/Mania 2026.1005.0、Framework 2026.921.1；全部测试带过滤器。

| 定向组 | 通过 | 覆盖 |
|---|---:|---|
| 元数据、读写预算、正式兼容与计分 | 175 | 其中新增元数据 66 项；明文/GZip、无效/溢出/非有限数、原生枚举、默认值、容量边界、F/MS/NoMS 与已有成绩播放 |
| 独立进程临时 Realm 回放 | 73 | 其中新增 27 项；实际原生 Import 拒绝无残留、源文件和已有数据保留、GetStream 认领、原生 Populate/附件保存与重载、旧归档字段不重导入 |
| BMS 两种独立载入顺序 | 2 | 指定 BMS 与 O2Lazer 的现有过滤/星数/图标/结算及正式回放、原生 Player 保存接缝 |

共 250 项，0 失败、0 跳过；不与 R03 或历史全项目数字累计。
临时 Realm 的有效 Mod 测试显式登记可用 ruleset，与真实安装登记前提一致。
源码语义与直接存储边界检查通过；六条精确例外、两个跨层 partial 保持，未新增层级豁免。
既有 AutoMapper 13.0.1 NU1903 告警仍在。证据位于 `.artifacts/replay-metadata-validation/`。
以上 250 项使用临时库，没有操作客户端真实数据库。后续[验收记录](r03-r04-acceptance.md)
包含真实客户端正常更新、预览控制和回放读写/重启；NoMod/MS 新成绩保存、重启后读取及回放前后跳转正常样本已核对，完整视觉/听感组合未据此关闭。
