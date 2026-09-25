# 🗺️ Feature status

**English** · [简体中文](FEATURES.zh-CN.md) · [🏠 Home](../README.md)

**Current version: 0.6.2** — this page is the reference for feature and acceptance status. “Implemented” means the code and UI exist; completed checks are recorded below. A feature can be implemented while its full acceptance testing remains outstanding. Ideas under consideration are not commitments.

## ✨ New in 0.6.2

Version 0.6.2 adds the following behavior to the 0.6.1 baseline. Application checks and final package checks are recorded separately; historical release results below do not certify the new packages. See the [0.6.2 GitHub release details](https://github.com/DM10cn/PyDeck/releases/tag/v0.6.2) for packaging validation.

| Area | 0.6.2 behavior |
| --- | --- |
| 🕰️ Historical micro versions | Read every page of the selected PIM index, retain separate micro identities, and group them by minor series. For the official source, this means the Windows packages present in its index, not every Python source release |
| 🎯 Exact version operations | Resolve the selected micro through PIM before installation or offline download; repair requests the installed micro and checks the resulting interpreter instead of silently selecting the newest micro |
| 🔁 Replacement confirmation | Different micros sharing a PIM runtime ID replace one another. Online and offline installation review the existing version; a changed installation invalidates the confirmation |
| 🧭 Common page layout | Shared title, primary action, toolbar and content roles; the installed list carries the default marker without a duplicate hero; installation uses source, filters, recommendation and minor groups |
| 🔎 Installation filters | Architecture, package type and Show preview releases live on Install Python, apply immediately and remember the selection between sessions, including All architectures and individual Embeddable / Free-threaded / With tests choices. Settings retains Confirm before uninstall |
| 📐 Control sizing | Shared typography, icon size, control padding and minimum height; controls can grow for text scaling, and compact layouts rearrange content |
| ⚙️ Settings | Plain section headings and setting rows, compact style previews, responsive label/control placement, and independent inline management expanders |
| 🧰 Environment browsing | Empty-state Create / Import actions; populated lists add search and status filters, with Terminal directly available and secondary actions in a menu |
| 📋 Activity viewer | Time, INFO / WARN / ERROR and message columns; level filtering, clear and filtered copy; command/output text uses monospace and multiline errors expand for details |
| 📝 Feedback | Notification text remains visible, and an empty Shebang template disables Add with validation feedback beside the field |

Usage details for the history and interface changes are in [Python management](MANAGEMENT.md). Runtime dependencies are unchanged; 0.6.1 artifacts remain immutable historical releases.

**0.6.2 application validation (2026-09-25):** 81 core checks and 22 GUI check groups passed. GUI coverage includes 80 page / language / design / width combinations, persisted catalog filters, draft retention after cancelling reviews, and exact historical installed-state markers. Eight checks in a disposable Windows Sandbox passed, including an actual 3.14.7 → 3.14.6 replacement, execution of the selected interpreter, repair without upgrading, stale-confirmation rejection, and uninstall cleanup. This does not replace manual compositor inspection, every Windows text-scaling configuration, or fresh installer acceptance.

## ✅ Implemented in 0.6.1

| Area | Current behavior |
| --- | --- |
| 🔌 PIM connection | Automatic discovery, file-picker replacement, validation before saving, and reconnect |
| 🧰 Prerequisite recovery | Native startup window, official download links and recheck; standalone checker does not launch the app; GUI can open without PIM and reconnect after its manual installation |
| 🐍 Installed versions | Effective default, version, publisher, architecture, full executable path, search, and filters |
| ⭐ Recommendations | Latest standard stable version, expandable older versions and specialized distributions |
| 📥 Runtime actions | Uniquely resolved selector install, update, repair, uninstall, default selection, terminal, folder, and path copy; mutation baseline PIM 26.3 |
| 📦 Offline installation | Local `index.json` and ZIP bundle, required SHA-256, verified staging copy, and local sources |
| 💾 Offline download | New bundle subfolder without overwriting existing bundles |
| ⏳ Progress | Measured package bytes, smoothed speed, reliable ETA, KB / MB units; approximate extraction and indeterminate unknown phases |
| 🛑 Cancellation | Installation, updates, offline installation, and offline download; confirmation for install changes; partial files may remain |
| ✅ Result consistency | Refreshed PIM inventory, exact version and interpreter health checks; unresolved results remain warnings |
| 🔎 PATH / aliases | Default comparison, actual paths for trusted commands, shadowing, Windows settings, PIM registration refresh |
| ⚙️ PIM configuration | Four common settings, reviewed changes, unknown-field preservation, backups, conflict checks, restore, override warnings |
| 🌐 Proxy | System / direct / HTTP proxy; passwords in Windows Credential Manager; child-process-only environment |
| 🔧 Health / repair | Isolated core-module probe and confirmed PIM forced reinstall of the same available version |
| ⚙️ Python preferences | Default architecture, preview visibility, specialized-package expansion, and uninstall confirmation enabled by default |
| 🎨 Design and themes | Fluent / Material 3 Expressive semantic tokens, System / Light / Dark themes, bounded content width |
| 🪟 Fluent materials | Mica / Acrylic; Use Windows setting / On / Off; translucent content layers; opaque fallback; controller preserved across unrelated changes |
| 🌏 Languages | Main GUI: English default plus Simplified Chinese, Traditional Chinese (Taiwan), Japanese, with immediate switching and persistence; helper: same four choices, English on each launch; MSI wizard: English |
| 🖼️ App identity | PyDeck icon in executable, window, and sidebar; enlarged text-only title bar; About and dependency information |
| 📋 Activity | Bounded session output, errors, exit codes, and explicit copy action |
| 🔐 Safeguards | Argument validation, ownership and stale-state checks, cross-instance operation lock, bounded output, and atomic configuration writes |
| 📦 Packaging | Per-user MSI, MSIX, local certificate signing, separate runtime prerequisites, and stable upgrade identities |
| 🗂️ MSI installation options | Editable folder, native folder browser, optional desktop / Start menu shortcuts, and choices preserved through repair and upgrades |
| 🗜️ Source release archives | GitHub-generated ZIP / tar.gz from the release tag; no duplicate uploaded source archives; separate bilingual install guides and asset checksums |
| 🧰 Virtual environments | Create, import, inspect, explicitly check, activated terminal, open folder and forget; no project deletion |
| 🌐 Installation source | App-local HTTPS PIM index, explicit trust, source snapshots, retained install origin for update / repair, cross-host download confirmation |
| 📜 Shebang rules | Default / allow / block non-Python programs, reviewed template mappings to installed py / pyw selectors, backups and advanced-entry preservation |
| 🔄 PyDeck updates | Stable-version check and official GitHub release page in the default browser |
| 🪗 Inline settings | PIM configuration, PATH diagnostics, alias refresh, network, source and Shebang sections expand on the settings page |
| 🐍 Catalog organization | Minor-version groups, independent special-distribution filters, Python SVG, variant badges and separate EAP label |
| 🔃 Database refresh | Footer action refreshes installed inventory and the selected online catalog or offline bundle |
| 🔁 MSI replacement upgrades | Full packages replace older versions transactionally, preserving folder, shortcuts and separate user data; no binary-delta claim |

The table describes 0.6.1. See [Python management](MANAGEMENT.md) for usage. Only PIM-managed Python runtimes can be updated, repaired, or removed. Python uninstallation inside the app does not expose mid-operation cancellation. See [Security](../SECURITY.md) for trust boundaries and [installation](INSTALL.md) for dependency and package requirements.

## 🛠️ Planned / awaiting acceptance

| Item | Scope |
| --- | --- |
| ♿ Desktop acceptance | External keyboard and screen-reader testing, DPI behavior, and native backdrop appearance |
| 🔏 Production signing | Publicly trusted signing and clean-machine verification of the certificate-trust installation path |
| 🧼 Clean-machine deployment | Verify external runtime prerequisites and startup on a clean Windows 11 installation |

## 💭 Under consideration

| Idea | Notes |
| --- | --- |
| 📊 Persistent transfer history | Current activity remains session-only |
| 🧰 Package management | pip, uv, conda, environment migration and recursive deletion remain outside scope |
| 💻 ARM64 application builds | Catalog architecture filtering does not mean the GUI has a native ARM64 build |

⏸️ **Windows 10 support is deferred**. Windows 7, 8, and 8.1 are outside the target scope.

## 🧪 0.6.1 acceptance — 2026-09-24

| Check | Result | Scope |
| --- | --- | --- |
| Core regressions | 64 passed | Includes a real temporary venv, source and redirect rejection, source snapshots, Shebang preservation and update metadata |
| GUI | 18 groups passed | Four-language inline settings, environment page, Python / EAP / variant icons, package filters, minor-version grouping and existing appearance checks |
| Disposable Sandbox lifecycle | 14 cases passed | Full earlier lifecycle plus real custom HTTPS source install / repair / origin retention, PIM Shebang mapping and venv creation |

Installer and final signed-package results are recorded in the 0.6.1 release notes. The lifecycle harness runs under a disposable Sandbox System account. These results do not replace normal-user, clean-machine GUI, external accessibility or production MSIX certificate-trust acceptance.

## 🧪 0.6.0 acceptance — 2026-09-24

| Check | Result | Scope |
| --- | --- | --- |
| Core regressions | 54 passed | Includes credentials, real loopback proxy authentication / failure, resume / checksum checks, config conflicts, and ETA sampling |
| GUI | 18 groups passed | Four languages, new settings dialogs, read-only actual PATH resolution, progress, cancellation controls, and existing appearance / layout checks |
| Disposable Sandbox lifecycle | 12 cases passed | Official PIM 25.2 → 26.3; Python 3.14.6 → 3.14.7; damage / repair; cancellation; measured offline bundle; Python 3.13.15 install and actual default execution; uninstall; offline reinstall with network blocked; alias refresh and final cleanup |
| MSI options | Passed | Four shortcut combinations, Unicode / spaced directories, repair, upgrade retention, and unrelated-file preservation with isolated test identities |

The lifecycle harness uses a disposable Windows Sandbox System account. GUI checks are in-app state/render checks; they are not external mouse, keyboard, or screen-reader automation. The E2E fixtures exercise real PIM and official Python archives; no daily Python installation is damaged or removed. Final package-specific evidence is reported in the release notes. Clean-machine GUI deployment and the production MSIX certificate-trust path remain outside this matrix.

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
| 🐍 PIM integration | Read-only live queries, isolated offline extraction, and test-download cancellation exercised during development | Full lifecycle was not part of the 0.5.1 matrix |
| 🎨 Appearance | GUI state / render checks, controller retention, and wide / compact layouts | Native compositor appearance, external keyboard / screen-reader use, and all DPI settings remain pending |

Existing daily-use Python environments were not used for destructive acceptance tests. These historical 0.5.1 results do not claim full Python lifecycle or clean-machine GUI acceptance; current-source results are recorded separately.
