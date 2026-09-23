# 📦 安装 PyDeck

[English](INSTALL.md) · **简体中文** · [🏠 项目首页](https://github.com/DM10cn/PyDeck)

🚧 **0.5.1 预览版 · Windows 11 x64**

从 [GitHub Releases](https://github.com/DM10cn/PyDeck/releases) 下载文件，选择**一种**安装格式。MSI 与 MSIX 不能相互升级，切换格式前请卸载旧格式。两个安装器都不会移除 Python 安装，也不会主动删除 PyDeck 偏好设置

## 📋 准备依赖

🧰 原生依赖窗口无需 .NET、Windows App Runtime、WebView2 或另外安装的 VC++ 运行库即可启动。点击**下载**打开 Microsoft 官方来源，安装 x64 版本后点击**重新检查**，再**打开 PyDeck**。窗口不会执行安装程序、自行提权或修改证书信任，默认英语，支持简体中文、繁体中文（台湾）和日语

MSIX 安装前可先运行独立的 `PyDeck-Dependencies-0.5.1-win-x64.exe`。Windows 可能在缺少框架依赖时阻止 MSIX 安装，届时包内窗口也无法运行；独立工具不受这一依赖链影响，补齐后再回到 MSIX 安装程序

| 依赖 | 官方来源 |
| --- | --- |
| .NET Runtime 10，x64 | [.NET 10 下载](https://dotnet.microsoft.com/download/dotnet/10.0)，选择 **.NET Runtime**，也可使用 Desktop Runtime / SDK |
| Windows App Runtime 2.5.1 或兼容的较新 2.x，x64 | [Windows App SDK 下载](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable，x64 | [Microsoft 官方下载](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)，非打包 / MSI 运行路径需要此依赖 |
| Python Install Manager | [Windows 上的 Python](https://docs.python.org/3/using/windows.html) |

🔌 **PyDeck 不内置或自动下载 .NET 和 Windows App Runtime**，Python Install Manager 也需要单独安装。离线使用前，请在联网时准备好这些依赖

发行附件中的 `Test-Prerequisites.ps1` 可进行只读检查。如果管理器不在 PATH 中，可在 PyDeck 设置中手动选择

## 🛠️ MSI

1. 下载 `PyDeck-0.5.1-win-x64.msi`
2. 输入安装目录，或点击 **Browse…（浏览）** 打开 Windows 文件夹选择窗口
3. 按需勾选**创建桌面快捷方式**与**添加到开始菜单**，默认仅勾选开始菜单
4. 完成安装，从所选快捷方式打开 **PyDeck**

MSI 为当前用户安装，默认目录为 `%LocalAppData%\Programs\PyDeck`，可手动输入或通过原生文件夹窗口选择其他可写目录。修复和后续 MSI 升级会保留目录与快捷方式选择。如果两项快捷方式都关闭，可从所选目录运行 `PyDeck.Launcher.exe`

安装前检查 Windows 11，允许稍后补装 .NET。两类快捷方式均打开原生依赖启动器，检测通过后进入完整应用。请保留整个安装目录；直接打开 `PyDeck.exe` 会跳过依赖窗口

🧑‍💻 当前用户的静默安装也可使用相同选项，`0` 表示关闭，`1` 表示开启

```powershell
msiexec /i PyDeck-0.5.1-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0
```

请选择当前账号有写入权限的目录。原生文件夹选择窗口不依赖 .NET；MSIX 的安装位置由 Windows 管理，不提供这些 MSI 选项

🐍 未安装 Python Install Manager 也能进入主界面。在未连接页面或设置中点击**下载 Python Install Manager**，从 Python 官方 Windows 页面安装管理器后，点击**重新检查**或**自动检测**即可连接，无需重启。按钮仅打开官方下载页面，不会静默安装 PIM 或 Python

预览版使用自签证书，Windows 不会将其识别为公开受信任的发布者。安装 MSI 不要求导入预览证书，遇到 Windows 提示时请先核对下载来源，再决定是否继续

较新的 MSI 会升级同一用户的安装，旧版本会被阻止。可从 **设置 → 应用 → 安装的应用** 卸载，只移除安装器自身的文件和快捷方式，不卸载 Python 或共享运行时

## 🪟 MSIX — 自签预览版

MSIX 要求签名证书受信任，本预览版**没有公开信任的签名**。请仅在核对文件并确实准备测试 PyDeck 时信任预览发布者，签名私钥不会分发

1. 从同一发行页下载 `PyDeck-0.5.1-win-x64.msix`、`PyDeck-preview.cer` 和 `SHA256SUMS.txt`
2. 使用 `Get-FileHash -Algorithm SHA256`，将文件哈希与 `SHA256SUMS.txt` 对照
3. 检查证书：发布者 **CN=DM10cn**，指纹 **3912817E181E5EA9AF0DED6BC51E332E99BBDD16**
4. 将公开证书导入 **本地计算机 → 受信任人（Trusted People）**，再打开 MSIX

管理员可在 PowerShell 中手动执行信任操作

```powershell
Import-Certificate -FilePath .\PyDeck-preview.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

随后以正常桌面用户安装

```powershell
Add-AppxPackage -Path .\PyDeck-0.5.1-win-x64.msix
```

**不要**导入“受信任的根证书颁发机构”。PyDeck 的脚本不会自动导入证书或更改系统信任设置。当前预览证书于 **2028-09-23** 到期，安装包未加用于长期分发的时间戳

MSIX 声明依赖 `Microsoft.WindowsAppRuntime.2`，最低版本 `2.5.1.0`，缺少时需要先安装。.NET 10 仍为独立系统依赖。该包运行完整信任的桌面应用，并让 PIM 配置和 Python 文件保持在 MSIX 虚拟化之外

🧹 移除所有由该预览证书签名的包后，可在 `certlm.msc` 中删除 Trusted People 内的对应证书；仍需使用相关包时请保留

## 🔁 更新与数据

MSI 使用固定升级标识，MSIX 使用包名 `DM10cn.PyDeck` 和发布者 `CN=DM10cn`。后续兼容更新需要保持这些身份，替换发布者或切换安装格式需要考虑迁移

设置保留在 `%LocalAppData%\PyDeck`，Python 安装、PIM 配置和共享运行时独立于 PyDeck 安装器。更新或卸载前请关闭 PyDeck，并先等待 Python 操作完成

## 🗜️ 源码归档

`PyDeck-0.5.1-source.zip` 和 `PyDeck-0.5.1-source.tar.gz` 仅包含发行提交中的文件，排除构建产物、日志、本地设置、包缓存和签名密钥，也可使用 GitHub 自动提供的源码链接

## 🧪 预览版边界

这是预览版，本机安装和激活检查不能替代干净机器、辅助功能、所有 DPI 设置及完整 Python 安装 / 更新 / 卸载验收，详见仓库中的 [功能状态](https://github.com/DM10cn/PyDeck/blob/main/docs/FEATURES.zh-CN.md)
