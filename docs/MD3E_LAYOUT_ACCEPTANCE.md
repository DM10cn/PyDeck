# MD3E 工作区调整：验收记录与复验方法

日期：2026-10-03。范围为用户批准的 `PyDeck-MD3E-Agent-修改建议.md` 全体界面调整，以及两项补充：Fluent / MD3E 的安装与已安装操作等宽，宽窗口的列表工作区铺宽且滚动条靠工作区右边。

本文件区分新增检查能力与已执行证据。新增测试代码本身不代表通过运行验收；未取得对应结果文件的项目保持“未验证”。离屏 WinUI 检查不能替代实际键盘、输入法、Narrator、高对比或多显示器测试。

**当前交付状态：代码与开发构建已完成，自动检查分轮完成，人工实机验收尚未覆盖。** 核心 118 项通过；192 场景布局矩阵通过，与归档旧版同条件比较 0 个密度回退。`DesignOnly` 的前置页面、组件、导航和构建页检查通过后，在组收起焦点检查失败；修复后最终 `PerformanceOnly` 整体通过，覆盖焦点回归及其余性能检查。不能将这些分轮结果写成一次完整 `DesignOnly` 全绿。

## 实现与文件映射

Fluent 与 Material 保留各自的 presentation 和原生交互，共享现有 runtime、安装、日志及设置业务。修改集中于呈现、有限视口、控件状态和相应检查，没有更换技术栈、最低 Windows 版本、SDK、运行时依赖或安装器发布模型。

| 界面职责 | 主要源文件 | 本轮实现与保留边界 |
|---|---|---|
| 全宽工作区与滚动 | [MainWindow.xaml](../src/PimGui.App/MainWindow.xaml)、[MainWindow.xaml.cs](../src/PimGui.App/MainWindow.xaml.cs)、[RuntimePages.cs](../src/PimGui.App/RuntimePages.cs) | 移除 `ContentColumn` 整体限宽；`PageHost` 和列表 viewport 铺满内容面，保留 Grid 星号行的有限高度。设置/构建等表单只在 ScrollViewer 内部限宽，滚动条仍位于工作区右侧 |
| 独立侧栏与页头 | [MaterialPresentation.cs](../src/PimGui.App/MaterialPresentation.cs)、[FluentPresentation.cs](../src/PimGui.App/FluentPresentation.cs)、[DesktopPresentation.cs](../src/PimGui.App/DesktopPresentation.cs) | Material 展开/紧凑侧栏保留全部目的地、设置与可访问名称；当前导航使用 tonal 状态。窄侧栏显式清除原生 `MinWidth` 约束，并检查控件位于分配槽内 |
| 语义 token 与原生状态 | [DesignTokens.cs](../src/PimGui.App/DesignTokens.cs)、[DesignComponents.cs](../src/PimGui.App/DesignComponents.cs)、[MaterialTemplates.cs](../src/PimGui.App/MaterialTemplates.cs)、[Appearance.cs](../src/PimGui.App/Appearance.cs) | 统一字号/行高、角色表面与密度；保留 hover、pressed、disabled、focus；运行期间动画偏好变化更新现有模板过渡，不重建当前页面 |
| 我的 Python 与安装操作 | [RuntimePages.cs](../src/PimGui.App/RuntimePages.cs)、[Visuals.cs](../src/PimGui.App/Visuals.cs)、[RuntimeListView.cs](../src/PimGui.App/RuntimeListView.cs) | 默认 badge 靠近标题，默认容器采用轻量 tonal 区别；保留版本、发行来源、架构、路径与复制入口。安装/已安装共享真实文本测量占位，隐藏测量内容不进入普通 UIA 视图；原有命令及禁用条件保留 |
| 连续版本分组与回收 | [RuntimeListView.cs](../src/PimGui.App/RuntimeListView.cs)、[DesignExpanderStyles.cs](../src/PimGui.App/DesignExpanderStyles.cs) | 将组头与 release 展平为同一原生 `ListView` 数据序列，由 `ItemsStackPanel` 回收；native Expander 负责展开语义。按稳定 key 更新条目、同步搜索展开状态、保留滚动锚点；回收时清除旧内容和标记 |
| 搜索与筛选 | [RuntimePages.cs](../src/PimGui.App/RuntimePages.cs) | 搜索保持弹性宽度，筛选按内容需求和文字缩放换行；结果摘要区分全部与筛选数量，无匹配可清除筛选。继续使用原生输入、已有查询/排序/筛选语义 |
| Activity | [ActivityPage.cs](../src/PimGui.App/ActivityPage.cs) | 保留真实日志模型、级别筛选、错误详情、复制/清空；区分空会话与筛选无记录。原本位于末尾才跟随追加，阅读历史时使用原生可见条目锚定；工具栏按真实内容宽度换行 |
| 本地化与检查 | [Strings](../src/PimGui.Core/Strings)、[RuntimeWorkspaceChecks.cs](../src/PimGui.App/RuntimeWorkspaceChecks.cs)、[VirtualizedListChecks.cs](../src/PimGui.App/VirtualizedListChecks.cs)、[MaterialInteractionChecks.cs](../src/PimGui.App/MaterialInteractionChecks.cs) | 补充四语言文案、全宽/等宽矩阵、原生状态和合成长列表检查；不把程序化焦点/UIA 调用当作完整人工键盘或读屏验收 |

以下均为项目使用的 DIP / XAML 有效像素，不是对截图反推的尺寸，也不宣称为 Google 官方 token：

| Material 角色 | 当前值 | 实现位置 |
|---|---|---|
| 页面标题 | 字号 30 / 行高 38，Semibold | `DesignTokens.Typography` 与 `MaterialPresentation.Header` |
| Runtime 卡片 | 圆角 16；常规内边距 12 | `DesignTokens` 与 `Visuals.RuntimeCardBox` |
| 主内容承载面 | 圆角 24；常态内容内边距水平 24 / 垂直 20，紧凑 16 | `DesignTokens` 与 `MaterialPresentation.ApplyShell` |
| 连续分组 | 外角 16 / 内角 4，相邻行间隔 2 | `Visuals.CatalogSegmentCorner`、`DesignExpanderStyles`、`RuntimeListView` |
| Material 侧栏 | 展开 220 / 紧凑 72；导航项 `MinHeight` 44 | `MaterialPresentation` |
| 操作密度 | 常规 `MinHeight` 40 / 紧凑 32；工具组间隔 12 | `DesignTokens` 与 `DesignComponents` |

高度采用最小值和内容测量，不锁死放大文字。菜单、输入焦点、错误及高对比所需的边界保留；普通卡片减少常驻描边。安装/卸载/默认切换、权限、排序与数据来源继续沿用原业务，不新增任务后端、伪造取消/重试或进度。

## 已执行证据索引

| 证据 | 路径 | 结论与边界 |
|---|---|---|
| 最终开发构建 | [PyDeck.Launcher.exe](../artifacts/dev-win-x64-20261003-130033/PyDeck.Launcher.exe)、[发布日志](../artifacts/md3e-final-publish.log) | 编译 0 警告、0 错误；原生 launcher 检查通过。开发构建，未制作安装器或发布 release |
| 核心检查 | [md3e-final-build.log](../artifacts/md3e-final-build.log) | 118 passed，0 failed |
| 旧版基线 | [smoke-20261003-122730/result.json](../artifacts/smoke-20261003-122730/result.json)、[来源说明](../artifacts/md3e-baseline-build/baseline-provenance.json) | 归档提交 `0a4311a945796b9ec279ac0d57984ec3c52ebba5`，仅注入独立测量夹具；192 场景通过 |
| 布局与对比 | [smoke-20261003-124845/workspace-layout.json](../artifacts/smoke-20261003-124845/workspace-layout.json)、[密度比较](../artifacts/md3e-density-comparison.json) | `passed: true`；192 场景、252 个普通 Material 文字对比样本；同 Root 尺寸和缩放下 0 个密度回退 |
| 设计分项 | [smoke-20261003-124845/result.json](../artifacts/smoke-20261003-124845/result.json) | 整体 `passed: false`，保留真实失败记录。此前搜索、下拉 UIA、组件、192 个六页面组合、192 工作区组合、紧凑导航边界、动画、构建页均完成；失败点为收起组内焦点归位 |
| 最终专项复验 | [smoke-20261003-130132/result.json](../artifacts/smoke-20261003-130132/result.json) | 最终构建 `passed: true`。两主题千条候选/千条 Activity、组内焦点归位、搜索展开/清空、刷新锚点、四语言构建组件及日志/进度批处理全部通过 |
| 交互采样 | [结果](../artifacts/smoke-20261003-124823/result.json)、[视频](../artifacts/md3e-interaction-demo.mp4)、[采样范围](../artifacts/md3e-interaction-demo.md) | 64 个实际 Root 帧，6.64 秒、1458 × 1052；覆盖导航、展开/收起、键盘焦点、关闭动画。不是桌面或真人操作录屏 |
| 交付清单 | [md3e-delivery.json](../artifacts/md3e-delivery.json) | 构建路径、哈希、分轮证据和未覆盖范围 |

最终 `PyDeck.dll` SHA256：`A996771B36E1FC824AED589B26EE9326C33BA73942F2EAB353AAC09DA1C7711B`。

布局截图及录制来自最后焦点时序修复之前；其后生产代码只调整收起操作完成后的焦点归位，未改变布局。最终构建的该回归由专项结果直接验证。源码与最终构建之间没有后续生产代码修改。

基线和新版布局目录各保存 96 张中英文截图。按同名 `workspace-{design}-{theme}-{language}-{width}-{page}.png` 配对查看，其他语言有数值检查。两轮均等待 600 ms，并在截图后读取完整可见记录数。

下表为中文、深色、请求 1920 × 850 DIP，实际 Root 1905.6 × 841.6 DIP，125% 显示缩放、100% 文字缩放的合成数据比较：

| 设计 / 页面 | 工作区宽度 DIP：旧 → 新 | 完整可见记录：旧 → 新 |
|---|---|---|
| Fluent / 我的 Python | 1040 → 1632 | 5 → 5 |
| Fluent / 安装 Python | 1040 → 1632 | 3 → 3 |
| Fluent / Activity | 1120 → 1632 | 14 → 14 |
| Material / 我的 Python | 1020 → 1625.6 | 4 → 5 |
| Material / 安装 Python | 1100 → 1625.6 | 2 → 2 |
| Material / Activity | 1080 → 1625.6 | 13 → 13 |

长列表最终专项每种主题均使用 1,000 条候选，采样滚动位置最多实例化 8 个 runtime 行。查询响应中位数（含 debounce）为 Fluent 455.9 ms、Material 554.7 ms；没有同条件旧版性能基线，不报告速度提升百分比或稳定内存占用。

弹出层的独立 `architecture-popup.png` 经视觉复核为空白，**不作为下拉外观证据**。原生下拉展开/收起由 UI Automation 状态验证；Root 视频不能证明其外观、桌面合成材料或真人操作。
## 运行入口

先使用项目现有构建命令创建发布目录，再运行以下入口。每次运行使用单独的 `artifacts/smoke-时间戳/preferences`，不读写用户的正式设置；`WorkspaceOnly` 不进行 PIM 发现、网络请求、安装、卸载或默认版本变更。

```powershell
./scripts/Smoke-Test.ps1 -WorkspaceOnly -TimeoutSeconds 900
./scripts/Smoke-Test.ps1 -DesignOnly -TimeoutSeconds 1200
./scripts/Smoke-Test.ps1 -PerformanceOnly -TimeoutSeconds 900
./scripts/Smoke-Test.ps1 -RecordingOnly -TimeoutSeconds 180
```

- `WorkspaceOnly`：本轮相关的布局、等宽、颜色、导航、动画、搜索筛选、长列表和 Activity 滚动检查。
- `DesignOnly`：包含上述工作区检查及现有组件、原生下拉、设置、主题、构建页等检查。
- `PerformanceOnly`：现有搜索/进度/日志批处理检查，以及新的 1,000 条候选与 1,000 条 Activity 记录检查。
- 不应同时运行多个 UI probe，也不应在 probe 运行中替换其发布目录。

`result.json` 中的 `passed` 才是本次检查结果；发生错误后写出的部分 `checks` 只代表此前完成的子项。`workspace-layout.json` 另记录布局子项的成功状态及原始数据。失败也尽可能保存已采集测量，不能据此宣布整个矩阵通过。

## 可复查输出

`workspace-layout.json` 使用标明为合成的数据：12 条已安装 runtime、24 条安装候选、60 条单行 Activity 记录。矩阵包含 Fluent / Material、Light / Dark、四种现有语言、930 / 1180 / 1680 / 1920 DIP 的请求窗口宽度，以及三个重点页面，共 192 个场景。请求窗口高度为 850 DIP；同时记录实际 Root 大小，避免把外框尺寸误称为客户区尺寸。

每个场景记录实际工作区宽度、列表 viewport 高度、完整可见记录数、已实例化记录数、DPI 比例及系统文字缩放。中文简体和英文场景保存 `workspace-{design}-{theme}-{language}-{width}-{page}.png`。其他语言执行几何及文字裁切检查，不把缺少截图的场景伪装为已截图。

完整记录数只计算完整落在当前 viewport 的条目，不把虚拟化缓存中的离屏控件算作可见记录。安装目录计数不含“推荐”副本与分组标题。Activity 采用单行合成消息，不能直接与任意真实多行日志的行数比较。

最终矩阵会在导航后等待 600 ms 并重新布局；截图返回后再次布局、重新取 realized rows 才计算完整可见数，避开原生 Expander 的初始布局过渡。基线必须使用相同等待和取样时序。合成已安装数据通过约束检查保证仅一条默认 runtime，所有条目均为 managed；不能用全部默认或误标为外部版本的截图评判产品默认态。

长列表检查使用 1,000 条合成候选与 1,000 条合成 Activity 消息，确认 `ItemsStackPanel`、有限视口和已实例化容器数远少于源条目数；采样多个滚动位置，检查回收后的已安装状态不会串到其他版本。记录初次显示耗时、包含既有 debounce 的五次查询响应中位数、未经强制 GC 的托管内存快照差值。该内存值可能含尚未回收对象，不能称为稳定内存占用或泄漏证据；没有旧版本同条件数据时不能宣称性能提升百分比。

实际 Material 文字对比检查从已渲染 TextBlock 的前景以及视觉祖先的 SolidColorBrush 背景计算 sRGB 对比度，记录文字、前景、背景、比值。仅覆盖关闭透明效果的普通启用文字，最低 4.5:1；不以 token 相等代替对比测量。搜索检查另验证实际焦点边框相对于内外相邻面至少 3:1。禁用控件、渐变、合成器透明材料、系统高对比不计入这份颜色结论。

## A01–A15 证据状态

“自动部分通过”仅指下列实测内容，右列未覆盖条件保持未验证。

| ID | 状态 | 已验证 | 未验证范围 |
|---|---|---|---|
| A01 信息层级 | 自动部分通过，代表截图已复核 | 默认 badge 邻近标题、主次操作、两主题明暗及宽窄布局 | 真人识别效率与视觉偏好 |
| A02 密度 | 当前条件通过 | 同数据、Root 尺寸、缩放下 192 场景无回退；配对截图和计数 | 其他 DPI、文字缩放及真实多行日志 |
| A03 导航 | 自动部分通过 | 六目的地的 UIA 选择、名称、930/1180 DIP 导航项及图标边界 | 真实 hover、Tab/Shift+Tab、读屏 |
| A04 搜索筛选 | 自动部分通过 | 原生编辑/清空、最终查询、筛选持久化、保留控件与焦点、搜索展开恢复 | 中文/日文 IME composition |
| A05 Runtime 信息 | 自动部分通过 | 版本身份、默认态、四语言两主题安装状态等宽/等高/列对齐、回收状态正确 | 真实终端启动、长路径键盘复制、业务操作 |
| A06 安装分组 | 自动部分通过 | 原生展开语义、连续布局、无嵌套子列表、收起后焦点归位、搜索同步 | 真实安装派发与业务结果 |
| A07 Activity | 自动部分通过 | 千条回收、历史阅读位置、末尾跟随、详情/清空、日志洪峰 | 真实任务各来源多行日志操作 |
| A08 状态完整性 | 自动部分通过 | 空态/无匹配、禁用语义、局部进度及取消检查 | 真实网络失败、安装失败和恢复流程 |
| A09 键盘读屏 | 自动部分通过 | 原生 UIA 状态、程序化键盘焦点、收起后回到原组头 | 完整真人键盘路径、菜单焦点与 Narrator |
| A10 主题对比 | 自动部分通过 | 明暗普通文字 4.5:1、搜索焦点 3:1、主题/颜色切换 | 系统高对比切换、透明合成及全部图标/禁用状态 |
| A11 缩放本地化 | 固定缩放通过，多缩放未验证 | 四语言、四窗口宽度的几何/裁切检查 | 100/150/200% DPI、独立文字缩放、跨屏 |
| A12 Motion | 自动部分通过 | 原位开关过渡时长、控件/页面/焦点保留、Root 帧演示 | 真实 Windows 偏好事件、完整桌面动效观感 |
| A13 Windows 习惯 | 几何部分通过，实机未验证 | 有限全宽 viewport、右侧滚动条、原生窗口控件保留 | 真正最大化、标题栏、触控板、右键及真人快捷键 |
| A14 长列表 | 合成专项通过 | 千条候选/Activity 回收、刷新锚点、查询采样、状态不串行 | 同机器旧版性能对比、真实生产负载、帧时间 |
| A15 范围构建 | 构建通过，自动检查分轮完成 | 核心 118 项、最终构建、受影响分项与最终性能专项；证据如上 | 不宣称单次最终 DesignOnly 全绿或安装器验收 |
## 剩余验证与结论边界

本次检查按证据索引分轮执行，保留失败结果，并在后续针对性检查中验证修复。窄导航截图来自已修复版本；最终焦点修复由最后专项验证。复验时可对最终发布目录顺序运行上述入口，不同时启动多个 UI probe。

仍未完成的实机覆盖包括真实 Tab/Shift+Tab 与方向键导航、Narrator、系统输入法组词、运行期间高对比/文字缩放/动画设置变化、多显示器移动、触控板、实际安装/卸载/默认切换和完整录屏。现有程序化焦点及 UI Automation 状态检查分别记录其自动化结论，不替代这些操作。

性能检查报告合成数据下的回收、锚点与响应采样，不承诺实际生产负载的帧率，也没有与旧版同负载的性能百分比结论。更早的不稳定取样或错误默认状态夹具仅供诊断历史使用，最终密度结论只引用上述稳定基线及与其匹配的当前矩阵。
