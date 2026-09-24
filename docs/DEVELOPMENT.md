# 🧑‍💻 Development guide

**English** · [简体中文](DEVELOPMENT.zh-CN.md) · [🏠 Home](../README.md)

## 🧰 Toolchain

- Windows 11 x64
- PowerShell 7 for repository build and release scripts
- Visual Studio 2026 with **WinUI application development**
- **MSVC x64/x86 build tools** (`Microsoft.VisualStudio.Component.VC.Tools.x86.x64`), including C++ headers and desktop libraries, for the native launcher and MSI actions
- .NET SDK **10.0.400** or a later patch in the same **10.0.4xx** feature band, as specified by `global.json` and `latestPatch`
- Windows SDK **10.0.26100**

These are source-build tools. To run the published GUI, follow the [end-user dependency guide](INSTALL.md), including VC++ for the unpackaged build. PIM is optional for opening the GUI, but required for Python operations and live / GUI smoke checks.

The app targets `net10.0-windows10.0.26100.0`. The lower minimum platform value in the project is not a claim of tested Windows 10 support. The build currently targets x64 only.

The project pins individual Windows App SDK component packages in `PimGui.App.csproj` and `packages.lock.json`; those build references are distinct from the compatible runtime versions described in the installation guide.

## 🔨 Build

```powershell
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Run.ps1
```

The build script keeps NuGet packages and .NET CLI state under ignored `.local/` directories. Publishing creates a new timestamped folder and records it in `artifacts/latest-build.txt` for the run script.

Visual Studio builds the C# projects in `PimGui.slnx`. `Build.ps1 -Publish` additionally compiles the native C++ launcher, verifies system-only DLL imports, and runs the native checks. `Build.ps1 -Checks` without `-Publish` runs the core checks only. `Run.ps1` uses the launcher's most recent development publish, not a release installer or a bare Visual Studio build.

The output uses external .NET and Windows App Runtime installations. The script checks the runtime configuration, compiled WinUI resources, and absence of embedded native runtime binaries. Keep every published file together.

`EnableMsixTooling` enables WinUI resource compilation; `WindowsPackageType=None` keeps development output unpackaged. The separate [release workflow](RELEASING.md) creates MSI/MSIX packages. No signing private key or runtime installer is included in the repository.

NuGet versions are recorded in `packages.lock.json`. To enforce the committed dependency graph during restore:

```powershell
dotnet restore PimGui.slnx --locked-mode -p:Platform=x64
```

## 🧭 Source map

| Location | Responsibility |
| --- | --- |
| `src/PimGui.Core` | PIM discovery and protocol, subprocess execution, offline bundles, settings, catalog, and language resources |
| `src/PimGui.App` | WinUI pages, semantic design tokens, appearance policy, operation panel, and in-app smoke checks |
| `src/PyDeck.Launcher` | Win32 prerequisite dialog, system-only imports, official download links, and guarded GUI startup |
| `packaging/msi` | WiX install options and native folder-picker / preference actions with a static C++ runtime |
| `tests/PimGui.E2E` | Destructive Python lifecycle harness, guarded for disposable Windows Sandbox only |
| `tests/PimGui.Checks` | C# core regressions and opt-in PIM integration checks |
| `tests/Launcher.Checks.cpp` | Native prerequisite combinations, dialog controls, language choices, and recheck behavior |
| `scripts` | Build, launch, GUI smoke test, and icon generation |
| `docs` | English and Simplified Chinese documentation |

The product name is PyDeck. Existing `PimGui` project names and namespaces are internal identifiers. `PyDeck.exe` is the C# GUI; `PyDeck.Launcher.exe` is the normal startup entry. The small `resource.h` file defines native control IDs and may be counted as C by GitHub; it is not a separate program.

The launcher uses a static C++ runtime (`/MT`). Use `scripts/Build-Launcher.ps1 -OutputDirectory artifacts/launcher -Checks` to build it and run its tests separately. Pass `--dependencies` to `PyDeck.Launcher.exe` to show the window even when ready, or `--check` for read-only JSON status (exit 0 when ready, otherwise a missing-runtime bit mask; PIM is not checked here). The [installation guide](INSTALL.md) distinguishes this launcher from the standalone checker. Native tests use injected missing states and real Win32 controls; clean-machine deployment remains separate acceptance work.

## 🧪 Testing

```powershell
# Build and run core regression checks
.\scripts\Build.ps1 -Checks

# Also query the real PIM installation and online catalog (read-only)
.\scripts\Build.ps1 -LiveChecks

# In-app state/render checks against a published GUI
.\scripts\Smoke-Test.ps1
```

Core checks cover malformed protocol responses, runtime identity, stale and unmanaged runtimes, operation locks, bounded process output, configuration preservation, offline validation, backdrop policy, and locale key parity.

The GUI check requires working runtimes, PIM, and online catalog access. It uses isolated preferences and writes screenshots/results under ignored `artifacts/`. It exercises page state, missing-manager recovery, filters, themes, transparency settings, language switching, layouts, and saved preferences. It is **an in-app state/render check**, not external mouse or keyboard automation. RenderTargetBitmap does not capture compositor Mica/Acrylic or native caption buttons.

Optional integration checks:

```powershell
# Validate a bundle containing one official embeddable Python package,
# extract into an isolated directory, and run it; use your own bundle path
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python"

# Start a real test bundle download and cancel at the first progress event
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python" --cancel-live-download
```

These checks require PIM and can create temporary test files; the download check uses the network. They do not install into the normal PIM-managed runtime location. Logs and screenshots can contain local paths and should be reviewed before sharing.

See [feature status](FEATURES.md) for the current validation record and outstanding acceptance work. MSI / MSIX installation checks are covered by the [release workflow](RELEASING.md); they do not validate the complete Python interpreter lifecycle.

## 🐍 Isolated lifecycle acceptance

Use Windows 11 with the Windows Sandbox CLI (`wsb`). The script creates and stops its own headless sandbox and never uses daily Python installations:

```powershell
.\scripts\Test-PimLifecycle.ps1
```

It tests official PIM 25.2 → 26.3, an actual Python patch update, damage / repair, measured downloads, cancellation, default changes, uninstall, and offline reinstall. `-SeedBundleDirectory` accepts a flat older official offline bundle; `-InstallerDirectory` reuses cached `pim-25.2.msi` / `pim-26.3.msi`, still checking PSF Authenticode signatures. Results are written under `artifacts/pim-e2e-*`. A failed case stops dependent cases; unexecuted cases are not passes.

Do not run the harness by creating its marker on the host. PIM registration cleanup has user-wide effects even with a separate installation directory. The sandbox uses its disposable System account, so this does not replace all interactive-user, Store-package, policy, or clean-machine GUI acceptance. See [Python management](MANAGEMENT.md).

## 🎨 Appearance and localization

Use `DesignTokens` and the appearance policy for semantic colors, spacing, and surfaces. Keep style-dependent decisions out of individual page layouts. Fluent can use Mica or Acrylic; Material 3 Expressive is opaque. Unrelated settings changes must preserve the active native backdrop controller.

App strings are embedded JSON resources under `src/PimGui.Core/Strings`. Maintain the same keys in `en-US`, `zh-CN`, `zh-TW`, and `ja-JP`. English is the default. Prefer short, natural UI wording; avoid unnecessary sentence-ending punctuation in CJK labels. PIM identifiers and raw process output should not be translated.

The repository's Markdown documentation has only English and Simplified Chinese editions. Update both editions together and keep their sections aligned. Maintain end-user requirements in `INSTALL`, feature / acceptance status in `FEATURES`, build instructions here, and packaging procedures in `RELEASING`. README summarizes those pages and links to them; avoid maintaining another version matrix there.

## 🔐 Local data and contributions

Preferences are stored in `%LocalAppData%\PyDeck\settings.json`. The legacy `PimGui` preference path is read only for migration when the new file is absent. Session activity stays in memory; crash diagnostics remain local and bounded.

Do not commit build output, package caches, credentials, signing keys, private configuration, personal logs, screenshots, or planning notes. `.gitignore` covers common cases, but review the staged diff before submitting changes. See [Security](../SECURITY.md).

The icon source is in `src/PimGui.App/Assets/AppIcon.Source.png`. Rebuild its PNG/ICO variants with `scripts/Build-Icon.ps1`; review [third-party notices](../THIRD-PARTY-NOTICES.md) before changing branding.
