# 🧑‍💻 开发指南

[English](DEVELOPMENT.md) · **简体中文** · [🏠 首页](../README.zh-CN.md)

## 🧰 工具链

- Windows 11 x64
- PowerShell 7，用于仓库的构建与发行脚本
- Visual Studio 2026，安装 **WinUI 应用程序开发**
- **MSVC x64/x86 编译工具**（`Microsoft.VisualStudio.Component.VC.Tools.x86.x64`），包括 C++ 标准头文件与桌面库，用于原生启动器和 MSI 操作
- .NET SDK **10.0.400** 或同一 **10.0.4xx** 功能带的后续补丁，以 `global.json` 的 `latestPatch` 为准
- Windows SDK **10.0.26100**

以上为源码编译工具。运行发布后的 GUI 请按[用户依赖指南](INSTALL.zh-CN.md)准备环境，非打包版本也需要 VC++。打开 GUI 可以没有 PIM，但 Python 操作、真实 PIM 检查和 GUI 冒烟检查需要它

应用目标框架为 `net10.0-windows10.0.26100.0`，项目中较低的最低平台值不代表已经验证 Windows 10 支持，当前构建仅面向 x64

项目在 `PimGui.App.csproj` 和 `packages.lock.json` 中固定各个 Windows App SDK 组件包版本；这些编译引用与安装指南中的兼容运行时版本是不同概念

## 🔨 构建

```powershell
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Run.ps1
```

构建脚本将 NuGet 缓存和 .NET CLI 状态放在已忽略的 `.local/` 中，发布时创建带时间戳的新目录，并在 `artifacts/latest-build.txt` 中记录位置供启动脚本读取

Visual Studio 构建 `PimGui.slnx` 中的 C# 项目；`Build.ps1 -Publish` 还会编译 C++ 原生启动器、确认只导入系统 DLL，并运行原生检查。不带 `-Publish` 的 `Build.ps1 -Checks` 仅运行核心检查。`Run.ps1` 启动最近一次开发发布的启动器，不使用发行安装包或单独的 Visual Studio 构建输出

输出依赖单独安装的 .NET 和 Windows App Runtime。脚本检查运行时配置、编译后的 WinUI 资源，以及是否意外内置原生运行时文件，使用时需要保留整个输出目录

`EnableMsixTooling` 用于编译 WinUI 资源，`WindowsPackageType=None` 表示开发输出不打包。独立的 [发行流程](RELEASING.zh-CN.md) 用于生成 MSI / MSIX，源码仓库不包含签名私钥或运行时安装程序

NuGet 版本记录在 `packages.lock.json` 中，可使用以下命令强制按提交的依赖图还原

```powershell
dotnet restore PimGui.slnx --locked-mode -p:Platform=x64
```

## 🧭 源码结构

| 位置 | 职责 |
| --- | --- |
| `src/PimGui.Core` | PIM 发现与协议、进程调用、离线包、设置、版本目录和语言资源 |
| `src/PimGui.App` | WinUI 页面、语义设计 token、外观策略、操作面板和应用内冒烟检查 |
| `src/PyDeck.Launcher` | Win32 依赖窗口、系统 DLL 导入、官方下载链接和受控 GUI 启动 |
| `packaging/msi` | WiX 安装选项、原生文件夹选择与设置操作，静态链接 C++ 基础库 |
| `tests/PimGui.E2E` | 破坏性 Python 生命周期测试，仅在一次性 Windows Sandbox 中运行 |
| `tests/PimGui.Checks` | C# 核心回归检查和按需启用的 PIM 集成检查 |
| `tests/Launcher.Checks.cpp` | 原生依赖组合、窗口控件、语言选择和重新检查行为 |
| `scripts` | 构建、启动、GUI 冒烟测试和图标生成 |
| `docs` | 英语及简体中文文档 |

产品名称为 PyDeck，已有的 `PimGui` 项目名称和命名空间仅用于内部标识。`PyDeck.exe` 是 C# GUI，`PyDeck.Launcher.exe` 是正常启动入口。小型头文件 `resource.h` 只定义原生控件编号，GitHub 可能将其统计为 C，它不是独立程序

启动器使用 `/MT` 静态链接 C++ 基础库，可单独执行 `scripts/Build-Launcher.ps1 -OutputDirectory artifacts/launcher -Checks` 构建并检查。向 `PyDeck.Launcher.exe` 传入 `--dependencies` 可在依赖齐全时仍显示窗口；`--check` 输出只读 JSON 状态，就绪时退出码为 0，否则为缺失运行时位掩码，此处不检查 PIM。[安装指南](INSTALL.zh-CN.md)说明了启动器与独立检查工具的区别。原生测试使用注入的缺失状态和真实 Win32 控件，干净机器部署仍需单独验收

## 🧪 测试

```powershell
# 构建并运行核心回归检查
.\scripts\Build.ps1 -Checks

# 额外查询真实 PIM 的安装列表和在线目录，只读操作
.\scripts\Build.ps1 -LiveChecks

# 对已发布的 GUI 进行应用内状态与渲染检查
.\scripts\Smoke-Test.ps1
```

核心检查覆盖异常协议响应、版本身份、陈旧状态与非托管版本、操作锁、进程输出限额、配置保留、离线校验、背景策略和语言键一致性

GUI 检查需要可用的运行时、PIM 和在线目录访问，使用独立设置，将截图和结果写入已忽略的 `artifacts/`，覆盖页面状态、缺少管理器时的恢复入口、筛选、主题、透明效果设置、语言切换、布局和设置持久化。这是**应用内状态与渲染检查**，不是外部鼠标或键盘自动化；RenderTargetBitmap 无法捕获合成器中的 Mica / Acrylic 和原生标题栏按钮

可选集成检查

```powershell
# 验证包含单个官方 embeddable 包的离线目录，独立解压并启动解释器
# 请替换为自己的离线包路径
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python"

# 启动真实测试下载，在首次进度事件时取消
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python" --cancel-live-download
```

这些检查需要 PIM，可能创建临时测试文件，下载检查还会访问网络；不会安装到常规 PIM 管理的解释器目录。日志和截图可能包含本地路径，分享前请检查

已完成的验证与待验收项目统一见[功能状态](FEATURES.zh-CN.md)。MSI / MSIX 安装检查见[发行流程](RELEASING.zh-CN.md)，这些检查不能证明完整 Python 解释器生命周期已经通过

## 🐍 隔离生命周期验收

需要带 Windows Sandbox CLI（`wsb`）的 Windows 11。脚本创建并关闭自己的无界面沙盒，不使用日常 Python 安装：

```powershell
.\scripts\Test-PimLifecycle.ps1
```

覆盖官方 PIM 25.2 → 26.3、真实 Python 补丁更新、损坏与修复、下载统计、取消、默认切换、卸载和离线重装。`-SeedBundleDirectory` 可指定平铺的较旧官方离线包；`-InstallerDirectory` 可复用缓存的 `pim-25.2.msi` / `pim-26.3.msi`，仍检查 PSF Authenticode 签名。结果位于 `artifacts/pim-e2e-*`，某项失败会停止后续依赖项，未执行不能计为通过

不要在宿主机创建标记来运行测试。即使安装目录隔离，PIM 的注册清理仍有用户级影响。沙盒使用一次性的 System 账号，因此不能替代所有交互用户、Store 包、管理员策略或干净机器 GUI 验收。详见 [Python 管理](MANAGEMENT.zh-CN.md)

## 🎨 外观与本地化

颜色、间距和表面层次通过 `DesignTokens` 与外观策略定义，避免在各个页面中加入按风格分支的布局逻辑。Fluent 可使用 Mica / Acrylic，Material 3 Expressive 使用实色；无关设置变化应保留现有原生背景控制器

应用文案位于 `src/PimGui.Core/Strings` 的嵌入式 JSON 资源中，`en-US`、`zh-CN`、`zh-TW`、`ja-JP` 的键需要一致，默认英语。界面优先使用自然、简短的词句，CJK 标签减少不必要的句末标点，PIM 标识和原始输出不翻译

仓库 Markdown 文档仅维护英语和简体中文，修改时请同步更新并保持章节对应。用户依赖统一维护在 `INSTALL`，功能与验收状态在 `FEATURES`，构建方法在本页，打包流程在 `RELEASING`；README 概述并链接这些页面，避免再维护一份独立版本表

## 🔐 本地数据与贡献

设置保存在 `%LocalAppData%\PyDeck\settings.json`，仅当新文件不存在时读取旧的 `PimGui` 设置用于迁移。活动日志保留在内存中，崩溃诊断仅保存在本地并限制大小

请勿提交构建产物、缓存、凭据、签名密钥、私有配置、个人日志、截图或规划笔记。`.gitignore` 覆盖常见情况，但仍应检查暂存区内容，参见 [安全说明](SECURITY.zh-CN.md)

图标原图位于 `src/PimGui.App/Assets/AppIcon.Source.png`，可运行 `scripts/Build-Icon.ps1` 重新生成 PNG / ICO。修改品牌素材前请查看 [第三方声明](THIRD-PARTY-NOTICES.zh-CN.md)

## 🧰 T3 检查

核心检查覆盖正式版本信息、源地址与跨站重定向确认、过期源快照、Shebang 保留与冲突，以及未完成环境的保留。将 `PYDECK_TEST_VENV_PYTHON` 设为可信的本机 Python 路径，可追加真实临时 venv 创建、检查和移除记录测试，测试文件留在已忽略的 artifacts 目录

`Test-PimLifecycle.ps1 -T3Only` 在一次性 Windows Sandbox 中运行安装源、Shebang 和 venv 验收。HTTPS 测试仅在沙箱内创建短期 localhost 证书，结束后移除其信任条目，不更改宿主机证书信任。不加 `-T3Only` 则运行完整生命周期
