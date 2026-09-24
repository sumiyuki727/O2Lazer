# 变速 Mod 与 OJM 音频契约

更新：2026-09-24。当前代码已在目标 osu!lazer 2026.921.0 下通过过滤回归；下述 TrackBass/SampleChannelBass 后端细节最初按 2026.804.2/框架 2026.731.0 源码审阅，不能把它误写成新版后端重新审计或实机听感验收。R06 后续需要复查新框架实现及实际设备。

O2Jam 音乐事件以谱面时间安排。HT/DT 默认保留音高，Adjust Pitch 同时影响 BGM 与玩家键音；DC/NC 使用 Mania 的固定变调语义，NC 保留原生节拍音。WU/WD/AS 暴露实时 `SpeedChange`，宿主绑定同一速度到事件轨和视觉流速。判定仍由谱面位置驱动，不能对回放时间或音符时间再乘一次速率。背景音与 KeySound 不受全局效果音量影响。

| 路径 | 当前实现 | 应验证的区别 |
|---|---|---|
| BGM 与较大自动音 | `O2JamPreviewTrack`、`O2JamBeatmapSkin` 用原生 Track；自动音 ≥512 KiB 走流式轨 | 连续音源须跟随速率、可寻址并在暂停/恢复后对齐事件钟 |
| 小型自动 KeySound | 原生 Sample/Channel，按事件钟触发 | 其一次性包络不因 Tempo 属性自动拉伸；未来若要求全部键音体拉伸，属于新行为设计 |
| 玩家触发 KeySound | `O2JamHitSoundRateAdjustments` 限定 O2Jam endpoint 的原生可调音频 | Frequency/Adjust Pitch 与实时速度一致，不改全局 UI/效果音 |
| 视觉下落 | `O2JamDrawableRuleset` 读取原生 Mod 的速度 bindable | BPM 与 rate 的视觉补偿不反向改变判定 |

当前模式为预览自动播放可演奏键音，游玩则由玩家输入触发；同曲不同难度只在背景编排兼容时转移原生轨道。`O2JamWorkingBeatmap` 懒加载外部 OJN/OJM，缓存与预加载负责避免快速切曲阻塞，但文件变化、取消和释放仍是[问题 A09](architecture-audit.md#待处理问题)的系统审查项。

相关实现：`Host/Audio/O2JamPreviewTrack.cs`、`O2JamBeatmapSkin.cs`、`O2JamHitSoundRateAdjustments.cs`、`Integration/Beatmaps/O2JamWorkingBeatmap.cs`。自动测试覆盖速率与通道绑定、播放/寻址状态；实际设备的音量、起音、暂停恢复和长曲资源行为仍需客户端验证。[可选音频追踪](audio-sync-diagnostics.md)只记录观测，不自动调整偏移。
