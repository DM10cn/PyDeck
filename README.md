<p align="center">
  <img src="src/PimGui.App/Assets/AppIcon.png" width="112" alt="PyDeck icon">
</p>

# 🐍 PyDeck

**A home for your Python versions on Windows**

**English** · [简体中文](README.zh-CN.md)

PyDeck is a native **WinUI 3** companion for **Python Install Manager**. Browse interpreters, install a version, choose your default, and keep offline packages close at hand — in a desktop interface with **Windows Fluent** and **Material 3 Expressive** styles.

📦 **Stable release · 0.6.1** · 🪟 **Windows 11 x64** · 📄 **MIT**

## 📦 Download

Get **MSI**, **MSIX**, and the **standalone dependency helper** from [GitHub Releases](https://github.com/DM10cn/PyDeck/releases). For source, use GitHub's automatic **Source code (zip)** / **Source code (tar.gz)** links in Assets. Follow the [installation guide](docs/INSTALL.md) for dependencies, installer choices, and MSIX certificate trust.

New in **0.6.1**: 🧰 virtual environments, 🌐 custom HTTPS installation sources, 📜 Shebang rules, and 🔔 app update checks that open GitHub in your browser. Python settings expand inline; the catalog filters package types and groups minor versions. Python icons carry independent variant and EAP badges. The footer refreshes local and catalog data. MSI downloads perform full-package replacement upgrades while retaining settings and installer choices.

## ✨ What you can do

- 🐍 **See your Python installations** — version, publisher, architecture, executable path, and effective default
- 📥 **Find and install Python** — a stable recommendation, search, architecture filters, and expandable specialized distributions
- 📦 **Work offline** — download a portable PIM bundle on one computer and install it on another
- ⏳ **Follow an operation** — persistent stage progress and cancellation for installation, updates, and offline downloads
- 🛠️ **Manage a runtime** — update, uninstall, set the default, open a terminal or folder, and copy its path
- 🎨 **Make it yours** — Fluent or Material 3 Expressive, System / Light / Dark themes, and optional Fluent Mica or Acrylic
- 🌏 **Choose your language** — English by default, plus Simplified Chinese, Traditional Chinese (Taiwan), and Japanese
- 🧰 **Recover missing dependencies** — open official runtime or Python Install Manager download pages, install the missing software yourself, then recheck or reconnect
- 🗂️ **Choose how to install** — MSI folder selection with Browse, optional desktop and Start menu shortcuts, and preferences retained during upgrades

New in **0.6.0**: verified operation results, PATH / alias diagnostics, PIM configuration with backup / restore, HTTP proxies with Windows Credential Manager, measured download bytes / speed / ETA, and interpreter health checks / PIM repair. See the [Python management guide](docs/MANAGEMENT.md). These additions are not in the published 0.5.1 installers.

See the [feature status and roadmap](docs/FEATURES.md) for implementation details and remaining acceptance work.

## 📋 Before you start

PyDeck targets **Windows 11 x64**. The full GUI needs **.NET Runtime 10 x64** and **Windows App Runtime**; the MSI / unpackaged GUI also needs **Visual C++ v14 x64**. Exact versions, official downloads, and format-specific requirements are maintained in the [installation guide](docs/INSTALL.md).

The native dependency window can open before those runtimes are installed. **Python Install Manager is needed to manage Python, but its absence does not block the GUI**: download it from the app's official link, install it, then reconnect. None of these dependencies is bundled or silently installed by PyDeck.

Windows 10 support is deferred. Visual Studio and the build tools below are only needed when building from source.

## 🚀 Build and run

Use **Visual Studio 2026** with **WinUI application development**, the **.NET 10 SDK**, and **Windows SDK 26100**. The SDK baseline is pinned in `global.json`; NuGet dependencies have committed lock files.

The prerequisite launcher also needs the **MSVC x64/x86 build tools** component, including C++ headers and desktop libraries. Published output starts through `PyDeck.Launcher.exe`; it statically links its C++ support library and imports only Windows system DLLs.

```powershell
git clone https://github.com/DM10cn/PyDeck.git
cd PyDeck
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Run.ps1
```

You can also open `PimGui.slnx` in Visual Studio, select `PimGui.App`, and build the C# GUI for **x64**. Use the publish script above to include the C++ prerequisite launcher; it is built by script, not by the solution. The product is **PyDeck**, its GUI is `PyDeck.exe`, and the normal launch entry is `PyDeck.Launcher.exe`; `PimGui` remains an internal project name.

The development output is an **unpackaged, framework-dependent application** under `artifacts/`. Keep all published files together. See the [release workflow](docs/RELEASING.md) to build MSI and MSIX packages.

🧑‍💻 [Development guide](docs/DEVELOPMENT.md) · 🗺️ [Feature status](docs/FEATURES.md) · 🔐 [Security notes](SECURITY.md)

## 📦 Install Python offline

1. On a connected computer, open **Install Python → Online** and choose a version's **⋯ → Download offline package** action
2. Copy the complete generated folder, including `index.json` and ZIP files, to the offline computer
3. Open **Install Python → Offline → Choose folder**, select that folder, and install a version

Prepare Python Install Manager and all [runtime prerequisites for your installation format](docs/INSTALL.md) before taking the computer offline. This feature installs Python packages offline; it does not supply PyDeck's own runtime dependencies. Bundles produced by `pymanager install --download=<folder> <tag>` are also supported.

PyDeck requires a standalone local bundle with SHA-256 checksums and stages a verified copy before installation. A checksum checks integrity, not publisher identity, so use a trusted source. Missing or damaged packages stop the operation. See [Python's offline installation guide](https://docs.python.org/3/using/windows.html#offline-installs).

## ⏳ Progress and cancellation

The operation panel stays visible across pages. In 0.6.0, official package downloads show measured bytes, smoothed speed, and ETA when reliable. Values below 1 MB use KB. Extraction percentages remain **approximate stage progress** derived from PIM output; unknown phases use an indeterminate bar. Published 0.5.1 uses approximate download progress too.

You can stop Python installation, updates, and offline downloads. Stopping an installation requires confirmation and **may leave partial files**. PyDeck waits for the current process to exit and refreshes the installed list; it does not promise rollback. Python uninstallation cannot be cancelled mid-operation. These controls apply to Python operations inside PyDeck, not to the MSI / MSIX setup wizard.

## 🎨 Appearance and language

Fluent provides **Transparency effects: Use Windows setting / On / Off**, with a separate **Mica / Acrylic** choice. Windows accessibility, power, and hardware policies can still apply a solid fallback. Material 3 Expressive uses opaque surfaces.

Language changes apply immediately and are saved. Documentation is maintained in **English and Simplified Chinese**; the application also supports Traditional Chinese (Taiwan) and Japanese. Raw PIM output and version identifiers remain unchanged.

## 🧪 Validation status

The 0.5.1 preview passed core and native checks, installed-MSI GUI checks, and MSIX development-registration / activation checks. MSI folder selection, shortcut choices, repair, and upgrades have also been exercised.

Version 0.6.0 adds a disposable Windows Sandbox lifecycle harness covering PIM upgrade, real Python update, repair, cancellation, default changes, uninstall, and offline reinstall. Clean-machine GUI deployment and the MSIX production certificate-trust installation path remain separate acceptance work. The [feature-status table](docs/FEATURES.md) records completed checks and remaining work; the [development guide](docs/DEVELOPMENT.md) explains how to run them.

## 🤝 Contributing

Issues and pull requests are welcome. Include the app version, Windows version, reproduction steps, and expected behavior. Remove personal paths, account details, credentials, and private package sources from logs before sharing them.

Keep public documentation in English and Simplified Chinese, keep the English page as the default, and update both versions when behavior changes. 🔐 Do not post secrets or sensitive vulnerability details in a public issue; see [Security](SECURITY.md).

## 📄 License and acknowledgements

PyDeck is licensed under the [MIT License](LICENSE). Third-party dependencies retain their own licenses.

PyDeck is an independent project and is not affiliated with or endorsed by the Python Software Foundation or Microsoft. Python names and logos are subject to the PSF's trademark policy; the MIT license does not grant third-party trademark rights. See [third-party notices](THIRD-PARTY-NOTICES.md).
