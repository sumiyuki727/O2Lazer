# 变速 Mod 与 OJM 音频契约

维护状态（2026-10-06）：现行音频/速率契约；保留已确认 KS 行为及小型 KS 中段尾音限制。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-06。当前目标为 osu!lazer 2026.1005.0；历史音频专项最初在 2026.921.0 验证，
当前版本已通过[收尾过滤回归](recent-changes-review.md#自动验证与部署边界)；暂停和声道生命周期对照了本地 osu!framework 源码。自动测试不等于实际音频设备上的同步和听感验收。

O2Jam 音乐事件以谱面时间安排。HT/DT 默认保留音高，Adjust Pitch 同时影响 BGM 与玩家键音；DC/NC 使用 Mania 的固定变调语义，NC 保留原生节拍音。WU/WD/AS 暴露实时 `SpeedChange`，宿主绑定同一速度到事件轨和视觉流速。普通 O2Jam 判定仍由谱面位置驱动；MS 的 HT/DC/DT/NC 判定窗口则复用原生 Mania 速率补偿。不能对回放时间或音符时间再乘一次速率。背景音与 KeySound 不受全局效果音量影响。

| 路径 | 当前实现 | 应验证的区别 |
|---|---|---|
| BGM 与较大自动音 | `O2JamPreviewTrack`、`O2JamBeatmapSkin` 用原生 Track；自动音 ≥512 KiB 走流式轨 | 连续音源须跟随速率、可寻址并在暂停/恢复后对齐事件钟 |
| 小型自动 KeySound | 原生 Sample/Channel，按事件钟触发 | 其一次性包络不因 Tempo 属性自动拉伸；未来若要求全部键音体拉伸，属于新行为设计 |
| 玩家触发 KeySound | `O2JamHitSoundRateAdjustments` 限定 O2Jam endpoint 的原生可调音频 | Frequency/Adjust Pitch 与实时速度一致，不改全局 UI/效果音 |
| 视觉下落 | `O2JamDrawableRuleset` 读取原生 Mod 的速度 bindable | BPM 与 rate 的视觉补偿不反向改变判定 |

`O2JamPreviewSchedule` 仅从已转换的谱面构造音频事件；它保留 `SampleId = 0` 与缺失 OJM 音色的原有路径，不重新映射音符。`O2JamPreviewTrack` 拥有预览与游玩的共同事件钟，并按谱面时间派发 BGM、自动 KS 和可演奏 KS。某个流的解码尚未完成时，该流保持顺序等待，其他已就绪流仍可派发。启动时则先等待所有到期音源准备好，以免开头静音。较大的自动音按原有 512 KiB 门槛走可寻址原生 Track；较小的自动音和可演奏键音仍走原生 SampleChannel。

选曲预览自动播放可演奏键音；普通游玩与回放由原生命中路径触发，回放漏掉的音符不会补播。预留的 `GameplayAutomatic` 策略改为按谱面时间自动播放可演奏键音，并只对当前 OJM 皮肤关闭判定音查找，避免重复播放。`O2JamDrawableRuleset.AutomaticallyPlayKeySounds` 是未来 Mod 可接入的宿主入口，目前没有可选的 Mod 或用户界面，也不改变计分规则。全局“谱面打击音效”开关无法静音普通 OJM 键音；其他皮肤和音效仍服从原生开关。

玩家触发的游玩 KS 按 OJM 样本身份在当前游玩内停旧重播：未判定物件的误触、连续误触与首次命中共享替换策略；单点/LN 头判定后不可再被列级空击选中。另一个未判定物件使用同 KS 仍可触发。策略位于 Host.Audio，复用原生声道，不改 Core、映射或零/缺失样本处置；MS 也保留这套音乐播放策略，其他 ruleset 不变。预览、背景与预留自动 KS 路径保留原有并发。

选曲与游玩暂停会停止共同事件钟和 BGM，并在原声道上冻结正在播放的小型 KS；恢复时不从头触发。跳转会重建 BGM 所处的谱面位置、丢弃跳转前的小型自动音尾音，并让后续事件按目标时间重新派发。游玩内原生命中 KS 的旧尾音也在 `GameplayClockContainer.OnSeek` 停止，回放向后跳转后重新经过的命中仍由原生回放输入触发。原生 `SampleChannel` 没有按音频内部偏移寻址的接口，因此跳到既有小型 KS 的中途时不会重建那段尾音；用户已在客户端观察到此限制并决定暂时搁置。

同曲不同难度只在背景编排兼容时转移原生轨道。Integration 的 `O2JamExternalChartResources` 懒加载外部 OJN/OJM 并核对 OJM 文件戳，Host 的 `O2JamWorkingBeatmap` 负责原生轨道及皮肤接入。缓存失效、取消和释放已按[问题 A09](architecture-audit.md#初始审查问题)完成定向检查及有界客户端复测，边界与剩余限制如下。

资源所有权按以下边界处理：

| 资源 | 持有与创建 | 失效与释放 |
|---|---|---|
| OJN 文档、OJM 索引 | 进程级 `OjnDocumentCache`（最多 128 文档）和 `OjmArchiveCache`（最多 12 归档，按已登记解码字节设 384 MiB 上限）按文件戳复用；集合与索引冻结，公开字节复制，轨道流只读且不复制音频块 | 失败读入从缓存剔除；成功导入或显式刷新写入后，按 OJN 源路径清除两类缓存。已交给工作谱面的对象不会热替换 |
| 工作谱面与 OJM 皮肤 | `O2JamWorkingBeatmapCache` 最多强引用六个包装谱面；选中后才开始 OJM 索引和皮肤预加载 | 原生 `WorkingBeatmapCache.OnInvalidated` 或本地逐出时请求释放皮肤；OJM 读取失败或文件戳变化的包装谱面下次选中不再复用，也不向新谱面转移旧轨 |
| 预加载工作 | 进程级 `O2JamPreloadScheduler` 限制同时工作的解码数；每个皮肤持有自己的取消令牌 | 皮肤释放时，排队工作立即取消并退出队列；正在执行的工作收到取消，若仍返回无人接收的原生对象则直接释放 |
| 正在播放的事件轨 | `O2JamPreviewTrack` 持有皮肤租约和自己的原生子轨、声道；协调器只保留弱引用 | 事件轨先释放子轨，再归还租约；皮肤等最后一个播放租约归还后才释放原生 Sample/Track 存储 |

仅 OJM 内容变化而文件长度与时间戳均未变时，选曲不会自动识别；手动刷新会失效已导入谱面的 OJM 缓存，即使 OJN 被判定为未变化。已加载的工作谱面在下次选中时不再复用旧归档，正在播放的曲目不会热切换音源。用户于 2026-09-29 确认手动刷新表现正常；此前有界音频复测除上述小型 KS 尾音限制外未报告异常。全曲库听感不在这些验证范围内。

相关实现：`Host/Audio/O2JamPreviewTrack.cs`、`O2JamBeatmapSkin.cs`、`O2JamHitSoundRateAdjustments.cs`、`Integration/Beatmaps/O2JamExternalChartResources.cs`、`Host/Beatmaps/O2JamWorkingBeatmap.cs`。自动测试覆盖速率与通道绑定、事件顺序、暂停与跳转；有界客户端复测无其他异常，不代表所有实际设备、长曲或谱面均已验收。[可选音频追踪](audio-sync-diagnostics.md)只记录观测，不自动调整偏移。

## 游玩 KS 重触发验证（2026-10-04）

本轮按用户实测后的选择实现 C06 的限定策略，没有新增判定算法、调整 KS 映射或
改变预览/背景混音。原 `O2JamHeldKeySoundPatch` 改为 `O2JamResolvedKeySoundPatch`，
继续使用同一个原生选音接缝，并覆盖单点末尾回退及 MS 的 OJM 物件。

最终 201 项音频/游玩/回放过滤回归通过，另在独立进程验证 BMS 两种载入顺序各一项，
共 203 个不同用例，无失败或跳过。报告位于忽略目录 `.artifacts/test-results/`：
`keysound-retrigger-final.trx`、`keysound-bms-first.trx`、`keysound-bms-last.trx`。
模块、存储、计分及语义依赖检查通过，无新增分层例外；没有运行 benchmark 或
未过滤测试集，也没有改写客户端谱面/成绩数据库。

原生 BASS 无声设备测试检查新声道起始偏移为 0、停掉旧声道、三个暂停/恢复周期
保持新声道句柄和位置，并确认 seek/恢复不会复活旧尾音。其余用例覆盖误触与命中
跨实例替换、不同 KS/游玩隔离、原生 Mania 选音不变、判定后禁用及 rewind 恢复，
以及已有预览、自动音、全局开关、变速、LN 视效和正式 v5 回放路径。

客户端已关闭后备份并安装 DLL；SHA-256 为 `A6E41944DF25657C7ACF1F53A1210F636A567EE8AB4089EDA62B3B2F4325B63B`，
旧版保存在 `D:/o2lazer/.artifacts/client-backups/keysound-retrigger-20261004-235113`。
用户于 2026-10-05 回复本轮测试没有问题，确认 KS 重触发改动通过有界客户端验收。
这次反馈不等同于所有谱面、音频设备或未来自动 KS Mod 的完整听感验证。
跳入已开始小型 KS 中段无法重建尾音的既有局限仍暂缓。


## MS 判定窗口的速率衔接（2026-10-05）

HT/DC/DT/NC 继承对应的原生 Mania Mod，保留其设置、变调及 NC 节拍覆盖层。
原生 IManiaRateAdjustmentMod 会把每个 Note 或 LN 首尾窗口直接转成
ManiaHitWindows，因此不能直接用于普通 O2Jam 物件。ManiaScore/Mods 的
O2JamManiaRateAdjustment 只将已转换的原生毫秒窗口交给该原生接口；每个 Mod
复用一个轻量适配器和原来的 SpeedChange bindable，不复制窗口公式或逐帧修改。

MS 的 DT/NC 原先只改变时钟，遗漏了窗口补偿，导致实际判定变严且 Legacy
误差条未变宽；现在两者随原生窗口一起调整，HT/DC 同步恢复原生语义。普通
O2Jam 的 tick 判定、固定显示宽度、音符时间和音频速率政策保持原有契约。
WU/WD/AS 并没有对应的原生 Mania 窗口补偿，本轮不扩展其判定政策。
Classic 仍按原生对外部 ruleset 的转换谱面政策处理，不改成 Mania 原生谱面政策。


本轮普通配置 178 项、监听配置 118 项定向测试及 BMS 两载入顺序均通过；对应
报告与客户端安装状态见[误差条记录](hit-error-display-validation.md)。
MS 判定窗口修复会改变真实窗口，并非仅改变绘制。曾在窗口补偿遗漏版本录制的
MS 变速回放，边界输入在新版本重算时可能产生不同判定；本轮不改写存量回放
或成绩文件。序列化、导入及默认/自定义速率测试不等同于旧窗口行为的兼容验收。
