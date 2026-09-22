# 🧑‍💻 Development guide

**English** · [简体中文](DEVELOPMENT.zh-CN.md) · [🏠 Home](../README.md)

## 🧰 Toolchain

- Windows 11 x64
- Visual Studio 2026 with **WinUI application development**
- .NET SDK **10.0.400** or a compatible patch, as specified in `global.json`
- Windows SDK **10.0.26100**
- For running the GUI: .NET Runtime 10 x64, Windows App Runtime **2.5.1 x64**, and Python Install Manager

The app targets `net10.0-windows10.0.26100.0`. The lower minimum platform value in the project is not a claim of tested Windows 10 support. The build currently targets x64 only.

## 🔨 Build

```powershell
.\scripts\Build.ps1 -Checks -Publish
.\scripts\Run.ps1
```

The build script keeps NuGet packages and .NET CLI state under ignored `.local/` directories. Publishing creates a new timestamped folder and records it in `artifacts/latest-build.txt` for the run script.

The output uses external .NET and Windows App Runtime installations. The script checks the runtime configuration, compiled WinUI resources, and absence of embedded native runtime binaries. Keep every published file together.

`EnableMsixTooling` enables WinUI resource compilation; `WindowsPackageType=None` keeps development output unpackaged. No MSI, MSIX, signing certificate, or runtime installer is included.

NuGet versions are recorded in `packages.lock.json`. To enforce the committed dependency graph during restore:

```powershell
dotnet restore PimGui.slnx --locked-mode -p:Platform=x64
```

## 🧭 Source map

| Location | Responsibility |
| --- | --- |
| `src/PimGui.Core` | PIM discovery and protocol, subprocess execution, offline bundles, settings, catalog, and language resources |
| `src/PimGui.App` | WinUI pages, semantic design tokens, appearance policy, operation panel, and in-app smoke checks |
| `tests/PimGui.Checks` | Core regressions and opt-in PIM integration checks |
| `scripts` | Build, launch, GUI smoke test, and icon generation |
| `docs` | English and Simplified Chinese documentation |

The product name is PyDeck. Existing `PimGui` project names and namespaces are internal identifiers.

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

The GUI check uses isolated preferences and writes screenshots/results under ignored `artifacts/`. It exercises page state, filters, themes, transparency settings, language switching, layouts, and saved preferences. It is **an in-app state/render check**, not external mouse or keyboard automation. RenderTargetBitmap does not capture compositor Mica/Acrylic or native caption buttons.

Optional integration checks:

```powershell
# Validate a bundle containing one official embeddable Python package,
# extract into an isolated directory, and run it; use your own bundle path
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python"

# Start a real test bundle download and cancel at the first progress event
dotnet run --project tests/PimGui.Checks -- --offline-fixture "C:\TestBundles\Python" --cancel-live-download
```

These checks require PIM and can create temporary test files; the download check uses the network. They do not install into the normal PIM-managed runtime location. Logs and screenshots can contain local paths and should be reviewed before sharing.

🚧 Remaining acceptance work includes the complete managed-runtime lifecycle in a disposable environment, external keyboard/screen-reader testing, native backdrop appearance, DPI behavior, and deployment on a clean machine. Passing core checks is not a substitute for these tests.

## 🎨 Appearance and localization

Use `DesignTokens` and the appearance policy for semantic colors, spacing, and surfaces. Keep style-dependent decisions out of individual page layouts. Fluent can use Mica or Acrylic; Material 3 Expressive is opaque. Unrelated settings changes must preserve the active native backdrop controller.

App strings are embedded JSON resources under `src/PimGui.Core/Strings`. Maintain the same keys in `en-US`, `zh-CN`, `zh-TW`, and `ja-JP`. English is the default. Prefer short, natural UI wording; avoid unnecessary sentence-ending punctuation in CJK labels. PIM identifiers and raw process output should not be translated.

The repository's Markdown documentation has only English and Simplified Chinese editions. Update both editions together.

## 🔐 Local data and contributions

Preferences are stored in `%LocalAppData%\PyDeck\settings.json`. The legacy `PimGui` preference path is read only for migration when the new file is absent. Session activity stays in memory; crash diagnostics remain local and bounded.

Do not commit build output, package caches, credentials, signing keys, private configuration, personal logs, screenshots, or planning notes. `.gitignore` covers common cases, but review the staged diff before submitting changes. See [Security](../SECURITY.md).

The icon source is in `src/PimGui.App/Assets/AppIcon.Source.png`. Rebuild its PNG/ICO variants with `scripts/Build-Icon.ps1`; review [third-party notices](../THIRD-PARTY-NOTICES.md) before changing branding.
