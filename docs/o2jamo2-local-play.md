# O2JamO2 本地对照环境

维护状态（2026-10-06）：历史本机对照操作指南；本轮不启动客户端/服务端，也不继续部署 Classic。
文档职责与最新状态入口见[索引](README.md)。

2026-10-04 已由用户确认可以进入游戏并游玩。环境只用于原客户端行为对照，
不属于 O2Lazer 的 Core、Integration、Host 或产品模块，也不加载 O2Lazer 的补丁。
Classic 暂不部署。

## 已部署环境

所有客户端资产、服务端程序、账号、数据库和日志放在忽略目录
`D:\o2lazer\.artifacts\o2jamo2-local`，不随 Git 提交。

| 项目 | 当前配置 |
|---|---|
| 原客户端 | 从 `E:\Downloads\O2JamO2.zip` 提取，未执行安装器或更新器 |
| 主程序 | `client\OTwo.exe`，SHA-256 `2c8ac19bc26a7e1cfde527f5871c22b14fcc5473137101f4be849435d99b3102` |
| 资源校验 | 313 个成员，904,226,567 字节；长度和安装目录 MD5 已核对；派生哈希记录在 `client-manifest.json` |
| 服务端 | Identity.Encore 7.2.0，Windows x86 自包含包，协议 5.89 |
| 服务端包 SHA-256 | `dfd1ba0fcbc167d9e133aab505b3ee74586f40f31e5199cbccf87303cf5b9652`，与 GitHub 发布信息一致 |
| 服务端 EXE SHA-256 | `f6327cd955f9644ec6eb228d07cdb523e8862978f0d1f468e7bf7e85cacd6f09` |
| 网络 | Full 模式，服务端仅监听 `127.0.0.1:15010`；HTTP 服务关闭 |
| 存档 | `server\O2JAM.db`，独立 SQLite，不接触 lazer Realm、成绩或回放 |
| 账号 | 本地测试账号 `localtest`；随机密码保存在本机 `account.json`，无需官方账号 |
| 曲库 | 安装包附带 149 份 OJN；客户端报告 148 首曲目，原目录共 395 项 |
| 选曲权限 | 服务端 FreeMusic 解除购买限制，不修改客户端判定或音频代码 |

来源：[Identity 7.2.0 发布页](https://github.com/SirusDoma/Mozart.Encore/releases/tag/v7.2.0)。
参考源码提交为 `9d87c505b2220f5e0cf5a627d61ce649584cd1bb`；兼容条件及命令见
[Identity 说明](https://github.com/SirusDoma/Mozart.Encore/tree/main/Source/Identity)。

## 启动与退出

1. 双击 `.artifacts\o2jamo2-local\Start-O2JamO2.cmd`。入口启动或复用后台服务端，
   自动授权本地账号并启动原客户端。游戏内按原版流程选择服务器、频道和房间。
2. 选择已有本地文件的曲目。缺失曲目不会自动下载；FTP 参数指向本机占位地址，
   当前没有运行 FTP 服务。
3. 正常退出游戏。后台服务端保持运行，之后再次启动可以复用。
4. 不再需要对照时，双击 `Stop-O2JamO2-Server.cmd` 停止本环境服务端。
   游戏未退出时，该入口拒绝停止，避免打断游玩。

入口使用当前机器已有的 Codex 配套 PowerShell 7，未修改 Windows 脚本执行策略。
它们不调用原安装包的登录器、更新程序，也不自动运行其他客户端。

启动逻辑为仓库中的 `scripts\o2jamo2-local.ps1`：

```powershell
pwsh -NoProfile -File .\scripts\o2jamo2-local.ps1 -Action Check
pwsh -NoProfile -File .\scripts\o2jamo2-local.ps1 -Action Start
pwsh -NoProfile -File .\scripts\o2jamo2-local.ps1 -Action Stop
```

`Check` 只读验证主程序、服务端哈希、必要数据、监听地址及进程身份。`Start` 避免重复
启动本客户端，端口被其他进程占用时拒绝启动。`Stop` 同时核对服务端路径和创建时间，
避免 PID 被复用后误停其他程序。移动仓库时先退出旧进程，并保留本地资产与运行时。
没有本地资产的新 checkout 不能仅凭脚本运行原客户端。

## 验证范围

用户确认可以游玩。日志记录了本地授权、进入频道 0、曲目/角色同步、创建房间、
选择 `o2ma166` EX、开始游戏、完成加载及退出游玩的流程，未见服务端异常。
这证明登录和基础游玩链路可用，不代表所有曲目、完整结算和玩法差异都已验收。

启动器验证已通过：只读预检、服务端停止与重启、原客户端启动、重复启动保护以及
游戏运行时的停止保护。检查记录在本地 `launcher-smoke-check.json`。
后续每次启动服务端使用独立的 `server-*.stdout.log` / `server-*.stderr.log`，
当前日志路径与进程身份记录在 `server-process.json`；初次游玩日志仍予保留。

未添加 DPJAM 的 O2Hook、图形/音频代理 DLL 或判定补丁，主程序和 OJN/OJM 保持
提取后的内容。当前截图工具不能稳定捕获原客户端的 DirectDraw 画面，视觉/听感
以用户实际操作为准。界面可能仍包含失效的历史网页，本轮未修复网页服务；
本机服务端监听不代表客户端本身已被限制为只访问本机。

后续重点核对判定位置取整、同 KS 重触发、音量/声像及 LN 视效。使用同一曲目、难度、
速率和输入条件，对比 O2Lazer 非 MS 玩法。服务端的购买、奖励、惩罚和排名策略不能
当成原官方服务器的完整行为证据。差异与取舍见
[韩服客户端对照](korean-client-comparison.md)及[行为规范](o2jam-behaviour-spec.md)。
