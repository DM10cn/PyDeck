# 🛠️ CPython Build — first x64 slice

English · [简体中文](#简体中文)

> 📚 Historical first-slice design. The subsequent implementation expands source selection, Debug/PGO, components and output choices. [Build Python](../../BUILD_PYTHON.md) describes current behavior; the restricted recipe below records the initial milestone only.

## Agreed scope

PyDeck orchestrates a local CPython build; it does not publish a Python distribution. A separate **Build Python** page exposes the first supported recipe: CPython 3.14.7, x64, Release. Tcl/Tk, tests, and symbols are layout options. Standard library, SSL, SQLite, ctypes, development headers, ensurepip, pip, and venv remain included. Debug, PGO, other architectures, arbitrary flags, and imported source trees require separate validated recipes.

## Flow and boundaries

1. Detect a supported MSBuild/MSVC installation, Windows SDK, and an explicitly selected existing Python 3.10+ bootstrap interpreter. Show missing dependencies before a job starts.
2. Download the official release archive over HTTPS and check the SHA-256 pinned in the recipe against the digest published with the official release. This is pinned integrity checking, not an in-app Sigstore verifier.
3. Extract into a unique job directory, rejecting traversal, links, special files, excessive counts, and excessive expanded sizes. Build through the official PCbuild scripts with a controlled working directory and environment. Official external dependencies may include prebuilt libraries.
4. Assemble a separate runtime through CPython's PC/layout. Validate architecture, exact version, modules, pip, and venv outside the source tree. The final prefix must not depend on a source or staging directory.
5. Register only a successful runtime. Every build receives a unique ID, so configurations of the same version coexist. History records the source hash, toolchain, options, stage, timestamps, output, and log location. Failed/cancelled/interrupted jobs remain history and never become usable runtimes.

One job runs at a time under a dedicated build lease. It does not hold the PIM configuration lock. A job is cancellable; its process group is terminated and awaited before releasing the lease. Navigation remains available. Compilation has stage progress, not invented percentages. Complete bounded logs are stored on disk; failure keeps files for inspection and retry creates a new job.

Local runtimes merge with the PIM inventory and remain usable without PIM. Terminal and venv use the explicit interpreter path. PIM default, update, repair, uninstall, and Shebang operations must not receive a local runtime. Removing a local runtime from the list preserves its files and existing environments. Environment records retain the base runtime ID.

## Implementation and acceptance

- Add isolated core recipe, toolchain, extraction, process, job store, and orchestration modules
- Integrate the local inventory and venv provenance without changing PIM mutation semantics
- Add the Build page, shared progress/cancellation, dependency details, history, and four UI languages
- Exercise hostile archives, unsupported options, process cancellation, persistence, and inventory boundaries
- Build official CPython in an isolated project directory, move its work directory away, run the final interpreter and a new venv, then run application checks and GUI smoke checks

No release, MSI/MSIX build, user Python replacement, system PATH changes, or automatic toolchain installation belongs to this feature task.

## 简体中文

> 📚 这是首阶段的历史方案，后续已扩展源码选择、Debug/PGO、组件和输出设置。当前行为以[构建 Python](../../BUILD_PYTHON.md#简体中文)为准，下文固定版本范围仅记录初始里程碑

PyDeck 仅编排本机构建，不发行自己的 Python 分发版。独立的“构建 Python”页面先支持 CPython 3.14.7、x64、Release；可选 Tcl/Tk、测试套件及符号，保留标准库、SSL、SQLite、ctypes、开发头文件、ensurepip、pip 和 venv。

流程为依赖检查 → 官方源码下载和固定 SHA-256 核验 → 安全解包 → PCbuild 编译 → PC/layout 整理独立目录 → 架构、版本、模块、pip、venv 验证 → 注册到本地运行时列表。每次构建使用独立标识及目录，失败与取消只保留记录，不注册可用运行时。

构建使用独立锁，可查看日志、切换页面和取消，不占用 PIM 配置锁。本地运行时在 PIM 未连接时仍能打开终端、创建虚拟环境；不会误用 PIM 的默认解释器、更新、修复或卸载命令。“从列表移除”保留文件和现有虚拟环境。

验收包含真实源码编译、移走源码后的独立运行及 venv 创建、核心回归和 GUI 检查。本轮不发布或打包安装程序。Debug、PGO、自定义参数、导入源码和其他架构后续单独验证。
