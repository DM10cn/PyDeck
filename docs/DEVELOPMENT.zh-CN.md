# 🧑‍💻 开发指南

[English](DEVELOPMENT.md) · **简体中文** · [🏠 首页](../README.zh-CN.md)

## 🧰 工具链

- Windows 11 x64
- Visual Studio 2026，安装 **WinUI 应用程序开发**
- .NET SDK **10.0.400** 或兼容补丁版本，以 `global.json` 为准
- Windows SDK **10.0.26100**
- 运行 GUI 还需要 .NET Runtime 10 x64、Windows App Runtime **2.5.1 x64** 和 Python Install Manager

应用目标框架为 `net10.0-windows10.0.26100.0`，项目中较低的最低平台值不代表已经验证 Windows 10 支持，当前构建仅面向 x64

## 🔨 构建

```powershell
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Run.ps1
```

构建脚本将 NuGet 缓存和 .NET CLI 状态放在已忽略的 `.local/` 中，发布时创建带时间戳的新目录，并在 `artifacts/latest-build.txt` 中记录位置供启动脚本读取

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
| `tests/PimGui.Checks` | 核心回归检查和按需启用的 PIM 集成检查 |
| `scripts` | 构建、启动、GUI 冒烟测试和图标生成 |
| `docs` | 英语及简体中文文档 |

产品名称为 PyDeck，已有的 `PimGui` 项目名称和命名空间仅用于内部标识

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

GUI 检查使用独立设置，将截图和结果写入已忽略的 `artifacts/`，覆盖页面状态、筛选、主题、透明效果设置、语言切换、布局和设置持久化。这是**应用内状态与渲染检查**，不是外部鼠标或键盘自动化；RenderTargetBitmap 无法捕获合成器中的 Mica / Acrylic 和原生标题栏按钮

可选集成检查

```powershell
# 验证包含单个官方 embeddable 包的离线目录，独立解压并启动解释器
# 请替换为自己的离线包路径
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python"

# 启动真实测试下载，在首次进度事件时取消
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python" --cancel-live-download
```

这些检查需要 PIM，可能创建临时测试文件，下载检查还会访问网络；不会安装到常规 PIM 管理的解释器目录。日志和截图可能包含本地路径，分享前请检查

🚧 后续仍需在专用环境验收完整解释器生命周期，并补充外部键盘与读屏、原生背景视觉、DPI 和干净机器部署测试，核心检查通过不能替代这些验收

## 🎨 外观与本地化

颜色、间距和表面层次通过 `DesignTokens` 与外观策略定义，避免在各个页面中加入按风格分支的布局逻辑。Fluent 可使用 Mica / Acrylic，Material 3 Expressive 使用实色；无关设置变化应保留现有原生背景控制器

应用文案位于 `src/PimGui.Core/Strings` 的嵌入式 JSON 资源中，`en-US`、`zh-CN`、`zh-TW`、`ja-JP` 的键需要一致，默认英语。界面优先使用自然、简短的词句，CJK 标签减少不必要的句末标点，PIM 标识和原始输出不翻译

仓库 Markdown 文档仅维护英语和简体中文，修改时请同步更新

## 🔐 本地数据与贡献

设置保存在 `%LocalAppData%\PyDeck\settings.json`，仅当新文件不存在时读取旧的 `PimGui` 设置用于迁移。活动日志保留在内存中，崩溃诊断仅保存在本地并限制大小

请勿提交构建产物、缓存、凭据、签名密钥、私有配置、个人日志、截图或规划笔记。`.gitignore` 覆盖常见情况，但仍应检查暂存区内容，参见 [安全说明](SECURITY.zh-CN.md)

图标原图位于 `src/PimGui.App/Assets/AppIcon.Source.png`，可运行 `scripts/Build-Icon.ps1` 重新生成 PNG / ICO。修改品牌素材前请查看 [第三方声明](THIRD-PARTY-NOTICES.zh-CN.md)
