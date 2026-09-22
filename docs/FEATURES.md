# 🗺️ Feature status

**English** · [简体中文](FEATURES.zh-CN.md) · [🏠 Home](../README.md)

**Development preview 0.4.0** — “Implemented” means the code and UI exist, with validation limits described separately. Planned items are future work; ideas under consideration are not commitments.

## ✅ Implemented

| Area | Current behavior |
| --- | --- |
| 🔌 PIM connection | Automatic discovery, file-picker replacement, validation before saving, and reconnect |
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
| 🌏 Languages | English default, Simplified Chinese, Traditional Chinese (Taiwan), Japanese; immediate switching and persistence |
| 🖼️ App identity | PyDeck icon in executable, window, title bar, and sidebar; About and dependency information |
| 📋 Activity | Bounded session output, errors, exit codes, and explicit copy action |
| 🔐 Safeguards | Argument validation, ownership and stale-state checks, cross-instance operation lock, bounded output, and atomic configuration writes |

Only PIM-managed runtimes can be updated or removed. Uninstallation does not expose mid-operation cancellation. See [Security](../SECURITY.md) for trust boundaries.

## 🛠️ Planned / awaiting acceptance

| Item | Scope |
| --- | --- |
| 🧪 Full runtime lifecycle | Install, update, uninstall, and effective-default changes in a disposable test environment |
| ♿ Desktop acceptance | External keyboard and screen-reader testing, DPI behavior, and native backdrop appearance |
| 📦 MSIX and MSI | Packaging, signing, upgrades, and prerequisite detection after product completion |
| 🗜️ Source release archives | Versioned ZIP / tar.gz artifacts and release notes after product completion |
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

Core regressions, in-app GUI state/render checks, read-only live PIM queries, isolated offline extraction, and test-download cancellation have been exercised. Existing daily-use Python environments were not used for destructive acceptance tests. Full lifecycle, native compositor visuals, external accessibility interaction, and clean-machine installation still need validation.
