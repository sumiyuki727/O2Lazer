# 本地化模块与新增语言

维护状态（2026-10-06）：现行本地化指引；随机七项固定英文是用户例外，仍统一走资源入口。
文档职责与最新状态入口见[索引](README.md)。

更新：2026-10-06。A13 已检查生产文本的所有使用位置及原生接收接口；本轮保留现有运行实现，补充原生绑定回归与固定字符串约定。

后续更新（2026-10-08）：新增 `ReplayReadLimitExceeded`、`ReplaySaveLimitExceeded` 和 `ReplaySaveFailed` 原生通知文本入口，中英文各 134 个资源键并保持一致；用于[回放读取拒绝及保存附件失败反馈](replay-resource-limits.md)。下方 A13 测试／未新增键的说明是 2026-10-06 历史范围，随机七项固定英文政策不变。

后续更新（2026-10-13）：R09 已删除 32 个无生产消费者的公开文本入口及中英文各 30 个资源键，当前各 **104 项**。保留入口的值、参数签名和随机七项固定英文均未改；上方 134 项为 2026-10-08 历史数量。

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

## 资源清理与兼容（R09）

清理前对 200 个实际生产编译输入进行语义引用核对，普通版及 O2JAM_SYNC_DIAGNOSTICS 版结论一致。
另外扫描仓库测试、TestSupport、脚本中的标识符和字面键；两个旧测试引用改为验证当前通知和完整收藏夹名称。
已安装的 BmsRuleset、AIEditorBridge DLL 的程序集、类型和成员引用没有指向这些入口；
只读 BMS 源码也没有 O2Lazer 依赖。没有因此改写其他 ruleset 或新增共存补丁。

删除范围为旧导入按钮/结果通知、旧手动编码说明、冗余流速/动作标签、旧计数格式及收藏夹前缀入口；
还移除两个未使用的原生 Notes/HoldNotes 转发。其完整公开成员清单见
[审查基线](refactor-review-2026-10-06.md#r09资源配置和预留开关的清理候选)，另含后来失去消费者的 SourceFolderCollectionPrefix。
现有界面继续使用原生 Mania 动作/统计、原生流速标签，以及当前 O2Lazer 通知和 SourceFolderCollectionName。

这是测试版本的公开 API 清理：本仓库和已核对 DLL 未发现生产消费者，
但不能保证未知外部源码、旧二进制或动态反射调用兼容；直接引用被删入口的外部项目需更新并重新构建。
资源键是 UI 实现细节，不是成绩、回放、Mod 或收藏夹的存储身份；不修改或清理已保存数据。

O2JamRulesetSetting.MetadataEncoding 仍保留原名称、序号和默认值，原生配置管理器继续读取旧值。
它不控制当前 OJN 自动识别，也不重新开放手动编码 UI；目录编码上下文及导入 EncodingFallback 仍在使用。
未来自动 KS 的 internal 入口继续保留，计分政策确定前不注册可见 Mod。Random 枚举/回放算法身份没有改动。

自动验证覆盖 97 项资源/原生绑定/Mod/谱面属性/收藏计划回归及独立进程的 8 项旧设置测试。
旧设置测试使用临时原生数据库，覆盖四种编码的名称/数字旧值、保存其他设置后重启、规则集与变体隔离。
资源、占位符、固定英文及正式包隔离均核对；未替换客户端 DLL，本轮不代表新的客户端视觉验收。

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

动态切换适用于保留 LocalisableString 的控件。UI 应把原始值交给原生 Text、Caption、HintText、TooltipText、SettingSource 或 Bindable，不在赋值前调用 ToString。SpriteText 的原生本地化绑定会响应宿主语言变化，无需增加自己的全局语言事件或刷新全部谱面。

## 文本所有者与固定字符串

2026-10-05 用户追加约定：随机算法的标题、名称和说明固定英文。标题 `random_algorithm` 使用 Algorithm；
`random_algorithm_description` 与
`random_algorithm_native/o2jam/panic/r_random/s_random` 仍通过 O2LazerStrings 供原生控件消费，
这七个入口直接读取 Catalog.GetEnglish，不随当前或未来新增语言的绑定变化；
所有语言资源也应保留相同英文文本；`panic` 的显示值是 S-Random，`s_random` 是隐藏旧值的
S-Random (legacy)。其余设置继续本地化，不能因此改变 enum/回放身份。

| 使用位置 | 当前方式与理由 |
|---|---|
| 设置标题、按钮、提示、导入路径占位、进度/结果通知 | 原始 LocalisableString 交给原生控件和通知；路径本身是用户数据。 |
| Mod 说明、SettingSource 标签/说明、SettingDescription 的设置名与值 | 保留 LocalisableString；MS/EZ 自定义描述仍由原生设置/提示控件消费，数字随 EffectiveCulture 格式化。 |
| 谱面属性长标签/说明、Lv 文本、等级筛选提示和分组标题 | 保留 LocalisableString，原生控件消费；等级/颜色和动画状态不因语言变化重新计算。 |
| Mod.Acronym，以及原生属性的 o2ma/SR/LV/OD/HP 缩写 | 原生接口为 string。MS 缩写用于 APIMod 与回放，OD/HP 还用于原生属性匹配；使用英文基础值，不能随 UI 语言改变标识。 |
| Mod.Name、ExtendedIconInformation、Ruleset.Description | 原生接口为 string，保留当前英文名称/品牌及原生样式的短数字文本。Name 不是 Mod 序列化身份；它保持英文是当前原生展示约定。 |
| OJN 难度名、文件夹收藏夹生成名称 | 写入时使用英文基础值和不变文化，避免切换语言改变存储名称。收藏夹所有权按持久化管理 ID 确认；名称/前缀不是删除或认领权限，见[归属契约](library-persistence-contract.md#文件夹收藏夹的管理归属)。 |
| 跨模式转换异常消息 | 原生异常接收普通 string，使用发生时的英文基础消息，便于一致诊断；不是可持续刷新控件。 |

`O2LazerStrings` 返回值的 `ToString()` 在无语言上下文时始终返回英文基础值，不依赖线程 CurrentCulture/CurrentUICulture。本轮核对到的生产 ToString 调用均属于上表固定字段，未发现将可本地化的自有 UI 标签提前保存成普通字符串的调用。

原生 string 接口仍有展示限制：MS Name 当前显示英文，资源中的中文名称不会由该接口自动选用；ExtendedIconInformation 不随 UI 语言改变小数分隔符。原生 ModPresetRow 还会用 string.Join 将 SettingDescription 压成预设摘要字符串，已有摘要不能动态翻译。这些是原生消费者的限制，不是资源模块缺少语言发现。本轮沿用原生行为；若以后要求这些位置实时翻译，应在 Presentation 分别定义显示目标、使用原生 LocalisableString 接收点，并记录必须适配的原生缺口，不能把当前语言注入 Mod 标识或存储逻辑。

需要自己订阅解析文本时，使用原生 LocalisationManager.GetLocalisedBindableString，并在控件释放时解绑。GetLocalisedString 的返回值只对当前语言有效；它适合一次性使用，不应缓存为长期 UI 文本。传入现成的 LocalisableString 时通常可直接交给原生控件。

## 验证与限制

`O2JamLocalisationTest` 检查资源发现、回退、嵌套参数、相等性及全部资源键/占位符一致性。`O2JamLocalisationBindingTest` 使用原生 FrameworkConfigManager/LocalisationManager，让同一批原始文本经历英文→中文→德文→英文→中文；覆盖设置、通知、Mod、谱面属性、等级筛选/分组、原生数字格式、文本替换和释放，并验证线程文化变化不改变 MS 缩写、短徽章、难度名和收藏夹名。这是原生绑定与生产文本入口的回归，不是加载全部界面的视觉验收。

常规验证由 `scripts/verify.ps1` 的过滤测试覆盖。新增语言仍需在客户端检查布局、截断与切换；A13 当轮未改资源键，本次 R09 清理范围见上方记录。A13 的范围与原生限制记录在[问题清单](architecture-audit.md#初始审查问题)。
