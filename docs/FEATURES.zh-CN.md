# ✨ 功能说明

[English](FEATURES.md) · **简体中文** · [🏠 首页](../README.zh-CN.md)

PyDeck 将 Python 安装、虚拟环境和本地构建集中在一个 Windows 桌面工作区中。

| 类别 | 可以做什么 | 指南 |
| --- | --- | --- |
| 🐍 Python 版本 | 浏览当前与历史安装包，安装、更新、修复、切换默认版本和准备离线包 | [Python 管理](MANAGEMENT.zh-CN.md) |
| 📦 环境与软件包 | 创建或导入 venv、打开终端、管理 pip 包、导入导出受支持的 requirements | [Python 管理](MANAGEMENT.zh-CN.md) |
| 🛠️ 构建 Python | 选择官方或本地 CPython 源码、编译器、SDK 与组件，使用标准、性能、调试、精简或自定义预设 | [构建 Python](BUILD_PYTHON.md#简体中文) |
| 🧹 空间与使用关系 | 查看构建缓存、本地运行时和已登记环境依赖，确认后清理 | [Python 管理](MANAGEMENT.zh-CN.md) |
| ⚙️ 配置 | 调整 PIM 偏好、检查 PATH 与别名、配置 HTTPS 安装源、代理和 Shebang 规则 | [Python 管理](MANAGEMENT.zh-CN.md) |
| 🎨 外观 | 选择 Material 3 Expressive 或 Windows Fluent，调整动态颜色、启动、窗口行为和语言 | [设计系统](DESIGN_SYSTEMS.md), [Monet](MONET.md) |
| 📦 安装 | 使用离线 Setup 或当前用户 MSI，选择目录、快捷方式与初始风格 | [安装 PyDeck](INSTALL.zh-CN.md) |

界面提供英语、简体中文、繁体中文（台湾）和日语。独立任务分别显示进度并支持取消，活动页提供本次会话日志和级别筛选。

PyDeck 面向 Windows 11 x64。修改 Python 安装需要 PIM 26.3 或更高版本；本地构建及基于本地运行时的 venv 无需 PIM。包管理仅面向已登记的 venv，构建兼容性取决于所选 CPython 源码与工具链。具体操作限制见各项指南。
