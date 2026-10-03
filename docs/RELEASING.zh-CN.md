# 🚀 发行流程

[English](RELEASING.md) · **简体中文** · [🏠 首页](../README.zh-CN.md)

## 🧰 工具与版本

使用[开发工具链](DEVELOPMENT.zh-CN.md)、PowerShell 7、WiX 7.0.0 与 Windows SDK MakeAppx / SignTool。通过 `wix extension add -g WixToolset.UI.wixext/7.0.0` 安装扩展；请自行审阅 [WiX OSMF 条款](https://docs.firegiant.com/wix/osmf/)，脚本不代为接受。

构建前提交源码。`PimGui.App.csproj` 的 `Version` 提供三段式 MSI 版本，可选 `InformationalVersion` 提供显示与文件名标签，必须以前者开头；MSIX 增加 `.0`。常规升级应提高数字版本，仅修改显示标签不能形成升级。

## 📦 MSI 与离线 Setup

当前使用未签名的 MSI/Setup，显式选择此方式：

```powershell
.\scripts\Build-Release.ps1 -AllowUnsigned -MsiOnly
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release -AllowUnsigned
```

构建依次还原锁定依赖、检查原生颜色引擎与核心代码、发布应用、构建并检查启动器和 MSI 操作、生成 MSI、构建并检查 Setup。哈希一致的原生颜色构建可复用已通过检查的结果。`-SkipChecks` 跳过回归并记录在 `build.json`，编译成功不等于回归通过。

Setup 内置 MSI 和未修改的微软运行时安装程序。`packaging/setup/prerequisites.json` 固定下载地址、大小和 SHA-256，构建时还会核对微软签名。缓存位于已忽略的 `.local/setup-prerequisites`，修改固定值前先审阅上游变化。

使用现有同版本未签名 MSI 重建 Setup：

```powershell
.\scripts\Build-Setup.ps1 -MsiPath <msi> -OutputDirectory .\artifacts\setup-test -AllowUnsignedMsi -Checks
.\scripts\Test-Setup.ps1 -SetupPath <setup-exe> -MsiPath <msi>
```

`Test-Release` 检查必需文件、MSI 身份与选项连接，并提取核对 Setup 内置安装器，不执行安装。未签名元数据必须配合 `-AllowUnsigned`；`msiOnly` 决定是否要求 MSIX，缺少该字段的旧元数据按双格式处理。缺少应有的包会失败。

## 🔏 可选签名与 MSIX

```powershell
$thumbprint = .\scripts\New-PreviewCertificate.ps1
.\scripts\Build-Release.ps1 -CertificateThumbprint $thumbprint
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release
```

省略 `-MsiOnly` 时同时生成 MSIX。应用、启动器、安装包与 Setup 均参与签名，只导出公开证书。预览私钥位于 `CurrentUser\My` 且不可导出；生成证书不代表公开受信任，脚本不改变系统证书信任。MSIX 发布者须匹配证书主体。

元数据声明已签名时，即使传入 `-AllowUnsigned` 也必须具有匹配的证书和签名。MSIX 还检查身份、外部运行时声明、排除文件、分块哈希与 CMS 签名。未签名 MSIX 可供检查，不适合正常安装；历史预览包的信任说明已[归档](archive/INSTALL-MSIX.zh-CN.md)。

## 🧪 安装验收

包检查不执行实际安装或 GUI 操作。请在一次性 Windows 环境检查缺少运行时、UAC 取消、重启处理、离线安装、修复、升级、降级拒绝与卸载。实际结果保存在已忽略的 artifacts 中，不沿用历史通过数量。

- `Test-MsiOptions.ps1 -ReleaseDirectory <release>` 安装并移除独立测试包，检查目录、快捷方式、风格、修复与回滚。
- `Test-MsiUpgrade.ps1 -ReleaseDirectory <new> -PreviousReleaseDirectory <previous>` 检查实际包替换，发现已有 PyDeck 安装时拒绝运行。
- `Smoke-Test.ps1 -BuildDirectory <安装目录>` 检查 MSI GUI；Windows PowerShell 的 `Smoke-Packaged.ps1` 检查 MSIX 激活，均需满足各自依赖。

这些按需检查会安装或移除测试产品，仅在预定测试环境运行。核心、原生与 GUI 检查方法见[开发指南](DEVELOPMENT.zh-CN.md)，历史记录见[归档](archive/README.md)。

## 🗜️ 发布

`Export-Source.ps1 -ReleaseDirectory <release>` 准备双语安装指南、依赖检查脚本与哈希，要求干净工作区且提交匹配 `build.json`。上传前审阅 `assets/`，仅发布选定的包、指南与校验清单。使用 GitHub 自动提供的源码 ZIP/TAR，不另传重复源码压缩包、日志、缓存、密钥或 `build.json`。

将实际构建提交标为 `v<version>`。发行介绍分为英语与简体中文，以简洁功能条目和少量 emoji 为主，每种语言分别约 160–200 英文单词／中文字；开发检查记录不塞进产品介绍。仓库 README 介绍当前功能，版本变化统一维护在 GitHub Releases。

文档和工具维护可直接更新 `main`，无需重建未变更的公开二进制或移动标签。附件默认保留发布时快照；若明确刷新附件，应同步更新相应校验项并核对远端哈希。

## 🔁 稳定身份

| 项目 | 值 |
| --- | --- |
| MSI UpgradeCode | `D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA` |
| MSI 范围 | 当前用户 |
| MSIX 包名／发布者／应用 ID | `DM10cn.PyDeck` / `CN=DM10cn` / `App` |

保留稳定的 MSI 组件身份，在 `InstallInitialize` 后以事务方式移除旧产品。升级使用完整 MSI，应用偏好和 venv 记录保留在安装载荷之外。MSI/MSIX 不自动跨格式迁移；独立安装包不内置共享运行时，Setup 通过独立运行时安装程序准备依赖。
