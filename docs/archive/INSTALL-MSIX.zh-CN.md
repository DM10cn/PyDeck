# 🪟 历史 0.7.1-fix MSIX — 自签安装包

[English](INSTALL-MSIX.md) · **简体中文** · [当前安装说明](../INSTALL.zh-CN.md)

本节仅保留给已发布的 **0.7.1-fix** MSIX；0.7.2 发行不包含 MSIX 或新证书。历史 MSIX 要求签名证书受信任，但**没有公开信任的签名**。请仅在核对文件并确实准备使用该包时信任发布者，签名私钥不会分发

1. 从同一发行页下载 `PyDeck-0.7.1-fix-win-x64.msix`、`PyDeck-preview.cer` 和 `SHA256SUMS.txt`
2. 使用 `Get-FileHash -Algorithm SHA256`，将文件哈希与 `SHA256SUMS.txt` 对照
3. 检查证书：发布者 **CN=DM10cn**，指纹 **3912817E181E5EA9AF0DED6BC51E332E99BBDD16**
4. 将公开证书导入 **本地计算机 → 受信任人（Trusted People）**，再打开 MSIX

管理员可在 PowerShell 中手动执行信任操作

```powershell
Import-Certificate -FilePath .\PyDeck-preview.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

随后以正常桌面用户安装

```powershell
Add-AppxPackage -Path .\PyDeck-0.7.1-fix-win-x64.msix
```

**不要**导入“受信任的根证书颁发机构”。PyDeck 的脚本不会自动导入证书或更改系统信任设置。当前预览证书于 **2028-09-23** 到期，安装包未加用于长期分发的时间戳

MSIX 声明依赖 `Microsoft.WindowsAppRuntime.2`，最低版本 `2.5.1.0`，缺少时需要先安装。.NET 10 仍为独立系统依赖。该包运行完整信任的桌面应用，并让 PIM 配置和 Python 文件保持在 MSIX 虚拟化之外

MSIX 同样包含两种界面，可在应用的**设置 → 外观**中选择，没有 MSI 式安装选项页。不存在已有偏好或安装器默认值时，应用以 Material 启动

🧹 移除所有由该预览证书签名的包后，可在 `certlm.msc` 中删除 Trusted People 内的对应证书；仍需使用相关包时请保留
