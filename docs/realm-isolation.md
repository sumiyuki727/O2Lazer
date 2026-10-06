# 当前 Realm 存储边界

维护状态（2026-10-06）：现行具体存储边界；直接类型隔离不等于后端已可无修改替换。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-06。这里记录现状，不预设 osu! 未来存储技术，也不授权删除或重写用户数据。[A04 设计](library-persistence-contract.md#重设计草案的范围与分层)中的 A04-1/2/3/4 已实现；A04-5 活动文件修复与三个真实样本的临时库验证也已完成。普通更新与内部修复已分流，逐批全库索引已由原生查找替代。星级计算与谱面投影分离：当前可并行计算已提交源，收藏夹同步后补齐剩余源；清空按原生事务分批报告进度。新的流水线性能、提前游玩与完整客户端联测仍待验收，见[流水线契约](library-persistence-contract.md#准备星级与进度流水线)。重构前的测试 replay 不在兼容范围。

生产代码中直接使用 Realm/RealmAccess/RealmFileStore/RealmUser 的文件集中在 `osu.Game.Rulesets.O2Lazer/Host/Persistence/Realm`：

2026-10-06 数字文件名查询合并也仅存在于此层；Host/Library 请求和流程不变。合并原生候选条件后仍按具体文件名过滤，再做完整路径/归属/唯一性校验，不新增索引 schema 或跨事务托管缓存。已通过功能/边界检查及获准真实备份只读对照并备份安装；有组查询样本约减少 24.7%，整轮实际收益仍待确认，详见[候选记录](library-persistence-contract.md#数字文件名候选查询待性能确认)。

| 文件 | 当前职责 |
|---|---|
| `O2JamLibraryWriter.cs` | 原生文件预留/模型两事务、活动 OJN/封面完整性与修复通知、谱面/标签/成绩写入；实现无 Realm 的单源/批次难度契约，最多十六源共享缓存重查和原生提交，先验证每源全部槽位；身份失效逐源拒绝，事务异常/取消整批回滚，提交后发布脱管通知；保留现有谱面 ID 的更新路径 |
| `O2JamNativeDifficultyPersistencePatch.cs` | 在原生 StarRating 写入事务内核对 O2Lazer 内容身份，保存该次原生计算已取得的版本/MaxCombo；其他 ruleset、非事务模型和 Mod 结果不参与，失败由后置阶段补齐 |
| `O2JamFileVerificationPatch.cs` | 观察指定线程/文件库/Hash 的原生校验结果，修复前保留活动 O2Lazer 所有者 ID；不改变校验或写入，安装失败回退提前校验，不跨线程/事务保存托管对象 |
| `O2JamLibraryLookup.cs` | 在模型事务内以原生 Ruleset 对象链接限定活动 beatmap，查询单字段 AudioFile 后缀；无 OJN 文件名的旧记录单独回退到 Path，set Hash 独立查询后验证归属；完整路径、歧义、大小写和孤立/待删除保护保持；已知 ID 为主键提示，源内容复用文件主键/反向关联，仅持有本批命中模型并跟踪同事务修改；没有全库托管索引或跨事务缓存；[真实只读备份对照](library-persistence-contract.md#原生链接查询重设计)已通过，完整客户端性能仍待验收 |
| `O2JamRealmLibraryBackend.cs` | 将导入会话接到写入器、收藏夹、并行/尾阶段难度处理器与宿主缓存失效；计算线程使用独立通知/恢复状态，原生 RealmAccess 串行写事务 |
| `O2JamSourceFolderCollectionService.cs` | 文件夹收藏夹的数据库操作 |
| `O2JamReplayPersistencePatch.cs` | 原生成绩/replay 导入导出接入和原生文件存储 |
| `O2JamSongSelectRankPatch.cs` | 原生 Realm 成绩集合通知；选中成绩策略在 Presentation 的同名 partial |
| `O2JamSettingsSubsection.Realm.cs` | 原生依赖注入组装后端；页面控件位于 Presentation 的同名 partial |

`Host/Library` 定义 `IO2JamLibraryWriter`、导入请求/结果和扫描/应用流程；`Host/Configuration/O2JamLibrarySettingsSession` 接受后端工厂，设置视图不持有 RealmAccess。`Host/Replays` 的 v5 归档编码、回放录制和输入也不直接引用 Realm。`scripts/check-storage-boundary.ps1` 搜索生产源码中的直接 Realm 类型；`scripts/check-architecture.ps1` 另外按真实声明文件检查本仓库跨层依赖，二者均由 `scripts/verify.ps1` 调用，2026-09-30 检查通过。

隔离的限度：宿主仍使用 osu! 原生数据库模型，主 ruleset 项目仍引用 Realm；两个 partial 接入点和原生 `ScoreManager`/`BeatmapInfo` 类型仍与具体后端共同编译。A12 将两个 partial 的跨层成员引用和准确声明文件列为[精确例外](clean-rewrite-architecture.md#允许依赖矩阵与例外)，防止范围无意扩大；语义检查不证明外部原生模型与 Realm 的传递依赖已经消除，也不证明可直接替换 SQL。当前 `IO2JamLibraryWriter` 是现有调用接口，不是已敲定的未来存档协议。

2026-10-03 原生难度结果的线程内交接只含 ID、Hash 和普通数值；原生 StarRating setter 适配及具体事务/文件引用验证全部留在 Realm 层。Host/Difficulty 通过原生工作谱面公开的文件流和路径扩展方法读取已提交 OJN，仍依赖原生 BeatmapInfo/BeatmapSetInfo 载体，未把存储模型传入独立 Core/Formats。并行/后置阶段通过无 Realm 契约重查当前缓存，当前一次原生 Run/Write 处理最多十六源，托管模型仅在调用内存活；提交再次核对身份并跳过已完成槽位。未新增后端框架、schema 或原生补丁。验证及失败恢复见[补算协调](library-persistence-contract.md#原生后台与后置补算协调)和[缓存批次](library-persistence-contract.md#星级缓存批次与分项计时)。

[A04 身份与字段清单](library-persistence-contract.md)记录了当前契约和已确认的旧成绩策略。重设计顺序：先列每个字段的来源、含义、读写方、版本和更新条件；区分源文件、set、难度和成绩/replay 身份；定义缺失/移动/重复源的更新与回滚；最后在临时数据库演练新旧关联和迁移。旧 schema 的读取/清理策略需单独确认，不因 Realm 停用传闻提前删除任何用户记录。本地/服务器档案关系仍属路线图 R10 待决项。

2026-10-01 源码核对确认 `RealmFileStore.Add` 已提供安全文件写入和内容校验，但新文件记录随模型事务回滚时，磁盘文件不属于原生零引用行清理的查询范围。草案建议先提交原生 RealmFile 预留行，再在独立模型事务中写文件/发布引用，以复用现有清理能力；该两事务恢复方案已在 A04-4 落地，故障注入、重启清理及共享文件保护通过临时库验证；它仍不提供数据库与磁盘共同原子提交。

A04-1 的源快照及提交前只读句柄验证位于 Host/Library，原生 Tags 载体编解码也位于该宿主桥接目录，供元数据读写方共用；它们不返回 Realm 托管对象，不把 Tags 协议当成独立 Core/Formats 或未来存储 schema。Realm 适配器共用字段投影和原生文件引用同步，难度复制及作者快照复用原生 CopyTo/DeepClone。具体 Realm schema 与当前两个 partial 接缝未改。全脚本 922 项过滤回归及全部边界检查通过；没有安装到客户端或对真实库执行写入。

A04-2 的三槽位清单、MD5 公式及缺失证明同样归 Host/Library；具体成绩关联快照、全库集合所有者检查、原生模型移除和正式 v5 唯一查询仍在 Host/Persistence/Realm。没有新增跨后端 ORM、身份别名表或永久迁移表。原生单条 QueryBeatmap 无法验证唯一性，因此 replay 导入在原生 ScoreImporter 的既有 Realm 接缝中限定 O2Lazer 活动谱面查询；归档字节与原生分数导入/读取管线保持。详见[身份迁移边界](library-persistence-contract.md#a04-2-实施边界)。

A04-3 的规范路径、SHA-256 分组、通知重试队列及取消结果快照位于 Host/Library，均不接收 Realm 对象。事务内所有者查找留在 Host/Persistence/Realm，复用原生查询和文件反向关联，随实际事务状态变化，不依赖扫描旧快照或跨事务缓存。工作谱面与难度缓存分别订阅脱管快照，监听器失败不改变已提交结果。外部 OJN 的副本协调和提交后独立重试仍是局部适配，没有 schema、ORM 或通用存档层。范围和失败语义见[存储契约](library-persistence-contract.md#a04-3-实施边界)。

A04-4 的预留清单、原生 RealmFile 写入、完整预留重查及内部故障阶段全部在 Realm 适配器内。Host/Library 只传递 CancellationToken 和既有结果快照，不知道文件行或具体事务。没有新增跨层 partial、存储 schema、永久 outbox 或自有文件清理器；原生 SHA-256、RealmFileStore.Add 与 RealmAccess 启动 Cleanup 继续复用。完整恢复边界见[故障矩阵](library-persistence-contract.md#a04-4-实施边界与故障矩阵)。

A04-5 的普通更新只在适配器内检查 OJN/封面引用的可用性；内部 Repair 模式才读取存储字节做深度校验。桥接获得 CanReuseFiles 跳过条件，不把它当成完整性证明；元数据和 Mania 缓存版本不与磁盘状态混合。实际安全写入仍由原生 Add 负责，适配器为失败事务及共享封面保留进程内待通知 set IDs，提交后使用原队列通知缓存。2026-10-03 增加可选原生校验观察器，省去实际写入前为通知而重复的文件读取；不会扫描无引用文件、其他媒体或回放，没有持久化表或 UI 按键；[当前分流及验收限制](library-persistence-contract.md#普通刷新迁移与内部修复)单独记录。

旧投影必定进入写入，因此无需为“已最新”判断提前校验其字节。准备进度使用不含 Realm 对象的 Host/Library 契约，具体查询、引用及校验仍在适配器；通知映射与本地化在 Host/Configuration。0/0 诊断仅打开停机备份的独立只读副本并抽查两个存储文件，没有在客户端原库执行写入、迁移或启动清理。

源读取与登记路径匹配的本轮指纹复用在 Host/Library，不依赖托管对象。普通更新保留每源一次内容读取，跳过判断不再次读取完整源；未扫描登记路径及准备/提交前变化仍需验证。逐批的全库托管索引已经删除，改为事务内原生查找；完整证据与待验收范围见存储契约。

旧 MD5 所有者计数也留在 Realm 适配器；使用原生查询，只计数该源改变的 MD5，模型修改前取得一致快照，仍包含大小写碰撞、其他 ruleset 及待删除对象。没有新增存储缓存或跨事务托管对象。当前刷新分流版已备份安装，904 项相关回归及全部分层检查通过；更新完成与速度待客户端复测，记录见[存储契约](library-persistence-contract.md#当前分流验证与安装)。

## F 成绩的结果标记

EX 锁血与完整计分不进入 Realm。Host 输出已有 ScoreInfo.Rank=F、Passed=false，
原生存储保留 Rank；Passed 标为忽略字段，正式 v5 读取与独立导入恢复临时状态。
未来个人记录按 Rank 排除 F、原始记录继续保留，不需要当前新增 Realm 字段或 schema。
此政策是跨后端数据语义；具体字段序列化仍由所选存储适配器负责，见[存储契约](library-persistence-contract.md#ex-零血成绩与未来个人记录)。
