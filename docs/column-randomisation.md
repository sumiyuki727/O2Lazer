# 随机轨道变换契约

维护状态（2026-10-06）：现行随机契约；三项入口；旧算法隐藏保留，随机工作已结束。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-06。RD 沿用原生 Mania Mod、Seed 控件、图标、排序、倍率和计表现分策略。
常规菜单为 Random、R-Random、S-Random（使用 Panic 算法）。原版对齐阶段已按用户决定结束，
未选细节暂缓；EX 零血的当前规则另见[玩法规范](o2jam-behaviour-spec.md#life)，不由随机模块决定。

## 算法、身份与参考

| 菜单 | 固定设置值 | 行为 |
|---|---|---|
| Random | Native = 0 | 直接调用原生 Mania 的整曲轨道置换；缺少 algorithm 的既有回放仍走这条路径 |
| R-Random | RRandom = 3 | 七键整体轮转，允许镜像轮转；排除正规与纯镜像，共 12 种布局 |
| S-Random | Panic = 2 | 使用已实现的 O2Jam 原始小节/保护段置换，算法和种子序列不变，仅修改显示名称 |
| 隐藏：旧逐音符 S-Random | SRandom = 4 | 保留逐音符随机、40 ms 保护及长条/和弦算法；不能从菜单重新选择，已有成绩/回放仍按此身份播放 |

O2Jam = 1 保留为退役的原版普通随机身份，避免破坏已经录制的布局。常规菜单隐藏它，
只有当前设置本来就是该旧值时才显示；打开设置不会自动改成另一算法。离开旧值后从菜单移除。
旧逐音符 `SRandom = 4` 即使已经选中也不加入菜单；原生下拉头部显示 `S-Random (legacy)`，
不自动改写其值或 Seed。离开该值后只能选择常规三项。新显示名称 S-Random 始终选择
`Panic = 2`，不把任何身份重编号或切换到别的算法；所有选项仍通过原生 ModsJson 保存。
算法标题 Algorithm、名称和说明按用户要求固定英文，O2LazerStrings 的七个入口直接读取英文基础资源，
不随当前或未来新增语言绑定变化；所有翻译资源也保持相同英文文本。

R 与保留的逐音符算法参考 beatoraja 提交 `ad42f56c4658e968f93b24bf23440fe51cb9878e` 的
[LaneShuffleModifier](https://github.com/exch-bms2/beatoraja/blob/ad42f56c4658e968f93b24bf23440fe51cb9878e/src/bms/player/beatoraja/pattern/LaneShuffleModifier.java) 与
[Randomizer](https://github.com/exch-bms2/beatoraja/blob/ad42f56c4658e968f93b24bf23440fe51cb9878e/src/bms/player/beatoraja/pattern/Randomizer.java)。
根据行为独立实现，不将参考工程或源文件纳入交付。参考文本、摘要及固定 Java 对照位于忽略目录
`.artifacts/beatoraja-random-reference` 和 `.artifacts/beatoraja-random-oracle`。

用户最初选择 R 包含镜像轮转、逐音符 S 保留 40 ms 保护；实际 O2Jam 谱面测试后决定隐藏后者，
由 Panic 作为当前 S-Random。隐藏实现仍保留以下原始契约。IIDX 官方仅描述逐音符随机与旧版额外的
[H-RAN 连打抑制](https://p.eagate.573.jp/game/2dx/28/howto/play/option.html)，未公开毫秒阈值；
不把 beatoraja 的 40 ms 宣称为 IIDX 的精确规则。[mBMplay 作者说明](https://mistyblue.info/mbmplay.html)
提供类似规避，其 BPM150 三十二分音符间隔换算为 50 ms，不能据此推出统一标准。
Panic 原版地址和 CRT 证据仍以[原版追踪](original-random-and-failure-analysis.md)为准。

## 保留的逐音符算法：40 ms、和弦与长条边界

- 使用原始谱面连续毫秒时间，间隔严格大于 40 ms 的空闲轨道优先；恰好 40 ms 也受保护。
  不改变音符时间、判定规则、KS 或自动音频；不将时间取整为参考程序的整数毫秒。
- 优先轨道不足时，选距上次音符最久的可用轨道，等距时随机。完整七键和弦等情况可能强制
  产生短间隔，因此这是轨道分配保护，不是无条件最小间隔保证；不删音符、不挪时间、不拆和弦。
- 长条轨道直到尾部所在时点处理完才释放，同一时点其他源轨道不能占用它。之后尾部时间也参与
  40 ms 恢复保护；这是对 O2Jam 长条可玩性的明确扩展，参考源码只更新自由源轨道的历史时间。
- 已有同源轨道的重叠长条、长条内单点及尾部接新长条保留原布局关系，锁定到最晚尾部。
  零时长长条不会造成永久锁定。避免增加新的冲突，不修复或删除谱面原有重叠。
- 同一时点不同源轨道一对一分配。无音符的自由源轨道也消耗 PRNG，只有尾部的时点仍参与
  变换；不忽略这些调用，以免之后种子序列漂移。
- HT/DT 等仍修改原生游戏时钟；算法使用谱面时间，因此 40 ms 不是变速后的实时时间保证。
  相同算法/Seed 的轨道布局不因 MS、DT/HT 或客户端运行时版本而改变。负 Seed 也稳定复现。

PRNG 显式实现 Java 的 48 位递推及有界整数抽样，避免 System.Random 版本变化影响回放。
R 将参考的目标到源映射取逆后写入 Column，保持轮转和镜像方向。隐藏的逐音符算法支持任意正键数，
R 支持至少两键；当前宿主适配固定为 O2Jam 七键。输入仅有列、头尾时间，返回与输入同序的列数组。

## 分层与原生复用缺口

`Integration/Beatmaps/Transforms/ColumnRandomizer` 是不依赖 osu 类型的具体算法，可供其他
ruleset 使用；`O2JamColumnRandomizer` 只承载 O2Jam 原版规则。
`O2JamRandomBeatmapTransform` 将每局工作谱面的物件转换为纯输入并写回 Column，利用原生
HoldNote.Column 的头尾传播。Host 负责选项和 Seed；Core、Drawable、音频与 Realm 不参与算法。

原生 Mania Random 可以满足整曲置换，继续直接使用。原生没有带上述约束的轮转、逐音符
随机或 Panic，才新增这些具体变换；不建立第二套 Mod 管线或抽象协议。
原生 SettingSource 要求具体控件类型，且枚举下拉没有隐藏退役值的接口，因此
`O2JamRandomAlgorithmDropdown` 继承原生 SettingsEnumDropdown，常规 Items 只包含三项。
O2Jam 旧值用原生 AddDropdownItem / RemoveDropdownItem 按选择状态增减，避免重新赋值 Items
时原生校验将隐藏的逐音符值重置为第一项；后者由原生非菜单选择显示支持，不需要补丁。
语义边界仅允许该 SettingControlType 的具体类型引用，没有放开 Host 到 Presentation 的一般依赖。

流速滑块与设置通知共用 O2LazerStrings 的数值格式，例如 `300毫秒 (28.0速 · ×3)`。
原生格式没有括号内追加字段的接口，所以提供这一格式模板；继续使用原生流速计算、滑块和
通知，不解析已本地化的字符串，英语和中文资源同步更新。

## 验证与客户端验收

此前算法扩展版 158 项过滤回归通过；模块、存储、计分及完整语义分层检查均通过，违规和过期例外为 0。
过滤测试覆盖 Java 固定向量、12 种轮转、40 ms 连续边界、长条占位/尾部恢复、密集和弦回退、
输入顺序/其他键数、原版 Panic 不变、旧算法菜单兼容、正式回放种子还原、MS/Invert/DT/HT、
音符/KS/时间保持及双语格式。Java S 向量由固定的未改动参考 Randomizer 加最小模型桩执行得到，
它们不代表尾部恢复扩展与参考完全相同。未运行 benchmark。

本轮隐藏/重命名、固定英文与原生语言绑定版 177 项过滤回归通过，报告 `.artifacts/test-results/random-menu-final.trx`；
覆盖常规三项菜单、隐藏旧值不被改写、旧 O2Jam 与隐藏值之间切换没有默认值中间态、
新 S-Random 与原 Panic 身份一致，以及中英文名称/说明固定英文。纯算法及各身份回放向量保留原验证。

菜单版 DLL 已于 2026-10-05 17:36:07（JST）在 lazer 关闭后备份安装，
安装记录 `.artifacts/install-random-menu.json`，SHA-256 `E25F1347E58398D20AF55DDE3FB49E03CD64A8A914505FE56A9C40B919E85518`。

标题 Algorithm 固定英文追加验证：75 项随机设置/本地化及原生语言绑定测试通过，
资源与源码边界检查通过。标题版 DLL 于 2026-10-05 17:44:23（JST）停机备份安装，
报告 `.artifacts/test-results/random-caption.trx`，安装记录 `.artifacts/install-random-caption.json`。

客户端需验收：RD 三项菜单及 Seed；R 的镜像/轮转；S-Random 按原 Panic 小节/保护段变化与重复开局；
KS 跟随音符；录制并播放含 R/S 的正式回放；中文流速只出现一个括号组。自动测试不能替代该验收。
