# 🚀 发行流程

[English](RELEASING.md) · **简体中文** · [🏠 首页](../README.zh-CN.md)

## 📋 0.7.1-fix 验证范围

**2026-10-03 · Asia/Taipei：**显示与下载版本为 **0.7.1-fix**，MSI 为 **0.7.1**，MSIX 为 **0.7.1.0**，标签为 **v0.7.1**。本次进行编译、打包与静态验包，不执行自动回归脚本；GUI 交互、安装、升级与回滚、MSIX 证书信任安装均待人工验证。只记录实际取得的构建、签名和包检查结果，不沿用历史测试数量；下文命令保留为通常的开发与发行流程

## 🧰 工具

使用[开发工具链](DEVELOPMENT.zh-CN.md)、PowerShell 7、**WiX 7.0.0** 和 Windows SDK 的 **MakeAppx / SignTool**；这些是打包工具，不是用户运行依赖。请安装同版本 WiX UI 扩展

```powershell
wix extension add -g WixToolset.UI.wixext/7.0.0
```

WiX 7 要求接受 [OSMF 条款](https://docs.firegiant.com/wix/osmf/)，请先核对适用资格和义务，仓库脚本不会代为接受 EULA

## 🔏 签名

创建本地预览证书

```powershell
$thumbprint = .\scripts\New-PreviewCertificate.ps1
```

脚本在 `CurrentUser\My` 中创建或复用代码签名证书，私钥不可导出，不导入受信任根证书，也不导出 PFX。请保留签名账号和密钥供后续更新使用；即使主题文字相同，开发者新生成的证书也不等于正式发行使用的证书

发行脚本签署应用 EXE、MSI 和 MSIX，仅导出公开 `.cer`。自签包（包括 0.6.0）的 MSIX 用户需要手动信任证书，详见 [安装说明](INSTALL.zh-CN.md)。公开受信任的正式签名仍属后续工作，当前包不带时间戳。正式 Release 状态不等于证书公开受信任，`PyDeck-preview.cer` 沿用历史文件名与签名身份

原生 `PyDeck.Launcher.exe` 也会签名，并将相同签名文件复制为 `PyDeck-Dependencies-<版本>-win-x64.exe`。文件名决定独立检查模式，该模式不启动相邻应用，发行时请一并上传。MSI 允许在缺少 GUI 运行时时安装；其快捷方式与 MSIX 激活均使用已安装的启动器，MSIX 框架依赖仍然必须满足。用户依赖与恢复步骤统一见 [INSTALL.zh-CN.md](INSTALL.zh-CN.md)

## 📦 构建

可选的 `InformationalVersion` 指定应用显示版本及下载文件名，例如 `0.7.1-fix`，必须以数字 `Version` 开头。安装器内部版本和正式 Git 标签仍使用数字，构建记录同时保存两者。仅修改显示名称不构成安装升级，后续发行必须递增 `Version`

先审查并提交修改。包版本取自 `PimGui.App.csproj`，要求三个数字段；MSI 使用该版本，MSIX 追加第四段零。每次升级都应提高安装器版本，包括后续预览版

```powershell
.\scripts\Build-Release.ps1 -CertificateThumbprint $thumbprint
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release
```

`Build-Release.ps1` 按锁定依赖还原、运行核心与原生检查、发布不带调试符号或内置共享运行时的应用、生成具有稳定组件身份的每用户 MSI、验证并创建 MSIX，最后签名。原生启动器与 MSI 操作 DLL 静态链接 C++ 基础库。中间文件与日志保留在已忽略的 `artifacts/`，仅 `assets/` 用于公开上传

📦 **从 0.7.1-fix 起：**脚本还会生成并签署 `PyDeck-Setup-<版本>-win-x64.exe`，其中包含已签名的 MSI 和未修改的微软运行时安装程序。这是新增的离线安装入口，独立 MSI 和 MSIX 继续保留，应用仍使用共享运行时，历史发行附件不变

`packaging/setup/prerequisites.json` 固定官方下载地址、长度及 SHA-256。`Build-Setup.ps1` 将缺少的文件下载到已忽略的 `.local/setup-prerequisites`，核对哈希与微软签名后再嵌入；下载内容变更会阻止构建，必须核实新官方版本及签名后才能更新记录。不要提交安装器缓存。可针对同版本的现有 MSI 单独构建和验证

```powershell
.\scripts\Build-Setup.ps1 -MsiPath <已签名MSI> -OutputDirectory .\artifacts\setup-test -Checks
.\scripts\Test-Setup.ps1 -SetupPath <Setup文件> -MsiPath <已签名MSI>
```

单独运行 `Build-Setup.ps1` 生成的是供本地验证使用的未签名外层 EXE，发行签名由 `Build-Release.ps1` 完成；`-AllowUnsignedMsi` 仅供本地测试包使用。原生 Setup 静态链接基础库，与启动器共用依赖检测，在 MSI 事务开始前顺序补装依赖，再打开原有目录和快捷方式向导

`-AllowUnsigned` 用于本地打包试验，生成的 MSIX 不能宣称可直接安装。`-SkipChecks` 只编译和打包，不运行核心与原生测试；使用时应在发行说明中注明跳过的验证，构建成功不代表回归或实装测试通过。脚本不会自动安装依赖或更改证书信任

## 🧪 验证

`Test-Release.ps1` 检查包身份、外部运行时、私密 / 调试文件排除、MSIX 块映射和密码学签名、签名者、MSI 品牌名称、目录浏览事件、可选快捷方式、版本、每用户范围及升级身份。它只检查包内容，不安装产品或操作 GUI。除非测试者手动信任，自签证书链仍不受系统信任

包含 Setup 的构建还会验证其签名，并调用 `Test-Setup.ps1`，提取四个内嵌安装器、核对固定哈希与独立 MSI、保留报告，全程不执行安装器。原生检查覆盖依赖组合、安装后复查、取消、重启结果、篡改拒绝、文件锁定和四语言控件。发行前还需在一次性环境实测缺失依赖补装、UAC 取消、标准用户注册、失败重试、重启续装、断网安装、MSI 升级，以及仅准备 MSIX 依赖；自动状态测试不能替代这些实装验收

🗂️ MSI 使用原生 `IFileOpenDialog` 选择文件夹，将安装目录和快捷方式选项保存在当前用户的安装器设置中。`Build-Launcher.ps1` 在收到 `-InstallerActionsDirectory` 参数时，才会额外编译 `/MT` 自定义操作 DLL 并检查其只导入系统 DLL；`Build-Release.ps1` 会传入该参数。该 DLL 嵌入 MSI，不作为应用依赖分发

```powershell
.\scripts\Test-MsiOptions.ps1 -ReleaseDirectory $release
```

这项手动启用的测试会安装并卸载具有独立产品、组件、注册表和快捷方式标识的 MSI 测试包，覆盖四种快捷方式组合、中文与空格目录、修复、升级保留选择及卸载保留无关文件，不替换已有 PyDeck 安装。发行前还应操作真实向导，验证浏览、选择、取消及前后导航；仅测试命令行安装不能证明界面交互正常

还应在专用环境验证 MSI 安装 → 启动 → 卸载、升级、降级阻止和缺少依赖时的行为，并在主动信任预览证书的机器上测试签名 MSIX 安装

已安装的 MSI 应用可运行 `scripts/Smoke-Test.ps1 -BuildDirectory <目录>`。两种 GUI 冒烟检查均需要 PIM 和在线目录访问；对于已安装或开发注册的 MSIX，请使用 Windows PowerShell 运行 Appx 命令

```powershell
powershell.exe -NoProfile -File .\scripts\Smoke-Packaged.ps1
```

该脚本通过真实 MSIX 身份激活应用，并使用独立设置执行已有的只读 GUI 检查。开发注册能验证包激活，不能证明正式证书信任安装路径已通过。测试时不要覆盖现有安装，只清理本次创建的测试安装或注册

实际结果和剩余边界应写入 [FEATURES.zh-CN.md](FEATURES.zh-CN.md) 及英语版；本页是发行操作流程，不表示其中每项验收都已通过

## 🗜️ 源码与发布

```powershell
.\scripts\Export-Source.ps1 -ReleaseDirectory $release
```

脚本保留历史名称，现在仅准备双语安装说明、只读依赖检查脚本和上传附件的 SHA-256 校验值，要求 Git 工作区干净且提交与安装包构建记录一致，发现 `assets/` 中残留手动生成的源码包时会拒绝继续。源码使用 GitHub 自动提供的 **Source code (zip)** / **Source code (tar.gz)** 链接，不上传重复归档，也不将自动源码下载列入 `SHA256SUMS.txt`

从 **0.6.0** 起，在 GitHub 创建普通正式 Release，`v<版本>` 标签固定到同一提交，并设为 Latest。历史 0.4.0 / 0.5.1 的发行标签使用 `-beta` 后缀，也改为普通 Release，但不设为 Latest；原标签可作为兼容引用保留，仍指向未变更的提交。仅上传 `assets/` 中经过检查的文件，核对远端哈希，并说明已验证范围与剩余限制。不要上传 `build.json`、`work/`、日志、截图、PFX、缓存或整个 `artifacts/`

📝 仅修正文档时，可提交到 `main`，无需重编未变更的应用或提高应用版本。已发布标签与签名安装包保持不变，发行说明链接到修正后的仓库指南；随附指南和标签源码保留原发行提交的快照。如明确删除过时附件，应同步从校验清单移除对应条目，保留附件的哈希不变

## 🔁 稳定身份

| 项目 | 值 |
| --- | --- |
| MSI UpgradeCode | `D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA` |
| MSI 安装范围 | 当前用户 |
| MSIX 包名 | `DM10cn.PyDeck` |
| MSIX 发布者 | `CN=DM10cn` |
| MSIX 应用 ID | `App` |

MSIX 为桌面应用禁用文件系统 / 注册表虚拟化，使 PIM 配置、解释器文件和跨实例锁能与非打包工具共享。两种独立包均不内置 .NET、Windows App Runtime 或 PIM；新增的 Setup 内含共享运行时安装器，应用并未改为自包含。MSI 与 MSIX 是独立安装渠道，不自动跨格式迁移

## 🔁 从 0.6.1 起的 MSI 覆盖升级

每版提供完整 MSI，不生成 MSP 或二进制差分包。运行新版 MSI，在同一次安装事务中替换旧版，无需用户先卸载。保留 UpgradeCode、每用户范围和稳定组件标识，在 InstallInitialize 后移除旧产品，失败时可回滚旧版；每次发行提高三段版本号

进行自动化 MSI 验收时，运行 `Test-MsiOptions.ps1`，由独立测试产品验证目录和快捷方式保留、旧版专属文件清理、无关用户文件保留，以及主动制造失败后的回滚。另用隔离安装验证上一正式 MSI → 新 MSI，未执行时明确标为待验收。应用设置和虚拟环境记录位于安装包之外，此规则仅适用于 MSI，MSIX 继续由 Windows 管理；完整包覆盖升级不宣称缩小下载量

使用 `Test-MsiUpgrade.ps1 -ReleaseDirectory <新版> -PreviousReleaseDirectory <旧版>` 核对上一公开 MSI 覆盖升级后的文件哈希、应用数据与安装选项。脚本拒绝覆盖已有的 PyDeck 安装。一次性环境没有 WinUI 依赖时可加 `-SkipGui` 仅验收安装器，GUI 烟雾检查需单独执行，并注明这一边界
