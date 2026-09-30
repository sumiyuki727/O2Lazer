# 当前 Realm 存储边界

更新：2026-10-01。这里记录现状，不预设 osu! 未来存储技术，也不授权删除或重写用户数据。[A04 设计](library-persistence-contract.md#重设计草案的范围与分层)中的 A04-1 已实现统一投影/版本并通过临时库验证；完整身份迁移、去重/通知和文件预留仍待后续步骤。重构前的测试 replay 不在兼容范围。

生产代码中直接使用 Realm/RealmAccess/RealmFileStore/RealmUser 的文件集中在 `osu.Game.Rulesets.O2Lazer/Host/Persistence/Realm`：

| 文件 | 当前职责 |
|---|---|
| `O2JamLibraryWriter.cs` | 谱面、来源文件、标签版本与事务写入；保留现有谱面 ID 的更新路径 |
| `O2JamRealmLibraryBackend.cs` | 将导入会话接到写入器、收藏夹与宿主缓存失效 |
| `O2JamSourceFolderCollectionService.cs` | 文件夹收藏夹的数据库操作 |
| `O2JamReplayPersistencePatch.cs` | 原生成绩/replay 导入导出接入和原生文件存储 |
| `O2JamSongSelectRankPatch.cs` | 原生 Realm 成绩集合通知；选中成绩策略在 Presentation 的同名 partial |
| `O2JamSettingsSubsection.Realm.cs` | 原生依赖注入组装后端；页面控件位于 Presentation 的同名 partial |

`Host/Library` 定义 `IO2JamLibraryWriter`、导入请求/结果和扫描/应用流程；`Host/Configuration/O2JamLibrarySettingsSession` 接受后端工厂，设置视图不持有 RealmAccess。`Host/Replays` 的 v5 归档编码、回放录制和输入也不直接引用 Realm。`scripts/check-storage-boundary.ps1` 搜索生产源码中的直接 Realm 类型；`scripts/check-architecture.ps1` 另外按真实声明文件检查本仓库跨层依赖，二者均由 `scripts/verify.ps1` 调用，2026-09-30 检查通过。

隔离的限度：宿主仍使用 osu! 原生数据库模型，主 ruleset 项目仍引用 Realm；两个 partial 接入点和原生 `ScoreManager`/`BeatmapInfo` 类型仍与具体后端共同编译。A12 将两个 partial 的跨层成员引用和准确声明文件列为[精确例外](clean-rewrite-architecture.md#允许依赖矩阵与例外)，防止范围无意扩大；语义检查不证明外部原生模型与 Realm 的传递依赖已经消除，也不证明可直接替换 SQL。当前 `IO2JamLibraryWriter` 是现有调用接口，不是已敲定的未来存档协议。

[A04 身份与字段清单](library-persistence-contract.md)记录了当前契约和已确认的旧成绩策略。重设计顺序：先列每个字段的来源、含义、读写方、版本和更新条件；区分源文件、set、难度和成绩/replay 身份；定义缺失/移动/重复源的更新与回滚；最后在临时数据库演练新旧关联和迁移。旧 schema 的读取/清理策略需单独确认，不因 Realm 停用传闻提前删除任何用户记录。本地/服务器档案关系仍属路线图 R10 待决项。

2026-10-01 源码核对确认 `RealmFileStore.Add` 已提供安全文件写入和内容校验，但新文件记录随模型事务回滚时，磁盘文件不属于原生零引用行清理的查询范围。草案建议先提交原生 RealmFile 预留行，再在独立模型事务中写文件/发布引用，以复用现有清理能力；该两事务恢复方案仍归 A04-4，尚需故障注入与共享文件保护测试，不把它误写成已实现的原子文件回滚。

A04-1 的源快照及提交前只读句柄验证位于 Host/Library，原生 Tags 载体编解码也位于该宿主桥接目录，供元数据读写方共用；它们不返回 Realm 托管对象，不把 Tags 协议当成独立 Core/Formats 或未来存储 schema。Realm 适配器共用字段投影和原生文件引用同步，难度复制及作者快照复用原生 CopyTo/DeepClone。具体 Realm schema 与当前两个 partial 接缝未改。全脚本 922 项过滤回归及全部边界检查通过；没有安装到客户端或对真实库执行写入。
