# 🛠️ Build Python

English · [简体中文](#简体中文)

📦 Available in 0.6.3 · x64 only

## 🚀 Configuration

PyDeck orchestrates CPython's own [PCbuild](https://github.com/python/cpython/tree/main/PCbuild) and [PC/layout](https://github.com/python/cpython/tree/main/PC/layout) on x64 Windows. It does not publish a separate Python distribution.

- 🔢 Refresh official source versions, including historical micro releases, or enter an exact release/preview version
- 📂 Import `Python-<version>.tgz` / `.tar.gz`; the archive is copied into the job before extraction
- 🎛️ Standard, Performance (Release + PGO), Debug, Minimal (no Tk or preinstalled pip), and Custom presets
- 🧩 Optional pip, SSL, SQLite, ctypes, Tcl/Tk + IDLE, tests, and symbols; enabling pip enables SSL
- 🧰 Choose a detected compiler/SDK combination and inspect the generated commands
- 📦 Choose an output parent; every build creates a unique runtime directory without overwriting another
- 🔁 Reuse any completed job's options, source and output choices from history

Release uses CPython's link-time optimization settings. Debug uses `python_d.exe` and requires the Visual C++ debug runtime. Standard library, ensurepip, venv, development headers and libraries remain included. Without pip, virtual environments use `--without-pip`. Disabling SQLite removes its native extension and DLL from the runtime; upstream may still compile them.

## 🔎 Compatibility and integrity

Selecting a version does not guarantee that modern tools can build every historical CPython. Before executing source scripts, PyDeck checks the selected version against `Include/patchlevel.h`, required PCbuild/layout files, and requested flags. Older sources lacking this layout require another adapter and stop with an explanation. Compiler errors remain in the job log. Tool discovery currently covers MSVC v141/v142/v143/v145 x64 and Windows SDKs; bootstrap Python must be 3.10+. v145 differs from CPython's official compiler.

Official archives only come from `https://www.python.org/ftp/python/`. Where Sigstore metadata exists, its SHA-256 is checked; 3.14.7 also has a pinned digest. PyDeck does **not** verify Sigstore signatures, identities or transparency proofs. A historical release with no metadata (HTTP 404) gets a recorded HTTPS-download fingerprint, distinguished from a published digest. Other metadata errors stop the job. Local archives get a fingerprint and require explicit trust before executing their scripts.

Extraction rejects traversal, links, special files, duplicates and oversized archives. Original imports remain untouched. CPython scripts acquire external dependencies, some as prebuilt binaries.

## ✅ Verification and management

PGO uses separately checked instrumented compilation, `-m test --pgo` training, and optimized compilation. ctypes is built for training even if excluded from the final runtime. For source paths containing spaces, the workload excludes `test_tabnanny`, whose path-quoting assertions fail in older CPython; this is recorded in the log and command preview. Other training failures stop the build. Before registration, PyDeck moves the source tree away, checks exact version/configuration, x64 architecture and selected modules, and creates/runs a separate virtual environment. Only validated runtimes enter **My Python**.

Local runtimes work with terminals and venv without PIM. Different builds coexist. PIM update, repair, uninstall, default selection and Shebang rules do not manage local builds. **Remove from list** retains files and existing venv dependencies.

## ⏹️ Logs and recovery

Download progress uses actual bytes. Compilation/testing show stages, not invented percentages. Cancel closes the process group; total timeout is four hours. Failed, cancelled and interrupted jobs retain logs/files. Reuse starts a new job, not partial resume.

Manifests and logs remain in `%LOCALAPPDATA%\PyDeck\Builds\Jobs`; default output is `Builds\Runtimes`. Provenance, digest, options and tool versions are recorded. Logs are bounded. Automatic cleanup, arbitrary shell flags, x86 and ARM64 builds are not provided.

## 简体中文

📦 0.6.3 新增 · 仅支持 x64

### 🚀 使用方法

进入 **构建 Python**，刷新官方源码版本列表或输入完整版本号，历史 micro 和预发布版均可指定；也可导入 `Python-版本号.tgz` / `.tar.gz`。选择预设、已有 Python 3.10+，检查构建工具后开始

- 🎛️ 标准、性能（Release + PGO）、调试、精简（不带 Tk 和预装 pip）、自定义预设
- 🧩 可选 pip、SSL、SQLite、ctypes、Tcl/Tk 与 IDLE、测试套件、调试符号；启用 pip 会启用 SSL
- 🧰 选择已检测到的编译器与 SDK 组合，查看实际执行命令
- 📦 自选输出位置，每次构建使用独立目录，不覆盖已有运行时
- 🔁 历史记录可复用版本、组件、源码位置和输出选项

Release 使用 CPython 自带的链接时优化；Debug 使用 `python_d.exe`，需要 Visual C++ 调试运行库。保留标准库、ensurepip、venv、开发头文件和库；关闭 pip 时不预装 pip，新建 venv 使用 `--without-pip`。关闭 SQLite 从最终运行时移除原生扩展与 DLL，上游仍可能编译它们

### 🔎 兼容性与校验

不再写死 3.14.7，但旧版不一定能用当前编译器构建。执行脚本前检查源码版本、PCbuild、打包脚本和所选功能，缺少适配时明确停止；编译器错误保留在日志。目前检测 MSVC v141/v142/v143/v145 x64 与 Windows SDK；v145 与 CPython 官方编译器不同

官方源码仅从 python.org 下载。有 Sigstore 元数据时比对 SHA-256，3.14.7 另有固定摘要；尚未验证 Sigstore 签名、身份与透明日志。旧版元数据不存在时记录 HTTPS 下载文件的指纹，与已发布摘要区分；其他元数据错误不会降级忽略。本地源码记录指纹，执行前确认来源可信

源码包先复制到独立目录，解包拒绝越界路径、链接、重复项和超限文件，不改动原文件。外部依赖由 CPython 脚本获取，部分为预编译库

### ✅ 构建与验收

PGO 分为插桩编译、训练、优化编译，逐步检查退出结果。训练会编译所需的 ctypes，最终产物仍按组件选择移除。源码路径含空格时，训练排除旧版中会因路径引号断言失败的 `test_tabnanny`，命令预览和日志均会注明；其他训练失败仍中止构建。注册前移走源码目录，核验最终解释器的版本、架构、构建类型、所选模块和 venv。通过后才进入“我的 Python”

无需 PIM 即可打开本地运行时终端、创建 venv，同版不同构建可以共存；不使用 PIM 默认版本、修复、卸载及 Shebang 规则。“从列表移除”保留文件

### ⏹️ 取消与记录

下载显示真实传输量，编译和测试显示阶段。取消终止本次进程组，总时限四小时。失败、停止、中断保留日志和文件，复用配置新建构建，不续编旧目录。记录源码摘要、配置与工具链，日志有上限

尚无自动清理、任意 shell 参数、x86/ARM64 构建
