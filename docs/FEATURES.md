# 🗺️ Feature status

**English** · [简体中文](FEATURES.zh-CN.md) · [🏠 Home](../README.md)

**Development preview 0.5.1** — this page is the reference for feature and acceptance status. “Implemented” means the code and UI exist; completed checks are recorded below. A feature can be implemented while its full acceptance testing remains outstanding. Ideas under consideration are not commitments.

## ✅ Implemented

| Area | Current behavior |
| --- | --- |
| 🔌 PIM connection | Automatic discovery, file-picker replacement, validation before saving, and reconnect |
| 🧰 Prerequisite recovery | Native startup window, official download links and recheck; standalone checker does not launch the app; GUI can open without PIM and reconnect after its manual installation |
| 🐍 Installed versions | Effective default, version, publisher, architecture, full executable path, search, and filters |
| ⭐ Recommendations | Latest standard stable version, expandable older versions and specialized distributions |
| 📥 Runtime actions | Exact-ID install, update, uninstall, default selection, terminal, folder, and path copy |
| 📦 Offline installation | Local `index.json` and ZIP bundle, required SHA-256, verified staging copy, and local sources |
| 💾 Offline download | New bundle subfolder without overwriting existing bundles |
| ⏳ Progress | Persistent operation panel; approximate download/extraction stage percentages; indeterminate unknown phases |
| 🛑 Cancellation | Installation, updates, offline installation, and offline download; confirmation for install changes; partial files may remain |
| ⚙️ Python preferences | Default architecture, preview visibility, specialized-package expansion, and uninstall confirmation enabled by default |
| 🎨 Design and themes | Fluent / Material 3 Expressive semantic tokens, System / Light / Dark themes, bounded content width |
| 🪟 Fluent materials | Mica / Acrylic; Use Windows setting / On / Off; translucent content layers; opaque fallback; controller preserved across unrelated changes |
| 🌏 Languages | Main GUI: English default plus Simplified Chinese, Traditional Chinese (Taiwan), Japanese, with immediate switching and persistence; helper: same four choices, English on each launch; MSI wizard: English |
| 🖼️ App identity | PyDeck icon in executable, window, title bar, and sidebar; About and dependency information |
| 📋 Activity | Bounded session output, errors, exit codes, and explicit copy action |
| 🔐 Safeguards | Argument validation, ownership and stale-state checks, cross-instance operation lock, bounded output, and atomic configuration writes |
| 📦 Preview packaging | Per-user MSI, MSIX, local certificate signing, separate runtime prerequisites, and stable upgrade identities |
| 🗂️ MSI installation options | Editable folder, native folder browser, optional desktop / Start menu shortcuts, and choices preserved through repair and upgrades |
| 🗜️ Source release archives | ZIP / tar.gz from the matching clean commit, bilingual install guide, and SHA-256 checksums |

Only PIM-managed Python runtimes can be updated or removed. Python uninstallation inside the app does not expose mid-operation cancellation. See [Security](../SECURITY.md) for trust boundaries and [installation](INSTALL.md) for dependency and package requirements.

## 🛠️ Planned / awaiting acceptance

| Item | Scope |
| --- | --- |
| 🧪 Full Python runtime lifecycle acceptance | End-to-end Python install, update, uninstall, and effective-default changes in a disposable environment; the corresponding features already exist |
| ♿ Desktop acceptance | External keyboard and screen-reader testing, DPI behavior, and native backdrop appearance |
| 🔏 Production signing | Publicly trusted signing and clean-machine verification of the certificate-trust installation path |
| 🧼 Clean-machine deployment | Verify external runtime prerequisites and startup on a clean Windows 11 installation |

## 💭 Under consideration

| Idea | Notes |
| --- | --- |
| 📊 Transfer details and history | Speed / remaining time only if reliable data is available from PIM |
| 🌐 Custom sources and proxy settings | Needs a clear relationship with existing PIM configuration |
| 🧰 Virtual environments and pip | Outside the current interpreter-management scope |
| 💻 ARM64 application builds | Catalog architecture filtering does not mean the GUI has a native ARM64 build |

⏸️ **Windows 10 support is deferred**. Windows 7, 8, and 8.1 are outside the target scope.

## 🔬 Verification boundary

The following records local checks for the **0.5.1 release**. See [development](DEVELOPMENT.md) and [releasing](RELEASING.md) for commands.

| Check | Completed | Remaining boundary |
| --- | --- | --- |
| 🧪 Core regressions | 39 checks passed | Not an independent security audit |
| 🧰 Native prerequisite tools | Version / architecture checks, missing-state combinations, four-language controls, standalone gating, live recheck, and system-only imports | Simulated missing states do not replace a clean-machine trial |
| 🗂️ MSI wizard | Final package's folder browser, selection, cancellation, path refresh, and Next / Back navigation | Not a full accessibility or all-DPI acceptance test |
| 🔁 MSI options and lifecycle | Isolated test products exercised all four shortcut combinations, Unicode / spaced folders, repair, upgrade retention, and preservation of unrelated files on uninstall | This validates PyDeck setup, not Python interpreter operations |
| 📦 Final MSI | Installation, 53 installed-file hash comparisons, 17 GUI check groups, and uninstall cleanup | Local Windows environment only |
| 🪟 Final MSIX | Signature and block-map checks, development registration, real package activation, 17 GUI check groups, and cleanup | Production certificate-trust installation still untested |
| 🐍 PIM integration | Read-only live queries, isolated offline extraction, and test-download cancellation exercised during development | Full managed-Python lifecycle remains pending |
| 🎨 Appearance | GUI state / render checks, controller retention, and wide / compact layouts | Native compositor appearance, external keyboard / screen-reader use, and all DPI settings remain pending |

Existing daily-use Python environments were not used for destructive acceptance tests. No published result claims that clean-machine deployment or full Python lifecycle acceptance has passed.
