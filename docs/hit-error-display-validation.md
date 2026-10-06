# 误差条显示与验收记录

维护状态（2026-10-06）：现行几何契约及历史证据；旧监听/旧安装 Hash 不表示当前运行版。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-05。用户已确认 OD7 黄300基准与 MS 变速修复表现正常，临时
误差条监听已从源码、构建开关、挂钩和诊断测试中移除；正式 DLL 不再输出
`[O2UR/v1]`，也不保留该组件的更新订阅。音频诊断是独立功能，不受这次清理影响。

## 当前显示契约

非 MS 的 Legacy/Argon 色区为 −25…+26 O2Jam tick；COOL/GOOD/BAD 的
早/晚边界分别为 6/7、18/19、25/26 tick。早侧 COOL 为 Mania OD7、1× 黄300
整体宽度的一半；晚侧沿用原刻度增加 1 tick，不缩窄早侧，也不移动真实零点。
每 tick 保持 7.25 投影单位；原生轴用 ±26 tick 的对称包络，早侧留一个透明 tick。
Legacy 轴总宽 301.6，本地色区总宽 295.8，COOL 相对零点为 −34.8…+40.6；
Argon 原生根轴按 188.5/130.5 延长，分别调整早晚原生色块。厚度、渐变、动画及
HUD 设置继续复用原生。上述数值是本地组件单位，父级和皮肤缩放仍由原生处理。

Core 使用 `[-6,7)`、`[-18,19)`、`[-25,26)` 的连续判定，不取整输入位置。
新 v5 回放带 `judgement_version=20261005`；已有无标记 v5 测试回放按新规则
重判，允许边界处播放结果与保存成绩不同；保存成绩不改写。

BAD 色区及最终 BAD 标记使用原生 Mania 50（HitResult.Meh）的黄色。适配仅
改变已使用 O2Jam 显示窗口的原生误差条取色参数；BAD 的实际判定仍为 Ok，
不会修改分数、准确率、回放或全局 OsuColour。药丸救回的 BAD 仍显示最终 COOL
颜色。MS、原生 Mania、非位置颜色表、自定义子类保持原生配色。

MS 的 HT/DC/DT/NC 判定窗口复用原生 IManiaRateAdjustmentMod；Legacy 随窗口
调整宽度，Argon 保持其原生归一化显示行为。默认 OD7 的 Legacy 本地外层宽度
为：1× 208.8、DT 1.5× 312.8、HT 0.75× 156。非 MS 的宽度不随 BPM 或速率改变。
数值 UR 继续使用原生毫秒统计，tick 显示不改写原始 TimeOffset 或游戏时钟。

## 已完成的验证

COOL 基准和 MS 速率修复先通过普通配置 178 项、临时监听配置 118 项及 BMS
两个独立载入顺序各一项过滤回归。默认/自定义速率、HR/EZ/Classic 和 Mod 顺序
与对应原生政策一致；Classic 按原生外部规则集的转换谱面政策比较。
对应历史报告为 `.artifacts/test-results/ms-rate-cool-*.trx`。

旧外层宽度基准的客户端记录 `1791132241.runtime.log` 中，三个有效 Legacy
会话共校验 5764 次命中、467 次几何采样，位置/颜色/平均值/尺寸异常均为 0；
覆盖单点、长条首尾及 77–300 BPM。这个旧版本的外层宽度为 208.8，不能代替
当前宽度的验证。

改用 COOL 基准后的客户端记录 `1791135507.runtime.log` 中，三个 Legacy 会话
共 112 次命中、20 次几何采样，四类异常均为 0；命中记录包含 1× 和 1.5×，
外层宽度始终为 290。用户随后回复“没什么问题了”，确认本轮有界复测正常。
MS 不创建 tick 监听会话；它的实际表现以原生对照测试和用户复测为依据。

本次移除监听和调整 BAD 黄色的过滤回归 120 项通过，核对 Legacy/Argon 实际
色区、标记、宽度、平均值、清空、MS/原生 Mania 隔离以及判定不被配色改写。
BMS 双顺序各一项、架构检查器 15 项通过。报告为忽略目录中的
`hit-error-bad-colour.trx`、`hit-error-colour-bms-first.trx`、
`hit-error-colour-bms-last.trx` 和 `hit-error-colour-architecture.trx`。
不运行 benchmark 或未过滤测试集，不改写客户端配置、谱面或成绩库。
BAD 新配色的客户端视觉复测尚待确认。

三个模块/计分/存储边界脚本通过。语义分层检查的编译错误、依赖违规和过期
例外均为 0，现有跨层 partial 数量保持为 2；没有新增例外。

2026-10-05 02:55:12，确认 lazer 关闭并备份原 DLL 后，将不含误差条监听的
正式版安装到 `D:\osu-lazer\rulesets\osu.Game.Rulesets.O2Lazer.dll`。
安装后 SHA256 为 `BCFDABE5680DB57FEC639DBFD152182E73CFD2804686312013BA4B547EC528FE`，
与已测试构建一致。安装记录为 `.artifacts/install-hit-error-bad-colour.json`，
原 DLL 保存在 `.artifacts/client-backups/hit-error-bad-colour-20261005-025512`。


## 连续不对称窗口与显示验证（2026-10-05）

用户选择将判定内核与显示一并修改，旧回放只作为测试数据，无需双算法兼容。
396 项不同过滤测试通过：独立 Core 84，宿主 283，生命周期/正式回放导入 27，
BMS 双载入顺序各 1。报告为 `.artifacts/test-results/asymmetric-*.trx`。
覆盖三个端点的早含/晚不含、负位置、非整 tick 目标、跨 BPM 及原生生命周期
包络；实际 Legacy/Argon 色块边界、连续命中标记、移动平均、清空、MS 隔离、
药丸最终颜色、长条视效、回放回退和键音行为。新回放字段与未知版本拒绝也已验证。

新增 Legacy.load 挂钩时发现 JIT 可能内联尚未适配的原生取色入口；调整为先
安装颜色入口，再重编译加载方法。修复后 BAD 色区及标记仍为原生 Mania 50 黄色。
三个边界脚本和语义架构检查通过，零编译错误、零越层违规、零过期例外，现有
跨层 partial 仍为 2。本轮未运行 benchmark 或未过滤测试，不操作真实客户端数据库。
当前不对称显示和新判定的客户端游玩验收待用户完成。

2026-10-05 04:11:41 已备份并安装到客户端，安装 SHA256 为
`24E88BDC527116BD029ED0CC0376D94E593D926AC9F8F644666963F7EF7BC3AF`，
与测试构建一致。DLL 为 3,536,896 字节，较移除监听后的上一正式版增加 3,072 字节。
安装记录 `.artifacts/install-asymmetric-hit-error.json`；旧 DLL 位于
`.artifacts/client-backups/asymmetric-hit-error-20261005-041141`。
