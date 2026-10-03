<p align="center">
  <img src="src/PimGui.App/Assets/AppIcon.png" width="112" alt="PyDeck 图标">
</p>

# 🐍 PyDeck

**在 Windows 上，为你的 Python 版本安个家**

[English](README.md) · **简体中文**

PyDeck 是管理 Python 解释器、虚拟环境、软件包与源码构建的原生 **WinUI 3** 桌面应用，配合 **Python Install Manager** 使用，提供独立的 **Windows Fluent** 与 **Material 3 Expressive** 界面。

🪟 **Windows 11 x64** · 📦 **0.7.2** · 📄 **MIT**

## 📥 下载

前往 [最新发行版](https://github.com/DM10cn/PyDeck/releases/latest) 下载安装包。

| 安装包 | 用途 |
| --- | --- |
| `PyDeck-Setup-0.7.2-win-x64.exe` | 推荐：离线安装与维护，内含共享运行时安装程序 |
| `PyDeck-0.7.2-win-x64.msi` | 已准备好运行时的用户使用，安装到当前用户 |
| `PyDeck-Dependencies-0.7.2-win-x64.exe` | 查看依赖状态与官方下载入口 |

当前 EXE/MSI 安装包未签名。Setup 可选择安装目录、快捷方式与首次界面风格，并支持修复和卸载，详见[安装说明](docs/INSTALL.zh-CN.md)。

## ✨ 主要功能

- 🐍 **Python 版本管理** — 查看解释器、设置默认版本、更新或修复安装，打开终端与文件夹
- 🔎 **版本目录** — 选择精确的小版本、搜索分组列表，按架构、发行类型与预览状态筛选
- 🧰 **虚拟环境** — 创建或导入环境、查看运行时引用，打开已激活环境的终端
- 📦 **软件包管理** — 在环境内搜索、安装、升级和卸载软件包，管理 pip，导入与导出依赖清单
- 🛠️ **构建 Python** — 使用官方源码或本地归档，选择预设与组件，准备构建工具并管理构建空间
- 📴 **离线包** — 在联网电脑下载 Python 安装包，复制到另一台电脑安装
- ⏳ **任务与日志** — 查看独立任务进度、切换当前操作、取消支持的任务，筛选结构化日志
- ⚙️ **设置** — 配置安装源、代理与脚本解释器规则，管理 PIM 配置备份和路径、别名诊断
- 🎨 **外观** — Fluent 支持 Mica/Acrylic；Material 支持壁纸、手动和双色配色；提供浅色、深色与跟随系统
- 🌏 **语言** — 英语、简体中文、繁体中文（台湾）和日语

Material 使用原生 HCT 配色引擎与 SIMD 加速壁纸取色。主题、语言和手动配色即时生效，切换界面风格在重启后生效。壁纸图片仅在本机读取。

## 🚀 开始使用

1. 使用 Setup 安装 PyDeck，它会先准备缺失的共享运行时。
2. 连接 **Python Install Manager**；缺少时通过应用内的官方入口下载、安装，再重新连接。
3. 打开**安装 Python**选择版本，或从**虚拟环境**创建环境。

GUI 需要 **.NET Runtime 10 x64**、**Windows App Runtime** 和 **Visual C++ v14 x64**。Setup 内含其安装程序，独立 MSI 用户需另行准备。通过 PIM 管理解释器时需要 Python Install Manager，未安装它也可以打开 GUI；普通用户无需安装 SDK。

离线安装 Python 时，使用**安装 Python → 在线 → ⋯ → 下载离线包**，复制生成的完整文件夹，再在目标电脑的**安装 Python → 离线**中选择该文件夹。

📖 [安装说明](docs/INSTALL.zh-CN.md) · [Python 管理](docs/MANAGEMENT.zh-CN.md) · [构建 Python](docs/BUILD_PYTHON.md#简体中文)

## 🧑‍💻 从源码构建

使用 **Visual Studio 2026**，安装 WinUI 应用程序开发、**.NET 10 SDK**、**Windows SDK 26100** 和 **MSVC x64/x86 编译工具**。

```powershell
git clone https://github.com/DM10cn/PyDeck.git
cd PyDeck
.\scripts\Build.ps1 -Publish
.\scripts\Run.ps1
```

仓库提供 C#、C++ 与手写 `.asm` 源码。构建时生成原生取色引擎 `PyDeck.Colors.dll` 并随应用输出；GitHub 自动提供的源码 ZIP / tar.gz 包含汇编源文件。

详见[开发指南](docs/DEVELOPMENT.zh-CN.md)和[取色引擎源码说明](docs/MONET.md)。

## 🤝 参与贡献

欢迎提交 Issue 和 Pull Request，请附上复现步骤、应用与 Windows 版本，分享日志前移除私人信息。文档保持英语与简体中文同步；敏感问题请参阅[安全说明](docs/SECURITY.zh-CN.md)。

## 📄 协议

[MIT](LICENSE)。第三方依赖保留各自许可证，详见[第三方声明](docs/THIRD-PARTY-NOTICES.zh-CN.md)。PyDeck 与 Python Software Foundation、Microsoft 不存在隶属关系；Python 名称与标志受 PSF 商标政策约束。
