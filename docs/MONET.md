# 🌈 Material dynamic colors in PyDeck

**English** · [简体中文](#简体中文) · [🏠 Home](../README.md)

PyDeck's Material presentation uses the official Material Color Utilities C++ engine for Celebi quantization, CAM16/HCT, tonal palettes and dynamic color roles. The wallpaper seed scorer is a C++ adaptation of AOSP `ColorScheme.getSeedColors(filter=true)`. It is not the generic MCU `Score` helper, an average RGB color, or an HSL tinting approximation. Fluent keeps its existing Windows palette and material policy.

## 🎨 Choose a color palette

With **Material 3 Expressive** active, open **Settings → Dynamic colors**. Choose **Desktop wallpaper** or **Custom base color**, then select **Balanced**, **Expressive** or **Two colors**. Expand **Custom base color** to edit a manual color and press **Apply custom color**; Two colors provides a second picker and saves both with **Apply custom colors**. **Refresh wallpaper colors** requests a new local extraction. Fluent has its own Windows palette and does not expose these Material controls.

## 📚 Pinned sources

- [Material Color Utilities](https://github.com/material-foundation/material-color-utilities/tree/5b3618b16fdc3825e21d5679bafd144662088ea1), commit `5b3618b16fdc3825e21d5679bafd144662088ea1`. The vendored file manifest records source and adapted file hashes. The C++ dynamic-role implementation is the **2021 color specification**, at standard contrast. This does not claim parity with the separate 2025 color specification or every current Android vendor theme.
- [AOSP ColorScheme](https://android.googlesource.com/platform/frameworks/libs/systemui/+/9aacbcb77aa9353e75bc7c4ebc51d20b8b241b62/monet/src/com/android/systemui/monet/ColorScheme.java), Android 16 `frameworks/libs/systemui` source pinned at `9aacbcb77aa9353e75bc7c4ebc51d20b8b241b62`. Its chroma filtering, nearby-hue population scoring, chroma weighting and fallback seed are retained. A neutral image can intentionally select the AOSP fallback blue `#1B6EF3`.
- [AOSP WallpaperColors](https://android.googlesource.com/platform/frameworks/base/+/99b01a65cc4c104933788b3143285ab6bae65827/core/java/android/app/WallpaperColors.java), `frameworks/base` commit `99b01a65cc4c104933788b3143285ab6bae65827`, is the reference for bounded wallpaper sampling and Celebi extraction.

The source headers and Apache 2.0 license are retained under `src/PyDeck.Colors`. MCU source files are vendored so a normal build does not fetch algorithm code. Windows portability changes, where present, are identified by the source manifest. Third-party code remains under its original license; PyDeck's MIT license does not relicense it.

## 🪟 Windows adapter and user behavior

The default source is the wallpaper on the display containing most of the PyDeck window. `IDesktopWallpaper` supplies the monitor-specific file, with a desktop SPI fallback when necessary. The Windows image decoder applies EXIF orientation and converts to sRGB; bounded image sampling feeds the native quantizer. Nonopaque samples are ignored. Android's wallpaper cropping, dimming, low-memory-device alternative quantizer and lock-screen policy are not emulated.

Sampling follows AOSP's area-based reduction: at most **12,544 pixels**, preserving aspect ratio with nearest-neighbor sampling and floored dimensions. The Windows adapter additionally caps extreme aspect ratios after enforcing a one-pixel minimum. Source files are limited to 64 MiB, 64 × 1024 × 1024 pixels and 32,768 pixels per dimension; remote paths and reparse points are rejected.

Extraction runs off the UI thread. A metadata fingerprint prevents repeated decoding of an unchanged file; activation and a 30-second timer check for changes while Material wallpaper colors are active. Local files are size-limited. The cache stores only the ordered opaque seed pair and a metadata hash, never image bytes or the wallpaper path. No image is copied or uploaded. If a wallpaper cannot be read, the last extracted pair is retained, or the custom base colors are used when no cache exists. Older single-color caches remain usable at startup while a fresh pair is prepared.

Settings offer a custom color picker and two official scheme variants: **Balanced (TonalSpot)** and **Expressive**. “Material 3 Expressive” names the UI presentation; choosing the Expressive color variant is a separate preference. All 49 native semantic roles are available, including primary, secondary, tertiary, error, foreground pairs and surface/container levels. Light and dark schemes are generated separately. Windows contrast themes override ordinary theme colors.

## 🎨 Two colors: PyDeck's DualSource extension

**Two colors** is an additional, opt-in PyDeck composition of the existing official MCU tonal palettes and `DynamicScheme` solver. The first source supplies Tonal Spot primary, neutral, neutral-variant and error palettes; the second supplies Tonal Spot secondary and tertiary palettes. All palettes enter one `DynamicScheme` before the official dynamic-role contrast and tone-delta rules resolve the 49 output roles. It is neither RGB averaging nor a splice of final light/dark colors. Selected navigation and supporting accents visibly consume the second source; primary actions and surfaces follow the first. Identical sources produce exactly the original Tonal Spot roles.

This extension is **not** Google's CMF variant or the 2026 color specification. [Official MCU SchemeCmf](https://github.com/material-foundation/material-color-utilities/blob/main/java/scheme/SchemeCmf.java) does accept two sources, but explicitly requires SPEC_2026 and has different palette, error-color and tone rules. PyDeck retains the pinned 2021 solver and names its own composition `DualSource`; the two existing official variants are unchanged.

Wallpaper mode uses the first two ordered candidates from the same pinned AOSP scorer. The legacy single-seed API still returns candidate zero. If the scorer produces only one candidate, both sources use it; neutral/empty images retain AOSP's blue fallback. Manual mode shows two pickers and saves their opaque values together when **Apply custom colors** is pressed. The optional second preference defaults to the first for older settings, and switching modes preserves the saved manual colors.

The additive `PyDeckColorsSeeds` and `PyDeckColorsDualScheme` exports preserve the original seed/scheme ABI. Managed scheme-cache keys include both sources. Wallpaper pairs are cached and prepared atomically, including second-source-only changes, and follow the same editor-preserving deferred application rule as single colors.

An automatic wallpaper result does not replace an open settings/build/environment editor or focused text input. Its prepared palette applies on the next page change. Manual color application is explicit. Switching between Fluent and Material still takes effect on the next launch; the active presentation owns its material and color capabilities for the current session.

## 🧩 Native integration and verification scope

The x64 `PyDeck.Colors.dll` is built from pinned sources with the Microsoft C++ toolchain and copied by MSBuild into application/check/publish output. It does not add a JavaScript runtime or bundle .NET / Windows App Runtime. The managed adapter validates pixel bounds and caches at most 64 schemes. A missing or incompatible native engine is reported as an error; no approximated replacement algorithm is silently substituted.

Native checks cover the algorithm and ABI; managed checks cover persistence and contrast; isolated WinUI fixtures cover decoding, manual colors, pending updates and rendered palettes. These fixtures do not change the Windows wallpaper or real Python configuration. The existence of a check is not a claim that it ran for every release: current results and remaining acceptance work are recorded in [Features](FEATURES.md), with developer procedures in [Development](DEVELOPMENT.md).

## 简体中文

### 🎨 使用动态配色

启用 **Material 3 Expressive** 后，在“设置 → 动态配色”选择桌面壁纸或自定义基色，再选择均衡、表现力或双色方案。展开自定义基色，调整后点击“应用自定义颜色”；双色模式提供两个取色器，一次保存两种颜色。“刷新壁纸配色”重新读取本地壁纸，Fluent 继续使用自身的 Windows 配色与材质规则

“Material 3 Expressive”是界面风格名称，“表现力”是独立的配色选项，两者并不等同。均衡对应官方 `TonalSpot`，表现力对应官方 `Expressive`；明暗主题分别生成，Windows 对比度主题优先

### 📚 算法与来源

PyDeck 使用固定版本的官方 **Material Color Utilities C++**，包括 Celebi 量化、CAM16/HCT、色调调色板和 49 个动态语义颜色角色。壁纸候选色排序移植自 AOSP `ColorScheme.getSeedColors(filter=true)`，保留色度过滤、相邻色相占比评分、色度权重及蓝色后备值 `#1B6EF3`，不使用简单 RGB 平均、HSL 染色或通用 MCU `Score` 替代

具体提交与来源链接见上方“Pinned sources”。C++ 动态角色采用 **2021 颜色规范**和标准对比度，不宣称与 2025、2026 规范或所有 Android 厂商主题完全一致。上游源码、变更清单与 Apache 2.0 许可证随引擎保留，PyDeck 的 MIT 许可证不会改变第三方代码许可

### 🎨 双色如何组合

双色是 PyDeck 的 **`DualSource` 扩展**：第一种基色提供 Tonal Spot 的主色、中性色、变体中性色与错误色调色板，第二种提供次要色与第三色调色板。全部调色板先进入同一个官方 `DynamicScheme`，再统一计算对比度与色调差规则，不是平均两种 RGB，也不是拼接已经生成的明暗主题颜色

主操作与背景跟随第一种基色，选中导航及辅助点缀使用第二种。两种基色相同时，结果与原始 Tonal Spot 完全相同。壁纸模式取同一 AOSP 排序器的前两个候选；只有一个候选时两种基色相同，中性或空图像仍可使用 AOSP 蓝色后备值。手动模式共同保存两种不透明颜色，旧设置缺少第二种时沿用第一种，切换来源保留已保存的手动颜色

这不是 Google 的 **CMF** 方案，也不是 **SPEC_2026** 移植。官方 `SchemeCmf` 确实支持两个来源，但要求 2026 规范，并有不同的调色板、错误色与色调规则；PyDeck 保留固定的 2021 求解器，均衡与表现力两个现有官方方案保持原样

### 🪟 壁纸、隐私与编辑保护

默认读取覆盖 PyDeck 窗口面积最多的显示器壁纸，通过 `IDesktopWallpaper` 获取文件，必要时退回 Windows 桌面 SPI。Windows 解码器处理 EXIF 方向并转换为 sRGB，按 AOSP 面积缩放方式使用最近邻采样，最多输入 12,544 个像素，忽略非完全不透明样本。Windows 适配限制文件大小、图像尺寸和像素数，拒绝远程路径与重解析点；不模拟 Android 壁纸裁切、变暗、低内存量化分支或锁屏策略

取色在后台进行，仅在 Material 壁纸模式下于窗口激活及每 30 秒检查变化，元数据指纹避免反复解码同一文件。缓存只保存有序基色对和元数据哈希，不保存壁纸路径或图像内容，不复制或上传图像。读取失败时保留上次提取的颜色，无缓存时使用自定义基色；旧版单色缓存可先用于启动，再准备新的基色对

自动取色结果不会重建正在编辑的设置、构建、环境页面或获得焦点的文本输入，可等待下次切换页面再应用。两种基色共同更新，只有第二色发生变化也遵守相同规则。手动应用颜色属于明确的外观操作；Fluent 与 Material 之间的风格切换仍在下次启动生效

### 🧩 引擎与验证边界

`PyDeck.Colors.dll` 随 x64 应用和安装包分发，不引入 JavaScript 运行时，也不内置 .NET 或 Windows App Runtime。托管层验证像素数量，最多缓存 64 套配色，缓存键包含两种基色；新增双基色接口保留原有单色接口。缺失或不兼容的引擎会显示错误，不会静默换成近似算法

原生、托管与隔离 WinUI 检查分别覆盖算法、接口、持久化、对比度、图像解码、手动颜色与延后应用。检查工具的存在不代表每次发行都已运行，具体结果和验收边界见[功能说明](FEATURES.zh-CN.md)，开发流程见[开发指南](DEVELOPMENT.zh-CN.md)
