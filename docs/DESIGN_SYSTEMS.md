# 🎨 PyDeck desktop design systems

**English** · [简体中文](#简体中文) · [🏠 Home](../README.md)

## 🎨 Choose your interface

PyDeck offers **Windows Fluent** and **Material 3 Expressive** in **Settings → Appearance → Interface style**. They have separate navigation, headers, settings layouts and component styling, while sharing the same Python installation, build, package-management, cancellation, localization and search services.

Selecting another style saves it for the next launch and opens a **Restart now / Later** dialog. **Later** is the default: it keeps the current interface, tasks and unsaved edits in place. Restart remains disabled for the first **1.8 seconds** and while a task or operation is running. Restarting closes the current session, so save any edits first. The restart action uses Windows App Lifecycle. Theme and language changes apply immediately; a pending interface choice does not change the active session's backdrop or color policy.

The MSI's initial-style option seeds a new profile only. Existing application preferences take precedence. **Settings → Appearance → Component gallery** shows examples of the active presentation's controls and interaction states.

## 🧩 Shared services, separate presentations

Only the startup-selected presentation's style dictionary and visual tree are loaded. Separate Fluent and Material implementations own navigation, headers, setting rows and component styles; application workflows remain shared. Page helpers consume semantic roles for foreground/background pairs, typography, spacing, density, surfaces and interaction states.

Fluent uses native navigation and control templates. Material has its own button and switch templates, navigation drawer and grouped surfaces, using native control classes for focus, invocation and automation semantics. Primary, secondary, quiet and destructive actions have distinct roles. Badges present status; choices and navigation expose their selected state.

Material action and navigation buttons use a circular ripple from the pointer location, clipped to the control's current rounded outline. Space/Enter feedback starts at the center. Disabled controls, Windows contrast themes and disabled system animations suppress the moving ripple. See [interaction checks](archive/MD3E_INTERACTION_ACCEPTANCE.md) for the Kazumi reference, expansion benchmark and verification boundaries.

Search retains the native AutoSuggestBox text editor, query/clear helpers, IME and debounced filtering. Fluent retains SDK text fill/elevation/focus resources; Material supplies its rounded editor and paired tonal states. Material selector items use rounded tonal selection and a trailing checkmark while native ComboBox owns selection, scrolling, popup placement and accessibility. Expanders share component configuration, including the custom-color and build-storage sections. Refresh and operation progress both use Material primary/secondary-container roles while retaining native progress semantics and animations.

Both presentations retain batched logs, in-place build-option updates and scroll gutters. Switching the saved style does not rebuild the current page or interrupt an active operation.

## 🌈 Surfaces and dynamic colors

Fluent uses **Mica** by default, with optional window **Acrylic** and transparency preferences. Material uses opaque surfaces and follows the desktop wallpaper's colors by default. Custom base colors and Balanced, Expressive or Two colors palettes are available under **Settings → Dynamic colors** while Material is active. The [Monet source notes](MONET.md) explain the pinned AOSP seed selection, official MCU HCT/dynamic-role engine and PyDeck's two-color extension.

Windows contrast themes override both palettes. Automatic wallpaper updates preserve open settings, build and environment editors and focused text inputs; a prepared palette can wait until the next page change.

## ♿ Desktop behavior and verification scope

Both presentations provide English, Simplified Chinese, Traditional Chinese (Taiwan) and Japanese, with light/dark themes and layouts for compact and regular windows. Material is a Windows desktop adaptation: it retains keyboard access, native dialogs, text scaling and Windows accessibility semantics. Its compact sizing is not a direct copy of Android density units.

The isolated WinUI fixtures cover rendered controls and pages, keyboard focus and invocation, selected/disabled states, long labels and deferred style changes. They use fixture data rather than modifying Python installations. Automated results are separate from human visual acceptance and accessibility or multi-monitor/DPI certification. Historical records are in the [archive](archive/README.md); repeatable procedures are in [Development](DEVELOPMENT.md).

## 📚 Design references

- [Design System Gallery](https://designsystem.gallery/) — complete systems and component families
- [Component Gallery](https://component.gallery/) — navigation, badges, buttons, disclosure and selection
- [Fluent tokens](https://fluent2.microsoft.design/design-tokens) — global values and semantic aliases, including interaction states
- [Fluent typography](https://fluent2.microsoft.design/typography) — Windows type ramp, including line height
- [Material expressive buttons](https://github.com/material-components/material-components-android/blob/master/docs/components/CommonButton.md) — emphasis, size, shape and selection
- [Windows materials](https://learn.microsoft.com/en-us/windows/apps/design/signature-experiences/materials) — backdrop, content and transient surfaces

The galleries index examples; they are not normative specifications. Official Fluent and Material guidance informs the respective presentations, with the Windows adaptations described above.

## 简体中文

### 🎨 选择界面风格

在“设置 → 外观 → 界面风格”选择 **Windows Fluent** 或 **Material 3 Expressive**。两种风格分别维护导航、标题、设置布局与控件样式，共用 Python 安装、构建、包管理、取消、语言和搜索逻辑

选择另一种风格后先保存偏好，再显示“立即重启 / 稍后重启”弹窗。默认选择稍后，保留当前界面、任务与未保存编辑；立即重启按钮前 **1.8 秒**不可用，有任务运行时继续禁用。真正重启会关闭当前会话，请先保存编辑。语言与明暗主题立即生效，待重启的风格选择不会提前改变当前窗口材质或配色规则

MSI 中的初始风格仅用于新配置，已有应用偏好优先。在“设置 → 界面设置 → 组件展示”可以查看当前风格的真实控件与交互状态

### 🧩 界面结构与控件行为

启动时只加载当前风格的资源字典和视觉树。页面通过语义角色使用文字与背景配对、字号、行高、字重、间距和交互状态，不重复实现业务流程

Fluent 使用原生导航与控件模板；Material 独立实现按钮、开关、导航抽屉和分组表面，同时保留原生焦点、调用与自动化语义。主操作、次操作、轻量操作、危险操作有各自样式；状态标签不可点击，可选控件和导航提供明确的选中状态

Material 操作按钮与导航按钮使用从指针落点向外扩散的圆形涟漪，按控件当前圆角裁切。Space/Enter 从中心开始；禁用控件、Windows 对比度主题或关闭系统动画时不播放移动涟漪。Kazumi 参考、展开基准和验证范围见[交互检查](archive/MD3E_INTERACTION_ACCEPTANCE.md)

搜索框保留原生输入法、清除、查询与防抖行为。Material 下拉候选使用圆角色调容器和勾选标记，由原生 ComboBox 管理选择、滚动、弹出位置与辅助功能。折叠面板、刷新和任务进度统一使用主题角色，保留日志合并、构建选项局部更新及滚动条留白

### 🌈 材质与动态配色

Fluent 默认使用 **Mica**，支持全窗 **Acrylic** 与透明效果设置；Material 使用不透明表面，默认从桌面壁纸取色。Material 生效后，“设置 → 动态配色”提供自定义基色与均衡、表现力、双色三种配色方案，算法与来源见 [Monet 说明](MONET.md#简体中文)

Windows 对比度主题优先于普通配色。自动壁纸取色完成后，不会替换正在编辑的设置、构建、环境页面或获得焦点的文本输入，新配色可延后到下一次切换页面应用

### ♿ 桌面适配与验证范围

两种风格均提供英语、简体中文、繁体中文（台湾）和日语，支持明暗主题及紧凑、常规窗口。Material 是 Windows 桌面适配，保留键盘操作、原生对话框、文字缩放和辅助功能语义，控件尺寸不直接照搬 Android 密度单位

隔离的 WinUI 检查覆盖控件、页面、焦点、选中与禁用状态、长文字和重启生效行为，不修改用户的 Python 安装。自动检查不等于人工视觉验收、完整辅助功能认证或多显示器与 DPI 验收，历史记录见[归档](archive/README.md)，检查方法见[开发指南](DEVELOPMENT.zh-CN.md)


### Settings workspace / 独立设置工作区

Settings uses a dedicated category/detail workspace with separate Material and Fluent navigation. Startup page, close behavior, system font, OLED black canvas and native title bar preferences are shared; About has its own destination. Material uses continuous tonal rows, compact accent section headings and borderless selectors with filled triangular disclosure icons. Implementation and verification: [settings workspace](archive/SETTINGS_WORKSPACE_ACCEPTANCE.md).

设置独立为分类与详情工作区，宽屏左右分栏，窄屏逐页进入并返回。两种风格都提供启动页、关闭行为、系统字体、OLED 纯黑背景、系统标题栏以及独立关于页面。普通开关不再重建整页，并通过有序后台队列保存，退出和重启等待保存完成。
