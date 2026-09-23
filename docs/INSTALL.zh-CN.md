# 📦 安装 PyDeck

[English](INSTALL.md) · **简体中文** · [🏠 项目首页](https://github.com/DM10cn/PyDeck)

🚧 **0.4.0 预览版 · Windows 11 x64**

从 [GitHub Releases](https://github.com/DM10cn/PyDeck/releases) 下载文件，选择**一种**安装格式。MSI 与 MSIX 不能相互升级，切换格式前请卸载旧格式。两个安装器都不会移除 Python 安装，也不会主动删除 PyDeck 偏好设置

## 📋 先安装依赖

| 依赖 | 官方来源 |
| --- | --- |
| .NET Runtime 10，x64 | [.NET 10 下载](https://dotnet.microsoft.com/download/dotnet/10.0)，选择 **.NET Runtime**，也可使用 Desktop Runtime / SDK |
| Windows App Runtime 2.5.1 或兼容的较新 2.x，x64 | [Windows App SDK 下载](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable，x64 | [Microsoft 官方下载](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist)，非打包 / MSI 运行路径需要此依赖 |
| Python Install Manager | [Windows 上的 Python](https://docs.python.org/3/using/windows.html) |

🔌 **PyDeck 不内置或自动下载 .NET 和 Windows App Runtime**，Python Install Manager 也需要单独安装。离线使用前，请在联网时准备好这些依赖

发行附件中的 `Test-Prerequisites.ps1` 可进行只读检查。如果管理器不在 PATH 中，可在 PyDeck 设置中手动选择

## 🛠️ MSI

1. 下载 `PyDeck-0.4.0-win-x64.msi`
2. 运行安装向导
3. 从开始菜单打开 **PyDeck**

MSI 为当前用户安装到 `%LocalAppData%\Programs\PyDeck`，安装前检查 Windows 11 与 .NET 10。Windows App Runtime 在应用启动时解析，需要先通过上方链接单独安装；PIM 连接在应用内检查

预览版使用自签证书，Windows 不会将其识别为公开受信任的发布者。安装 MSI 不要求导入预览证书，遇到 Windows 提示时请先核对下载来源，再决定是否继续

较新的 MSI 会升级同一用户的安装，旧版本会被阻止。可从 **设置 → 应用 → 安装的应用** 卸载，只移除安装器自身的文件和快捷方式，不卸载 Python 或共享运行时

## 🪟 MSIX — 自签预览版

MSIX 要求签名证书受信任，本预览版**没有公开信任的签名**。请仅在核对文件并确实准备测试 PyDeck 时信任预览发布者，签名私钥不会分发

1. 从同一发行页下载 `PyDeck-0.4.0-win-x64.msix`、`PyDeck-preview.cer` 和 `SHA256SUMS.txt`
2. 使用 `Get-FileHash -Algorithm SHA256`，将文件哈希与 `SHA256SUMS.txt` 对照
3. 检查证书：发布者 **CN=DM10cn**，指纹 **3912817E181E5EA9AF0DED6BC51E332E99BBDD16**
4. 将公开证书导入 **本地计算机 → 受信任人（Trusted People）**，再打开 MSIX

管理员可在 PowerShell 中手动执行信任操作

```powershell
Import-Certificate -FilePath .\PyDeck-preview.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

随后以正常桌面用户安装

```powershell
Add-AppxPackage -Path .\PyDeck-0.4.0-win-x64.msix
```

**不要**导入“受信任的根证书颁发机构”。PyDeck 的脚本不会自动导入证书或更改系统信任设置。当前预览证书于 **2028-09-23** 到期，安装包未加用于长期分发的时间戳

MSIX 声明依赖 `Microsoft.WindowsAppRuntime.2`，最低版本 `2.5.1.0`，缺少时需要先安装。.NET 10 仍为独立系统依赖。该包运行完整信任的桌面应用，并让 PIM 配置和 Python 文件保持在 MSIX 虚拟化之外

🧹 移除所有由该预览证书签名的包后，可在 `certlm.msc` 中删除 Trusted People 内的对应证书；仍需使用相关包时请保留

## 🔁 更新与数据

MSI 使用固定升级标识，MSIX 使用包名 `DM10cn.PyDeck` 和发布者 `CN=DM10cn`。后续兼容更新需要保持这些身份，替换发布者或切换安装格式需要考虑迁移

设置保留在 `%LocalAppData%\PyDeck`，Python 安装、PIM 配置和共享运行时独立于 PyDeck 安装器。更新或卸载前请关闭 PyDeck，并先等待 Python 操作完成

## 🗜️ 源码归档

`PyDeck-0.4.0-source.zip` 和 `PyDeck-0.4.0-source.tar.gz` 仅包含发行提交中的文件，排除构建产物、日志、本地设置、包缓存和签名密钥，也可使用 GitHub 自动提供的源码链接

## 🧪 预览版边界

这是预览版，本机安装和激活检查不能替代干净机器、辅助功能、所有 DPI 设置及完整 Python 安装 / 更新 / 卸载验收，详见仓库中的 [功能状态](https://github.com/DM10cn/PyDeck/blob/main/docs/FEATURES.zh-CN.md)
