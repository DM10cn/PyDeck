<p align="center">
  <img src="src/PimGui.App/Assets/AppIcon.png" width="112" alt="PyDeck icon">
</p>

# 🐍 PyDeck

**A home for your Python versions on Windows**

**English** · [简体中文](README.zh-CN.md)

PyDeck is a native **WinUI 3** desktop app for managing Python interpreters, virtual environments, packages and source builds. It works with **Python Install Manager** and offers independent **Windows Fluent** and **Material 3 Expressive** interfaces.

🪟 **Windows 11 x64** · 📦 **0.7.2** · 📄 **MIT**

## 📥 Download

Get the [latest release](https://github.com/DM10cn/PyDeck/releases/latest).

| Package | Use |
| --- | --- |
| `PyDeck-Setup-0.7.2-win-x64.exe` | Recommended: offline installation and maintenance, with shared runtime installers included |
| `PyDeck-0.7.2-win-x64.msi` | Standalone per-user installation when the required runtimes are already available |
| `PyDeck-Dependencies-0.7.2-win-x64.exe` | Dependency status and official download links |

Current EXE/MSI packages are unsigned. Setup lets you choose the installation folder, shortcuts and initial interface style, and supports repair and uninstall. See the [installation guide](docs/INSTALL.md).

## ✨ Features

- 🐍 **Python versions** — browse installed interpreters, choose a default, update or repair installations, and open terminals or folders
- 🔎 **Version catalog** — select exact micro versions, search grouped releases, and filter by architecture, distribution and preview status
- 🧰 **Virtual environments** — create or import environments, inspect runtime usage, and open activated terminals
- 📦 **Packages** — search, install, upgrade and uninstall packages inside environments; manage pip and import/export requirements
- 🛠️ **Build Python** — use official CPython sources or local archives, choose presets and components, prepare build tools, and manage build storage
- 📴 **Offline bundles** — download Python packages on one computer and install them on another
- ⏳ **Tasks and activity** — follow separate task progress, switch between active operations, cancel supported tasks, and filter structured logs
- ⚙️ **Settings** — configure Python sources, proxies and Shebang rules; manage PIM configuration backups and PATH/alias diagnostics
- 🎨 **Appearance** — Fluent with Mica/Acrylic, or Material with wallpaper, custom and two-color palettes; light, dark and system themes
- 🌏 **Languages** — English, Simplified Chinese, Traditional Chinese (Taiwan) and Japanese

Material uses the native HCT color engine with SIMD-accelerated wallpaper extraction. Theme, language and manually applied colors change immediately; switching interface style takes effect after restart. Wallpaper images stay on your computer.

## 🚀 Getting started

1. Install PyDeck using Setup, which prepares missing shared runtimes before installing the app.
2. Connect **Python Install Manager**. If it is missing, use the official download link in the app, install it, then reconnect.
3. Open **Install Python** to choose a version, or create an environment from **Virtual environments**.

The GUI requires **.NET Runtime 10 x64**, **Windows App Runtime** and **Visual C++ v14 x64**. Setup includes their installers; standalone MSI users prepare them separately. Python Install Manager is required for PIM-managed interpreter operations, but the GUI can open without it. End users do not need an SDK.

For offline Python installation, use **Install Python → Online → ⋯ → Download offline package**, copy the complete generated folder, then select it under **Install Python → Offline** on the destination computer.

📖 [Installation](docs/INSTALL.md) · [Python management](docs/MANAGEMENT.md) · [Build Python](docs/BUILD_PYTHON.md)

## 🧑‍💻 Build from source

Use **Visual Studio 2026** with WinUI application development, **.NET 10 SDK**, **Windows SDK 26100** and the **MSVC x64/x86 build tools**.

```powershell
git clone https://github.com/DM10cn/PyDeck.git
cd PyDeck
.\scripts\Build.ps1 -Publish
.\scripts\Run.ps1
```

The repository includes C#, C++ and handwritten `.asm` sources. The build compiles the native color engine into `PyDeck.Colors.dll` and includes it in the application output. GitHub's source ZIP / tar.gz downloads contain the assembly source files.

See the [development guide](docs/DEVELOPMENT.md) and [color-engine source notes](docs/MONET.md).

## 🤝 Contributing

Issues and pull requests are welcome. Include reproduction steps and your app/Windows versions, and remove private information from shared logs. Keep English and Simplified Chinese documentation in sync. For sensitive reports, see [Security](SECURITY.md).

## 📄 License

[MIT](LICENSE). Third-party dependencies retain their own licenses; see [third-party notices](THIRD-PARTY-NOTICES.md). PyDeck is independent of the Python Software Foundation and Microsoft. Python names and logos remain subject to the PSF trademark policy.
