# 📦 安装 PyDeck

[English](INSTALL.md) · **简体中文** · [🏠 项目首页](https://github.com/DM10cn/PyDeck)

📦 **0.6.0 · Windows 11 x64**

从 [GitHub Releases](https://github.com/DM10cn/PyDeck/releases) 下载文件，选择**一种**安装格式。MSI 与 MSIX 不能相互升级，切换格式前请卸载旧格式。两个安装器都不会移除 Python 安装，也不会主动删除 PyDeck 偏好设置

## 📋 准备依赖

本页统一说明用户运行依赖，源码编译工具见[开发指南](https://github.com/DM10cn/PyDeck/blob/main/docs/DEVELOPMENT.zh-CN.md)

| 依赖 | 何时需要 | 官方来源 |
| --- | --- | --- |
| Windows 11，x64 | PyDeck、安装器和原生工具，Windows 10 支持暂缓 | — |
| .NET Runtime 10.0.x，x64，稳定版 | MSI 与 MSIX 的完整 GUI | [.NET 10 下载](https://dotnet.microsoft.com/download/dotnet/10.0)，Desktop Runtime 10 / SDK 10 也提供此运行时 |
| Windows App Runtime 2.x，x64，最低 2.5.1.0 | 两种格式的完整 GUI，需为当前用户注册，可使用同系列兼容更新 | [Windows App SDK 下载](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable，x64 | MSI / 非打包 GUI；MSIX 的框架依赖由 Windows 在安装时解析 | [Microsoft 官方下载](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) |
| Python Install Manager | 管理 Python 时需要，缺少它也能打开 GUI | [Python 官方 Windows 下载](https://www.python.org/downloads/windows/) |

🔌 PyDeck 不内置或自动下载、安装这些共享运行时或 PIM。原生依赖工具与 MSI 文件夹浏览组件静态链接 C++ 基础库，因此这两个小组件自身无需 .NET、Windows App Runtime、WebView2 或另外安装的 VC++ 运行库。离线使用 Python 包前，请先准备好 GUI 运行依赖和 PIM

### 🧰 两种依赖工具

| 入口 | 行为 |
| --- | --- |
| 已安装的 `PyDeck.Launcher.exe` / PyDeck 快捷方式 | 依赖齐全时启动 GUI，否则显示依赖窗口；手动安装缺失组件后，点击**重新检查**，再**打开 PyDeck** |
| 独立的 `PyDeck-Dependencies-0.6.0-win-x64.exe` | 始终显示检查结果和下载入口，**打开 PyDeck 保持禁用**；即使放在应用旁边，也不安装或启动应用，请关闭工具后回到安装器或已安装的快捷方式 |

**下载**按钮打开 Microsoft 官方来源，工具不执行安装程序、自行提权或修改证书信任。独立工具按非打包环境检查，也会提示 VC++；MSIX 自身能否安装，以 Windows 的包依赖检查为准

MSIX 缺少框架时可能在包内启动器运行前就被阻止安装，可先使用独立工具准备依赖；该工具不能绕过 MSIX 的依赖或证书信任要求

🌏 应用和依赖工具提供英语、简体中文、繁体中文（台湾）及日语。应用会保存语言选择，依赖工具每次以英语打开。MSI 向导目前使用英语，原生文件夹窗口使用 Windows 的语言设置

发行附件 `Test-Prerequisites.ps1` 是辅助只读清单，检查常见安装位置及 PATH 中的 PIM。它的提示不等于启动器的精确就绪判断：PATH 之外的管理器可在设置中选择，缺少 PIM 也不阻止 GUI 启动。原生启动器的只读运行时状态可通过 `PyDeck.Launcher.exe --check` 查看

### 🐍 连接 Python Install Manager

**0.6.0** 修改 Python 安装需要 PIM **26.3 或更高版本**，旧版或无法识别的管理器仅支持兼容的只读查询，升级后请重新连接。详见 [Python 管理](https://github.com/DM10cn/PyDeck/blob/v0.6.0/docs/MANAGEMENT.zh-CN.md)，已发布的 0.5.1 安装包尚无这项能力限制

GUI 运行时齐全后，可在未安装 PIM 时打开 PyDeck。在未连接页面或设置中选择**下载 Python Install Manager**，从 Python 官方 Windows 页面手动安装，再点击**重新检查**或**自动检测**，也可在设置中选择管理器文件。按钮只打开下载页面或重新连接，不自行安装 PIM；重连无需重启 PyDeck

## 🛠️ MSI

1. 下载 `PyDeck-0.6.0-win-x64.msi`
2. 输入安装目录，或点击 **Browse…（浏览）** 打开 Windows 文件夹选择窗口
3. 按需勾选**创建桌面快捷方式**与**添加到开始菜单**，默认仅勾选开始菜单
4. 完成安装，从所选快捷方式打开 **PyDeck**

MSI 为当前用户安装，默认目录为 `%LocalAppData%\Programs\PyDeck`，可手动输入或通过原生文件夹窗口选择其他可写目录。修复和后续 MSI 升级会保留目录与快捷方式选择。如果两项快捷方式都关闭，可从所选目录运行 `PyDeck.Launcher.exe`

安装前检查 Windows 11，允许稍后补装 GUI 运行依赖。两类快捷方式均打开原生依赖启动器，运行时检查通过后进入完整应用。请保留整个安装目录；直接打开 `PyDeck.exe` 会跳过依赖窗口

🧑‍💻 当前用户的静默安装也可使用相同选项，`0` 表示关闭，`1` 表示开启

```powershell
msiexec /i PyDeck-0.6.0-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0
```

请选择当前账号有写入权限的目录。原生文件夹选择窗口不依赖 .NET；MSIX 的安装位置由 Windows 管理，不提供这些 MSI 选项

0.6.0 仍使用自签证书，Windows 不会将其识别为公开受信任的发布者。安装 MSI 不要求导入预览证书，遇到 Windows 提示时请先核对下载来源，再决定是否继续

较新的 MSI 会升级同一用户的安装，旧版本会被阻止。可从 **设置 → 应用 → 安装的应用** 卸载，只移除安装器自身的文件和快捷方式，不卸载 Python 或共享运行时

## 🪟 MSIX — 自签安装包

MSIX 要求签名证书受信任，此安装包**没有公开信任的签名**。请仅在核对文件并确实准备使用 PyDeck 时信任该发布者，签名私钥不会分发

1. 从同一发行页下载 `PyDeck-0.6.0-win-x64.msix`、`PyDeck-preview.cer` 和 `SHA256SUMS.txt`
2. 使用 `Get-FileHash -Algorithm SHA256`，将文件哈希与 `SHA256SUMS.txt` 对照
3. 检查证书：发布者 **CN=DM10cn**，指纹 **3912817E181E5EA9AF0DED6BC51E332E99BBDD16**
4. 将公开证书导入 **本地计算机 → 受信任人（Trusted People）**，再打开 MSIX

管理员可在 PowerShell 中手动执行信任操作

```powershell
Import-Certificate -FilePath .\PyDeck-preview.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

随后以正常桌面用户安装

```powershell
Add-AppxPackage -Path .\PyDeck-0.6.0-win-x64.msix
```

**不要**导入“受信任的根证书颁发机构”。PyDeck 的脚本不会自动导入证书或更改系统信任设置。当前预览证书于 **2028-09-23** 到期，安装包未加用于长期分发的时间戳

MSIX 声明依赖 `Microsoft.WindowsAppRuntime.2`，最低版本 `2.5.1.0`，缺少时需要先安装。.NET 10 仍为独立系统依赖。该包运行完整信任的桌面应用，并让 PIM 配置和 Python 文件保持在 MSIX 虚拟化之外

🧹 移除所有由该预览证书签名的包后，可在 `certlm.msc` 中删除 Trusted People 内的对应证书；仍需使用相关包时请保留

## 🔁 更新与数据

MSI 使用固定升级标识，MSIX 使用包名 `DM10cn.PyDeck` 和发布者 `CN=DM10cn`。后续兼容更新需要保持这些身份，替换发布者或切换安装格式需要考虑迁移

设置保留在 `%LocalAppData%\PyDeck`，Python 安装、PIM 配置和共享运行时独立于 PyDeck 安装器。更新或卸载前请关闭 PyDeck，并先等待 Python 操作完成

## 🗜️ 源码归档

使用 Release 的 Assets 区域中 **Source code (zip)** 或 **Source code (tar.gz)** 链接，GitHub 根据发行标签自动生成源码归档，PyDeck 不再上传重复源码包。`SHA256SUMS.txt` 仅校验上传的附件，不包含 GitHub 自动生成的归档。源码与随附指南保留发布时的快照，后续文档修正见[仓库当前指南](https://github.com/DM10cn/PyDeck/blob/main/docs/INSTALL.zh-CN.md)，不替换已打标签的源码或签名安装包

## 🧪 验证边界

正式 Release 状态不代表覆盖所有环境；本机安装和激活检查不能替代干净机器 GUI、辅助功能和所有 DPI 验收。Python 生命周期使用一次性 Sandbox 验收，详见仓库中的 [功能状态](https://github.com/DM10cn/PyDeck/blob/main/docs/FEATURES.zh-CN.md)
