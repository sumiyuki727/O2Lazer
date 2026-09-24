# 本地化模块与新增语言

当前本地化按三个职责组织，随宿主单 DLL 发布：

| 文件／目录 | 职责 |
|---|---|
| Host/Localisation/O2LazerStrings.cs | 面向 UI 的稳定文本入口及参数签名 |
| Host/Localisation/EmbeddedLocalisationCatalog.cs | 发现内嵌语言资源，按 CultureInfo 父链逐键回退 |
| Host/Localisation/ResourceLocalisableString.cs | 对接 framework 的 LocalisableString，处理动态选语与原生格式化 |
| Resources/Localisation | 英文基础资源及各语言翻译 |

资源目录只使用 .NET 的 ResourceManager，不依赖 O2Jam 玩法规则。
适配器复用 framework 的数字格式、嵌套本地化参数和字符串比较机制。
它们现在仍编译在宿主程序集；是否提取跨 ruleset 公共库留待实际复用时决定。

## 添加一种语言

1. 复制 O2LazerStrings.resx 为 O2LazerStrings.<语言代码>.resx，
   如 ja、ko、zh-Hant、pt-BR。采用 .NET CultureInfo 支持的标准名称。
2. 保留所有 data 的 name，只翻译 value。保留格式占位符编号及其语义；
   可以按语序重排，但不能删除或增加参数。
3. 不修改 O2LazerStrings.cs，也不修改 csproj；通配资源配置会自动编译并内嵌资源。
4. 执行下面的筛选测试，再重新构建 ruleset DLL。
5. 在 lazer 语言设置中选择对应语言，实测布局、截断和切换。
   本模块跟随宿主提供的 EffectiveCulture，不扩展 lazer 自身的语言选择列表。

```powershell
dotnet test osu.Game.Rulesets.O2Lazer.Tests -c Release `
  "-p:OsuBinaryDirectory=<新版 lazer 程序目录>" `
  --filter 'FullyQualifiedName~O2JamLocalisationTest'
```

本次未新增实际翻译；生产资源仍为英文及中文。日语区域测试资源只用于验证语言发现，
不打包到规则集 DLL，也不代表已经支持日语界面。

## 回退与一致性

查找顺序为具体区域、父文化链、英文。例如 pt-BR → pt → 英文。
区域资源缺少某个键时逐键回退，不要求整份资源存在才回退。
现有 zh 继续作为中文通用资源，未来可补 zh-Hant 等更具体翻译。
无宿主语言上下文的 ToString 保持英文，避免意外改变既有非 UI 使用行为。

运行时回退用于容错；正式支持的语言仍必须和英文保持完整键集合及占位符一致。
O2JamLocalisationTest 自动检查所有已打包语言，不需要增加语言专用测试分支。
新增文本时同时添加 O2LazerStrings 入口、英文键及所有支持语言的对应翻译。

动态切换适用于保留 LocalisableString 的控件。已提前调用 ToString 并保存的普通字符串，
例如部分持久化名称，不会自动刷新；这类行为应在对应功能重构时单独处理。

## 验证与限制

`O2JamLocalisationTest` 检查打包资源发现、英文/中文回退、区域优先、逐键缺失回退、动态语言切换、嵌套参数、数字格式、相等性及资源键/占位符一致性。常规验证由 `scripts/verify.ps1` 的过滤测试覆盖。新增语言仍需在实际客户端检查布局、截断、切换及非 UI 的持久化名称；自动测试不证明所有已有 `ToString()` 快照能随语言实时刷新，详见[问题 A13](architecture-audit.md#待处理问题)。
