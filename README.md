# O2Lazer

An osu!lazer ruleset for playing native O2Jam libraries directly from `.ojn` and `.ojm` files.

[简体中文](./README.zh-CN.md)

## Features

- Reads classic and newly encrypted OJN files with EX, NX, and HX difficulties.
- Decodes M30, OMC, and OJM keysounds and background music in memory.
- Supports seven-key notes, long notes, BPM changes, keysounds, and BGM events.
- Displays native O2Jam difficulty names and levels.
- Imports embedded OJN cover art as the beatmap background and continues past unreadable charts.
- Keeps score displays separate for EX, NX, and HX difficulties.
- Decodes Korean metadata as CP949 and Chinese O2Jam 2.9 metadata as GBK/CP936, with UTF-8 fallback.
- Uses native O2Jam COOL/GOOD/BAD/MISS windows, raw score, and independently judged long-note endpoints.
- Provides dedicated O2Jam settings, file import, current-folder import, and recursive library import.
- Keeps gameplay HUD and mods aligned with the suitable osu!mania basics, including Random but excluding Daycore and Nightcore.

The default key bindings are `S D F Space J K L`. Use Up/Down during play to adjust scroll speed.

## Install

Build or obtain `osu.Game.Rulesets.O2Lazer.dll`, place it in osu!lazer's `rulesets` directory, and restart osu!lazer.

## Importing a library

Keep each `.ojn` beside its corresponding `.ojm`, `.omc`, or `.m30` file. Open **Settings -> O2Lazer**, then choose a file, import the current folder, or recursively import a library folder. The audio archive remains external, so keep the original library available after importing.

## Build

The ruleset targets .NET 8 and the osu!lazer `2026.804.2` source tree in the sibling `../osu` directory.

```powershell
dotnet build osu.Game.Rulesets.O2Lazer.slnx
```

To build against an already-built `osu.Game.dll` without writing to the osu checkout:

```powershell
dotnet build osu.Game.Rulesets.O2Lazer.slnx -p:OsuGameProjectPath=D:\osu\osu.Game\bin\Debug\net8.0\osu.Game.dll
```

## Credits and license

This project is derived from [QingQiz/BmsRuleset](https://github.com/QingQiz/BmsRuleset) and is licensed under AGPL-3.0. O2Jam decoding work references the MIT-licensed [O2MusicBox](https://github.com/SirusDoma/O2MusicBox), [CXO2](https://github.com/SirusDoma/CXO2), and public Open2Jam format documentation. See [THIRD-PARTY-NOTICES.md](./THIRD-PARTY-NOTICES.md).
