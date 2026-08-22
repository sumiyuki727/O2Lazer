# O2Lazer

一个用于直接在 osu!lazer 中游玩原生 O2Jam 谱面的规则集，支持直接读取 `.ojn` 与 `.ojm` 文件。

[English](./README.md)

## 特性

- 支持经典与新版加密 OJN 文件，包含 EX、NX、HX 三种难度。
- 内存中解码 M30、OMC、OJM 的键音与背景音乐。
- 支持 7K 音符、长条、BPM 变化、键音与 BGM 事件。
- 显示原生 O2Jam 难度名称与等级。
- 将 OJN 内嵌封面作为谱面背景，并可在遇到无法读取的谱面时继续导入。
- EX、NX、HX 难度分别保存独立成绩显示。
- 韩文元数据按 CP949 解码，中文 O2Jam 2.9 元数据按 GBK/CP936 解码，并保留 UTF-8 回退。
- 使用原生 O2Jam 的 COOL/GOOD/BAD/MISS 判定窗口、原生计分与独立判定的长条首尾判定。
- 提供专用 O2Jam 设置、单文件导入、当前文件夹导入与递归曲库导入。
- 默认 HUD 与可用模组保持简洁，包含 Random，不包含 Daycore 与 Nightcore。

默认键位为 `S D F Space J K L`。游戏内使用 Up/Down 调整滚动速度。

## 安装

构建或获取 `osu.Game.Rulesets.O2Lazer.dll`，放入 osu!lazer 的 `rulesets` 目录并重启 osu!lazer。

## 导入曲库

将每个 `.ojn` 与对应的 `.ojm`、`.omc` 或 `.m30` 放在同一目录。打开 **设置 -> O2Lazer**，可选择文件、导入当前文件夹或递归导入曲库文件夹。音频档案保持外部引用，因此导入后请保留原始曲库文件。

## 构建

规则集面向 .NET 8，并使用同级 `../osu` 目录中的 osu!lazer `2026.804.2` 源码树。

```powershell
dotnet build osu.Game.Rulesets.O2Lazer.slnx
```

如果希望直接引用已构建的 `osu.Game.dll`，避免写入 osu 检出目录：

```powershell
dotnet build osu.Game.Rulesets.O2Lazer.slnx -p:OsuGameProjectPath=D:\osu\osu.Game\bin\Debug\net8.0\osu.Game.dll
```

## 致谢与许可证

本项目源自 [QingQiz/BmsRuleset](https://github.com/QingQiz/BmsRuleset)，以 AGPL-3.0 许可发布。O2Jam 解码工作参考了 MIT 许可的 [O2MusicBox](https://github.com/SirusDoma/O2MusicBox)、[CXO2](https://github.com/SirusDoma/CXO2) 与公开的 Open2Jam 格式文档。详见 [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md)。
