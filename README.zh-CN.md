<p align="center">
  <img src="src/PimGui.App/Assets/AppIcon.png" width="112" alt="PyDeck 图标">
</p>

# 🐍 PyDeck

**在 Windows 上，为你的 Python 版本安个家**

[English](README.md) · **简体中文**

PyDeck 是 **Python Install Manager** 的原生 **WinUI 3** 图形界面，支持查看解释器、安装版本、设置默认版本和管理离线包，并提供 **Windows Fluent** 与 **Material 3 Expressive** 两种界面风格

🚧 **开发预览版 · 0.4.0** · 🪟 **Windows 11 x64** · 📄 **MIT**

## 📦 下载

从 [GitHub Releases](https://github.com/DM10cn/PyDeck/releases) 获取 **MSI**、**MSIX** 和 **ZIP / tar.gz 源码**。安装前请阅读 [安装说明](docs/INSTALL.zh-CN.md)：运行时仍需单独安装，自签 MSIX 预览版需要手动信任公开证书

## ✨ 可以做什么

- 🐍 **查看 Python 安装** — 版本、发行方、架构、可执行文件路径和实际默认版本
- 📥 **查找并安装 Python** — 稳定版推荐、搜索、架构筛选和可展开的特殊发行包
- 📦 **离线使用** — 在联网电脑下载 PIM 离线包，复制到另一台电脑安装
- ⏳ **查看操作进度** — 持续显示阶段进度，支持取消安装、更新和离线下载
- 🛠️ **管理解释器** — 更新、卸载、设为默认、打开终端或文件夹、复制路径
- 🎨 **调整外观** — Fluent / Material 3 Expressive、跟随系统 / 浅色 / 深色，以及 Fluent 专属的 Mica / Acrylic
- 🌏 **切换语言** — 默认英语，另有简体中文、繁体中文（台湾）和日语

详细实现情况与待验收项目见 [功能状态与计划](docs/FEATURES.zh-CN.md)

## 📋 使用前准备

| 依赖 | 要求 |
| --- | --- |
| 操作系统 | Windows 11，x64 |
| .NET | .NET Runtime 10，x64 |
| Windows App Runtime | 2.5.1，x64，与项目引用保持一致 |
| Python 管理器 | [Python Install Manager](https://docs.python.org/3/using/windows.html) |

**.NET 和 Windows App Runtime 需要单独安装**，PyDeck 不内置或自动下载这两个运行时。.NET Desktop Runtime 或 SDK 也包含本应用需要的基础 .NET 运行时

应用会自动检测 Python Install Manager，检测失败时可在设置中选择它的可执行文件。Windows 10 支持暂缓

## 🚀 构建与运行

使用 **Visual Studio 2026**，安装 **WinUI 应用程序开发**、**.NET 10 SDK** 和 **Windows SDK 26100**。SDK 基线见 `global.json`，NuGet 依赖包含锁定文件

```powershell
git clone https://github.com/DM10cn/PyDeck.git
cd PyDeck
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Run.ps1
```

也可以在 Visual Studio 中打开 `PimGui.slnx`，选择 `PimGui.App`，使用 **x64** 构建。内部项目名称保留 `PimGui`，应用和可执行文件名为 **PyDeck**

开发输出位于 `artifacts/`，是**依赖外部运行时的非打包应用**，使用时请保留输出目录中的所有文件。MSI 与 MSIX 的构建方式见 [发行流程](docs/RELEASING.zh-CN.md)

🧑‍💻 [开发指南](docs/DEVELOPMENT.zh-CN.md) · 🗺️ [功能状态](docs/FEATURES.zh-CN.md) · 🔐 [安全说明](docs/SECURITY.zh-CN.md)

## 📦 离线安装 Python

1. 在联网电脑打开 **安装 Python → 在线**，从版本的 **⋯ → 下载离线包** 操作创建离线包
2. 将生成的整个文件夹复制到离线电脑，包括 `index.json` 和 ZIP 文件
3. 打开 **安装 Python → 离线 → 选择文件夹**，选择离线包目录并安装

离线电脑需要预先安装 Python Install Manager 和两个运行时依赖，也可以使用 `pymanager install --download=<folder> <tag>` 生成的离线包

PyDeck 接受包含 SHA-256 校验值的独立本地离线包，并在安装前创建经过验证的副本。校验值只能验证完整性，不能证明发布者身份，请使用可信来源；缺包或文件损坏时会停止操作。参见 [Python 离线安装指南](https://docs.python.org/3/using/windows.html#offline-installs)

## ⏳ 进度与取消

操作面板在切换页面后仍然可见。下载和解压百分比来自 PIM 输出，表示**当前阶段的大致进度**，其他阶段使用不定进度条

安装、更新和离线下载支持停止。停止安装需要确认，且**可能留下部分文件**。PyDeck 会等待当前进程退出并刷新安装列表，不承诺自动回滚；卸载不支持中途取消

## 🎨 外观与语言

Fluent 提供 **透明效果：使用 Windows 设置 / 开 / 关**，并可单独选择 **Mica / Acrylic**。Windows 的辅助功能、电源或硬件策略仍可能使背景回退为实色。Material 3 Expressive 使用实色表面

语言切换即时生效并保存。文档维护**英语和简体中文**两个版本，应用另支持繁体中文（台湾）和日语。PIM 原始输出及版本标识保持原文

## 🧪 验证状态

仓库包含核心回归检查和可选的应用内 GUI 冒烟测试，也已验证真实 PIM 的只读查询、独立目录中的离线解压，以及测试下载的取消流程

仍需在专用测试环境验收完整安装、更新、卸载和默认版本切换，并补充外部键盘与读屏测试、原生背景视觉检查和干净机器部署测试。命令与验证边界见 [开发与测试](docs/DEVELOPMENT.zh-CN.md)

## 🤝 参与贡献

欢迎提交 Issue 和 Pull Request，请附上应用版本、Windows 版本、复现步骤和预期行为。分享日志前，请移除个人路径、账号信息、凭据和私有软件源

公开文档仅维护英语和简体中文，英语为默认页面；行为变化时请同步更新两个版本。🔐 请勿在公开 Issue 中发布密钥或敏感漏洞细节，参见 [安全说明](docs/SECURITY.zh-CN.md)

## 📄 协议与致谢

PyDeck 使用 [MIT 协议](LICENSE)，第三方依赖保留各自的许可证

PyDeck 是独立项目，与 Python Software Foundation 或 Microsoft 不存在隶属或背书关系。Python 名称及标志受 PSF 商标政策约束，MIT 协议不授予第三方商标权利，详见 [第三方声明](docs/THIRD-PARTY-NOTICES.zh-CN.md)
