<p align="center">
  <img src="src/PimGui.App/Assets/AppIcon.png" width="112" alt="PyDeck icon">
</p>

# 🐍 PyDeck

**A home for your Python versions on Windows**

**English** · [简体中文](README.zh-CN.md)

PyDeck is a native **WinUI 3** companion for **Python Install Manager**. Browse interpreters, install a version, choose your default, and keep offline packages close at hand — in a desktop interface with **Windows Fluent** and **Material 3 Expressive** styles.

🚧 **Development preview · 0.4.0** · 🪟 **Windows 11 x64** · 📄 **MIT**

## 📦 Download

Get **MSI**, **MSIX**, and **source ZIP / tar.gz** from [GitHub Releases](https://github.com/DM10cn/PyDeck/releases). See the [installation guide](docs/INSTALL.md) before installing: runtimes remain separate dependencies, and the self-signed MSIX preview requires an explicit certificate-trust step.

## ✨ What you can do

- 🐍 **See your Python installations** — version, publisher, architecture, executable path, and effective default
- 📥 **Find and install Python** — a stable recommendation, search, architecture filters, and expandable specialized distributions
- 📦 **Work offline** — download a portable PIM bundle on one computer and install it on another
- ⏳ **Follow an operation** — persistent stage progress and cancellation for installation, updates, and offline downloads
- 🛠️ **Manage a runtime** — update, uninstall, set the default, open a terminal or folder, and copy its path
- 🎨 **Make it yours** — Fluent or Material 3 Expressive, System / Light / Dark themes, and optional Fluent Mica or Acrylic
- 🌏 **Choose your language** — English by default, plus Simplified Chinese, Traditional Chinese (Taiwan), and Japanese

See the [feature status and roadmap](docs/FEATURES.md) for implementation details and remaining acceptance work.

## 📋 Before you start

| Dependency | Requirement |
| --- | --- |
| Operating system | Windows 11, x64 |
| .NET | .NET Runtime 10, x64 |
| Windows App Runtime | 2.5.1, x64, matching the project reference |
| Python manager | [Python Install Manager](https://docs.python.org/3/using/windows.html) |

**.NET and Windows App Runtime are separate prerequisites.** PyDeck does not bundle or automatically download either runtime. The .NET Desktop Runtime or SDK also includes the base .NET runtime required by this app.

Python Install Manager is detected automatically. You can choose its executable in Settings if detection fails. Windows 10 support is currently deferred.

## 🚀 Build and run

Use **Visual Studio 2026** with **WinUI application development**, the **.NET 10 SDK**, and **Windows SDK 26100**. The SDK baseline is pinned in `global.json`; NuGet dependencies have committed lock files.

```powershell
git clone https://github.com/DM10cn/PyDeck.git
cd PyDeck
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Run.ps1
```

You can also open `PimGui.slnx` in Visual Studio, select `PimGui.App`, and build for **x64**. The internal project names remain `PimGui`; the application and executable are **PyDeck**.

The development output is an **unpackaged, framework-dependent application** under `artifacts/`. Keep all published files together. See the [release workflow](docs/RELEASING.md) to build MSI and MSIX packages.

🧑‍💻 [Development guide](docs/DEVELOPMENT.md) · 🗺️ [Feature status](docs/FEATURES.md) · 🔐 [Security notes](SECURITY.md)

## 📦 Install Python offline

1. On a connected computer, open **Install Python → Online** and choose a version's **⋯ → Download offline package** action
2. Copy the complete generated folder, including `index.json` and ZIP files, to the offline computer
3. Open **Install Python → Offline → Choose folder**, select that folder, and install a version

Python Install Manager and both runtime prerequisites must already be present on the offline computer. Bundles produced by `pymanager install --download=<folder> <tag>` are also supported.

PyDeck requires a standalone local bundle with SHA-256 checksums and stages a verified copy before installation. A checksum checks integrity, not publisher identity, so use a trusted source. Missing or damaged packages stop the operation. See [Python's offline installation guide](https://docs.python.org/3/using/windows.html#offline-installs).

## ⏳ Progress and cancellation

The operation panel stays visible across pages. Download and extraction percentages are **approximate stage progress** derived from PIM output; other phases use an indeterminate bar.

You can stop installation, updates, and offline downloads. Stopping an installation requires confirmation and **may leave partial files**. PyDeck waits for the current process to exit and refreshes the installed list; it does not promise rollback. Uninstallation cannot be cancelled mid-operation.

## 🎨 Appearance and language

Fluent provides **Transparency effects: Use Windows setting / On / Off**, with a separate **Mica / Acrylic** choice. Windows accessibility, power, and hardware policies can still apply a solid fallback. Material 3 Expressive uses opaque surfaces.

Language changes apply immediately and are saved. Documentation is maintained in **English and Simplified Chinese**; the application also supports Traditional Chinese (Taiwan) and Japanese. Raw PIM output and version identifiers remain unchanged.

## 🧪 Validation status

The repository includes core regression checks and an optional in-app GUI smoke test. Read-only live PIM queries, isolated offline extraction, and cancellation of a test download have also been exercised.

Full install/update/uninstall/default-switch acceptance in a disposable environment, external keyboard and screen-reader testing, native backdrop appearance, and clean-machine deployment remain outstanding. See [development and testing](docs/DEVELOPMENT.md) for commands and boundaries.

## 🤝 Contributing

Issues and pull requests are welcome. Include the app version, Windows version, reproduction steps, and expected behavior. Remove personal paths, account details, credentials, and private package sources from logs before sharing them.

Keep public documentation in English and Simplified Chinese, keep the English page as the default, and update both versions when behavior changes. 🔐 Do not post secrets or sensitive vulnerability details in a public issue; see [Security](SECURITY.md).

## 📄 License and acknowledgements

PyDeck is licensed under the [MIT License](LICENSE). Third-party dependencies retain their own licenses.

PyDeck is an independent project and is not affiliated with or endorsed by the Python Software Foundation or Microsoft. Python names and logos are subject to the PSF's trademark policy; the MIT license does not grant third-party trademark rights. See [third-party notices](THIRD-PARTY-NOTICES.md).
