# 变速 Mod 与 OJM 音频契约

更新：2026-09-27。当前代码已在目标 osu!lazer 2026.921.0 下通过音频定向过滤回归；暂停和声道生命周期对照了本地 osu!framework 源码。自动测试不等于实际音频设备上的同步和听感验收。

O2Jam 音乐事件以谱面时间安排。HT/DT 默认保留音高，Adjust Pitch 同时影响 BGM 与玩家键音；DC/NC 使用 Mania 的固定变调语义，NC 保留原生节拍音。WU/WD/AS 暴露实时 `SpeedChange`，宿主绑定同一速度到事件轨和视觉流速。判定仍由谱面位置驱动，不能对回放时间或音符时间再乘一次速率。背景音与 KeySound 不受全局效果音量影响。

| 路径 | 当前实现 | 应验证的区别 |
|---|---|---|
| BGM 与较大自动音 | `O2JamPreviewTrack`、`O2JamBeatmapSkin` 用原生 Track；自动音 ≥512 KiB 走流式轨 | 连续音源须跟随速率、可寻址并在暂停/恢复后对齐事件钟 |
| 小型自动 KeySound | 原生 Sample/Channel，按事件钟触发 | 其一次性包络不因 Tempo 属性自动拉伸；未来若要求全部键音体拉伸，属于新行为设计 |
| 玩家触发 KeySound | `O2JamHitSoundRateAdjustments` 限定 O2Jam endpoint 的原生可调音频 | Frequency/Adjust Pitch 与实时速度一致，不改全局 UI/效果音 |
| 视觉下落 | `O2JamDrawableRuleset` 读取原生 Mod 的速度 bindable | BPM 与 rate 的视觉补偿不反向改变判定 |

`O2JamPreviewSchedule` 仅从已转换的谱面构造音频事件；它保留 `SampleId = 0` 与缺失 OJM 音色的原有路径，不重新映射音符。`O2JamPreviewTrack` 拥有预览与游玩的共同事件钟，并按谱面时间派发 BGM、自动 KS 和可演奏 KS。某个流的解码尚未完成时，该流保持顺序等待，其他已就绪流仍可派发。启动时则先等待所有到期音源准备好，以免开头静音。较大的自动音按原有 512 KiB 门槛走可寻址原生 Track；较小的自动音和可演奏键音仍走原生 SampleChannel。

选曲预览自动播放可演奏键音；普通游玩与回放由原生命中路径触发，回放漏掉的音符不会补播。预留的 `GameplayAutomatic` 策略改为按谱面时间自动播放可演奏键音，并只对当前 OJM 皮肤关闭判定音查找，避免重复播放。`O2JamDrawableRuleset.AutomaticallyPlayKeySounds` 是未来 Mod 可接入的宿主入口，目前没有可选的 Mod 或用户界面，也不改变计分规则。全局“谱面打击音效”开关无法静音普通 OJM 键音；其他皮肤和音效仍服从原生开关。

选曲与游玩暂停会停止共同事件钟和 BGM，并在原声道上冻结正在播放的小型 KS；恢复时不从头触发。跳转会重建 BGM 所处的谱面位置、丢弃跳转前的小型自动音尾音，并让后续事件按目标时间重新派发。游玩内原生命中 KS 的旧尾音也在 `GameplayClockContainer.OnSeek` 停止，回放向后跳转后重新经过的命中仍由原生回放输入触发。原生 `SampleChannel` 没有按音频内部偏移寻址的接口，因此跳到既有小型 KS 的中途时不会重建那段尾音；此限制须在客户端听感验收时检查。

同曲不同难度只在背景编排兼容时转移原生轨道。`O2JamWorkingBeatmap` 懒加载外部 OJN/OJM，缓存与预加载负责避免快速切曲阻塞，但文件变化、取消和释放仍是[问题 A09](architecture-audit.md#待处理问题)的系统审查项。

资源所有权：`O2JamWorkingBeatmapCache` 最多强引用六个包装谱面，逐出时请求释放其 OJM 皮肤；仍在播放的 `O2JamPreviewTrack` 持有皮肤租约，先释放子轨再归还租约。皮肤取消自己的预加载任务并释放原生 Sample/Track 存储。OJM 归档读取失败或 OJM 文件戳已变化的包装谱面，在下一次选中时不再复用；旧轨不能转移给新包装谱面。`OjnDocumentCache` 和 `OjmArchiveCache` 是按文件戳复用的进程级缓存；成功导入或显式刷新写入后，导入服务按源路径清除这两类缓存，以覆盖同长度、同时间戳的 OJN 内容变化。仅 OJM 内容变化、文件长度与时间戳均未变时，仍无法自动识别；正在播放的曲目也不会热切换音源，属于 A09 剩余场景。

相关实现：`Host/Audio/O2JamPreviewTrack.cs`、`O2JamBeatmapSkin.cs`、`O2JamHitSoundRateAdjustments.cs`、`Integration/Beatmaps/O2JamWorkingBeatmap.cs`。自动测试覆盖速率与通道绑定、事件顺序、暂停与跳转；实际设备的音量、起音、暂停恢复、快速切曲和长曲资源行为仍需客户端验证。[可选音频追踪](audio-sync-diagnostics.md)只记录观测，不自动调整偏移。
