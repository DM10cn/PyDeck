# 🚀 发行流程

[English](RELEASING.md) · **简体中文** · [🏠 首页](../README.zh-CN.md)

## 🧰 工具

使用 [开发工具链](DEVELOPMENT.zh-CN.md)、PowerShell 7、**WiX 7.0.0** 和 Windows SDK 的 **MakeAppx / SignTool**，并安装同版本 WiX 扩展

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

发行脚本签署应用 EXE、MSI 和 MSIX，仅导出公开 `.cer`。自签预览版的 MSIX 用户需要手动信任证书，详见 [安装说明](INSTALL.zh-CN.md)。公开受信任的正式签名仍属后续工作，当前预览包不带时间戳

## 📦 构建

原生 `PyDeck.Launcher.exe` 也会签名，并将相同签名文件复制为 `PyDeck-Dependencies-<版本>-win-x64.exe`，供 MSIX 安装前使用，发行时请一并上传。MSI 不再因缺少 .NET 阻止安装，快捷方式与 MSIX 激活入口均使用原生启动器；MSIX 的框架依赖声明仍然保留

先审查并提交修改。包版本取自 `PimGui.App.csproj`，要求三个数字段；MSI 使用该版本，MSIX 追加第四段零。每次升级都应提高安装器版本，包括后续预览版

```powershell
.\scripts\Build-Release.ps1 -CertificateThumbprint $thumbprint
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release
```

`Build-Release.ps1` 按锁定依赖还原、运行核心检查、发布不带调试符号或内置运行时的应用、生成具有稳定组件身份的每用户 MSI、验证并创建 MSIX，最后签名。中间文件与日志保留在已忽略的 `artifacts/`，仅 `assets/` 用于公开上传

`-AllowUnsigned` 用于本地打包试验，生成的 MSIX 不能宣称可直接安装。`-SkipChecks` 仅用于核心检查已通过后的打包迭代，不用于最终发行构建。脚本不会自动安装依赖或更改证书信任

## 🧪 验证

`Test-Release.ps1` 检查包身份、外部运行时、私密 / 调试文件排除、MSIX 块映射和密码学签名、签名者、MSI 版本、每用户范围及升级身份。除非测试者手动信任，自签证书链仍不受系统信任

🗂️ MSI 使用原生 `IFileOpenDialog` 选择文件夹，将安装目录和快捷方式选项保存在当前用户的安装器设置中。`Build-Launcher.ps1` 同时编译 `/MT` 自定义操作 DLL 并检查其只导入系统 DLL；该文件嵌入 MSI，不作为应用依赖分发

```powershell
.\scripts\Test-MsiOptions.ps1 -ReleaseDirectory $release
```

这项手动启用的测试会安装并卸载具有独立产品、组件、注册表和快捷方式标识的 MSI 测试包，覆盖四种快捷方式组合、中文与空格目录、修复、升级保留选择及卸载保留无关文件，不替换已有 PyDeck 安装。发行前还应操作真实向导，验证浏览、选择、取消及前后导航；仅测试命令行安装不能证明界面交互正常

还应在专用环境验证 MSI 安装 → 启动 → 卸载、升级、降级阻止和缺少依赖时的行为，并在主动信任预览证书的机器上测试签名 MSIX 安装

已安装的 MSI 应用可将目录传给 `scripts/Smoke-Test.ps1`，已安装或开发注册的 MSIX 可运行

```powershell
.\scripts\Smoke-Packaged.ps1
```

该脚本通过真实 MSIX 身份激活应用，并使用独立设置执行已有的只读 GUI 检查。开发注册能验证包激活，不能证明正式证书信任安装路径已通过。测试时不要覆盖现有安装，只清理本次创建的测试安装或注册

## 🗜️ 源码与发布

```powershell
.\scripts\Export-Source.ps1 -ReleaseDirectory $release
```

导出要求 Git 工作区干净，且提交与安装包构建记录一致。ZIP 与 tar.gz 使用 `git archive`，不包含被忽略的文件或签名密钥；脚本还附上双语安装说明、只读依赖检查脚本和所有发行附件的 SHA-256 校验值

在 GitHub 创建 **pre-release**，版本标签固定到同一提交。仅上传 `assets/` 中经过检查的文件，核对远端哈希，并说明已验证范围与剩余限制。不要上传 `build.json`、`work/`、日志、截图、PFX、缓存或整个 `artifacts/`

## 🔁 稳定身份

| 项目 | 值 |
| --- | --- |
| MSI UpgradeCode | `D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA` |
| MSI 安装范围 | 当前用户 |
| MSIX 包名 | `DM10cn.PyDeck` |
| MSIX 发布者 | `CN=DM10cn` |
| MSIX 应用 ID | `App` |

MSIX 为桌面应用禁用文件系统 / 注册表虚拟化，使 PIM 配置、解释器文件和跨实例锁能与非打包工具共享。两个格式均不内置 .NET、Windows App Runtime 或 PIM，MSI 与 MSIX 是独立安装渠道，不自动跨格式迁移
