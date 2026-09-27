# 🧰 PyDeck 0.7.0 management

English · [简体中文](#简体中文)

📦 Available in 0.7.0-fix

## 🔀 Independent tasks

Catalog refresh, app update checks, building and venv package management have separate task ownership. Navigation, appearance, language and list filters remain available while unrelated tasks run. Conflicting runtime changes, environment changes and cleanup stay mutually exclusive. Network/source saves wait for their consumers to finish. Multiple long tasks have separate progress and cancellation, selectable in the progress panel.

Background completion updates the affected page or settings section; unrelated settings drafts remain in place. Failures and cancellation release only the owning task's resources. Registered environments still share one package/registry operation lock; this does not promise simultaneous pip edits in multiple environments or coordination with arbitrary external terminals.

## 🛠️ Build preparation

**Build Python → Prepare build tools** downloads the official `python` 3.14.7 x64 NuGet package into PyDeck's private build area. The package must match a pinned SHA-256; reused files are compared with the verified archive before execution. No PATH, PIM registration or system Python settings are changed. Network preferences and cancellation apply. The package digest was recorded from the official NuGet endpoint over HTTPS; this is not NuGet signature verification.

The helper discovers installed MSVC/SDK combinations. **Get C++ tools** opens Microsoft's official installer page. Select **Desktop development with C++**, x64 compiler tools and a Windows SDK in Visual Studio Installer, then recheck. PyDeck does not silently install Visual Studio or accept its license. A chosen target CPython still needs compatible source scripts and a compatible compiler.

## 🧹 Build storage

**Settings → Build storage → Scan storage** reports source/tool downloads, private build tools, build work directories and marked local runtimes. Each entry has an explicit path and size. Cleaning requires review, checks for changed files and refuses links and active build locks. History, logs, original imported archives, PIM runtimes and project directories are outside cleanup scope.

Runtime cleanup requires the build ownership marker and blocks registered venv dependencies. Removal changes runtime inventory before deleting files so interruption cannot leave a partially removed runtime marked ready. A failed or interrupted cleanup may leave files; scan again. Source and tool caches may be downloaded again after cleanup.

## 🔗 Runtime usage

**My Python → … → Runtime usage** shows registered environments matched by runtime identity, saved base path or current `pyvenv.cfg`. Missing environments remain visible conservatively. This is not a whole-disk project search. Unregistered environments, running programs and external tools may still depend on a runtime. PIM uninstall shows known dependencies even when the usual uninstall confirmation preference is off, and rechecks the usage snapshot before mutation.

## 📦 Virtual-environment packages

**Virtual environments → Packages** opens an inline panel for the chosen environment. Opening it executes that environment's interpreter after confirmation. It supports package search, installation, updates, uninstall, pip preparation with ensurepip, pip upgrades and requirements import/export. The UI does not modify packages in global Python installations and does not offer pip uninstall.

Every invocation verifies the venv prefix inside the executing process. pip is loaded from that venv, inherited pip options/configuration are suppressed, and package changes reject linked environment trees. Downloads use PyPI and the app's network policy. Package installation can execute third-party build code; review the target environment and package list before proceeding.

Requirements accept one package name (optional extras) and version constraints per line, blank lines and comments. Includes, options, URLs, local paths and environment markers are rejected explicitly. Export records installed names and versions, not a platform-independent lock file or original editable/source provenance. Refresh before exporting externally modified environments.

Changes have a 30-minute limit and cancellation closes their process group. pip is not transactional: cancellation or failure may leave changes. The package list is refreshed after each attempt; `pip check` problems appear as a warning. System-site packages inherited by a venv are not listed as locally installed packages. A missing base interpreter must be repaired before managing its environment.

## 简体中文

📦 0.7.0-fix 已包含以下功能

### 🔀 任务隔离

目录刷新、应用更新检查、构建和虚拟环境包管理分别持有任务状态，无关任务运行时仍可导航、切换外观和语言、筛选列表。会相互影响的运行时修改、环境修改和空间清理继续互斥，网络与安装源的保存需等使用它们的任务结束。多个长任务可在进度区切换查看，分别取消

后台完成只更新相关页面或设置区域，保留其他设置草稿；失败和取消只释放当前任务占用的资源。已登记环境仍共用包操作与登记锁，不承诺同时修改多个环境的 pip，也无法约束任意外部终端操作

### 🛠️ 构建准备

在“构建 Python”点击“一键准备”，从官方 NuGet 来源下载独立的 Python 3.14.7 x64，并验证固定 SHA-256；复用前将文件与已验证归档逐一比对。不修改 PATH、PIM 登记或系统 Python，支持代理与取消。摘要来自官方 HTTPS 下载，不宣称验证了 NuGet 签名

准备后检测 MSVC / SDK。“安装 C++ 工具”打开微软官方页面，在 Visual Studio Installer 选择“使用 C++ 的桌面开发”、x64 编译器和 Windows SDK，再重新检查。不会静默安装 Visual Studio 或替用户接受许可；目标源码仍须与工具链兼容

### 🧹 空间管理

设置中的“构建空间管理”扫描源码与工具下载、独立构建工具、构建临时目录以及带归属标记的本地运行时，逐项显示路径和大小。清理前确认范围，执行前重新检查文件快照，拒绝目录链接和进行中的构建

保留历史、日志、原始导入归档、PIM 运行时和项目目录。已登记环境仍在使用的运行时不能清理。删除前更新运行时登记，防止部分删除后仍显示就绪；失败或中断可能残留文件，可重新扫描。已清理的下载缓存以后可能需要重新下载

### 🔗 使用关系

在“我的 Python”的更多菜单查看使用关系，通过运行时 ID、已保存的基础路径和当前 pyvenv.cfg 匹配已登记环境；缺失环境仍保留提示。这不是全盘项目扫描，未登记环境、运行中的程序和外部工具也可能仍在使用

PIM 卸载时若存在已知依赖，即使关闭普通卸载确认也会显示影响，操作前重新核对使用关系

### 📦 虚拟环境包管理

在虚拟环境卡片点击“包管理”，页内展开包搜索、安装、更新、卸载、pip 补装与升级、requirements 导入导出。打开前说明会运行该环境的解释器；不修改全局 Python 的包，也不提供 pip 卸载

在实际执行进程中核对 venv 身份，仅使用该环境的 pip，屏蔽可能改变安装目标的外部 pip 配置，修改前拒绝环境目录中的链接。使用 PyPI 和应用网络设置，安装第三方包可能执行其构建代码，操作前确认环境与包列表

requirements 支持每行包名、可选 extras 和版本范围，支持空行与注释；明确拒绝递归引用、命令选项、网址、本地路径和环境标记。导出记录本环境已安装的名称与版本，不是跨平台锁文件，也不保留 editable 等原始来源；外部修改环境后先刷新

包操作最长 30 分钟，可取消整个子进程组。pip 不提供事务回滚，失败或取消后可能有部分变更，因此每次操作后重新读取包列表，用 pip check 提示依赖问题。继承的系统包不作为本环境已安装包展示，基础解释器缺失时需先修复环境
