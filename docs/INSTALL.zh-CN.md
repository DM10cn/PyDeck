# 📦 安装 PyDeck

[English](https://github.com/DM10cn/PyDeck/blob/main/docs/INSTALL.md) · **简体中文** · [🏠 项目](https://github.com/DM10cn/PyDeck)

从 [GitHub Releases](https://github.com/DM10cn/PyDeck/releases/tag/v0.7.2) 下载适用于 **Windows 11 x64 的 PyDeck 0.7.2**。MSI、Setup 和依赖工具均**未签名**，无需导入证书。遇到 Windows 提示时，先核对下载来源与 `SHA256SUMS.txt`。

| 文件 | 用途 |
| --- | --- |
| `PyDeck-Setup-0.7.2-win-x64.exe` | 推荐：离线安装与维护，内置运行时安装程序 |
| `PyDeck-0.7.2-win-x64.msi` | 独立的当前用户安装包，运行时需另行准备 |
| `PyDeck-Dependencies-0.7.2-win-x64.exe` | 依赖检查与官方下载入口 |

## 🚀 使用 Setup

1. 等待 Python 操作结束，退出 PyDeck，包括托盘图标。
2. 正常打开 Setup，无需“以管理员身份运行”；可选择英语、简体中文、繁体中文（台湾）或日语。
3. 选择可写目录、桌面／开始菜单快捷方式，以及初始 **Material 3 Expressive** 或 **Windows Fluent** 风格；已有应用偏好优先。
4. 查看依赖清单，点击“安装”；缺少的运行时会离线准备，必要时请求管理员授权。
5. 安装完成后点击“打开 PyDeck”。

Setup 内置 MSI 和微软运行时安装程序，使用者无需 Visual Studio、.NET SDK 或 Windows SDK。卸载 PyDeck 后，共享运行时仍保留。

“取消”会等待当前安装程序返回，进行中的操作可能完成。需要重启时停止后续步骤，不会自动重启电脑。“打开日志”可查看本次 `%LocalAppData%\PyDeck-Setup-{GUID}` 下的 `setup.log`、`result.json` 与安装日志。

## 📋 运行依赖

| 依赖 | 用途 | 官方来源 |
| --- | --- | --- |
| Windows 11 x64 | 应用与安装器 | — |
| .NET Runtime 10.0.x x64 稳定版 | GUI；Desktop Runtime 10 / SDK 10 也包含它 | [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Windows App Runtime 2.x x64，最低 2.5.1.0 | GUI，需为当前用户注册 | [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable x64 | MSI／非打包 GUI | [微软下载](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) |
| Python Install Manager 26.3+ | 修改 Python 安装 | [Python Windows 下载](https://www.python.org/downloads/windows/) |

独立 MSI 不安装共享运行时。应用快捷方式通过 `PyDeck.Launcher.exe` 启动：依赖齐全时进入 GUI，否则显示清单。准备好依赖后点击“重新检查”，再打开应用。

独立 Dependencies 工具仅检查依赖并打开下载链接，“打开 PyDeck”始终禁用；启动应用请回到已安装的快捷方式。

没有 PIM 也能打开 GUI。从 Python 官方页面安装后，在设置中“重新检查”“自动检测”或手动选择程序。使用本地构建无需 PIM，详见 [Python 管理](https://github.com/DM10cn/PyDeck/blob/main/docs/MANAGEMENT.zh-CN.md)。

## 🛠️ 独立 MSI

打开 MSI，选择安装目录、快捷方式与初始风格后安装。默认目录为 `%LocalAppData%\Programs\PyDeck`，默认勾选开始菜单。两个快捷方式都关闭时，可在安装目录运行 `PyDeck.Launcher.exe`；请保留完整安装目录。

MSI 向导使用英语，原生目录选择器跟随 Windows 语言。两套界面均已包含，可在“设置 → 外观”中切换。

无人值守的当前用户安装：

```powershell
msiexec /i PyDeck-0.7.2-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0 PYDECKSTYLE=Material
```

快捷方式属性接受 `0` 或 `1`；`PYDECKSTYLE` 接受 `Material` 或 `Fluent`，这些初始选项不会覆盖已保存的应用偏好。

## 🔧 修复、升级与卸载

相同 MSI ProductCode 可在 Setup 中“修复”，保留原安装目录。更高版本 MSI 会升级当前用户的安装，降级会被阻止。同版本但不同 MSI 的包不能修复现有安装，请先卸载该安装，或使用更高版本。

在 Setup 中选择“卸载…”，或通过“设置 → 应用 → 已安装的应用”移除。Python 安装、虚拟环境、共享运行时和应用偏好均保留。

MSI 与 MSIX 是独立安装格式，切换前先卸载原格式。已有的历史 MSIX 下载请参阅[归档安装说明](https://github.com/DM10cn/PyDeck/blob/main/docs/archive/INSTALL-MSIX.zh-CN.md)。

📖 [Python 管理](https://github.com/DM10cn/PyDeck/blob/main/docs/MANAGEMENT.zh-CN.md) · [构建 Python](https://github.com/DM10cn/PyDeck/blob/main/docs/BUILD_PYTHON.md#简体中文) · [动态颜色](https://github.com/DM10cn/PyDeck/blob/main/docs/MONET.md)
