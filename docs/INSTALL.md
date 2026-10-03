# 📦 Install PyDeck

**English** · [简体中文](https://github.com/DM10cn/PyDeck/blob/main/docs/INSTALL.zh-CN.md) · [🏠 Project](https://github.com/DM10cn/PyDeck)

Download **PyDeck 0.7.2 for Windows 11 x64** from [GitHub Releases](https://github.com/DM10cn/PyDeck/releases/tag/v0.7.2). The MSI, Setup and dependency helper are **unsigned**; no certificate import is required. Check the download source and `SHA256SUMS.txt` before proceeding with a Windows prompt.

| File | Purpose |
| --- | --- |
| `PyDeck-Setup-0.7.2-win-x64.exe` | Recommended: offline installation and maintenance, with runtime installers |
| `PyDeck-0.7.2-win-x64.msi` | Standalone current-user installation; prepare runtimes separately |
| `PyDeck-Dependencies-0.7.2-win-x64.exe` | Dependency checklist and official download links |

## 🚀 Install with Setup

1. Finish Python operations and exit PyDeck, including its tray icon.
2. Open Setup normally, without **Run as administrator**. Choose English, Simplified Chinese, Traditional Chinese (Taiwan) or Japanese.
3. Choose a writable folder, desktop/Start menu shortcuts and the initial **Material 3 Expressive** or **Windows Fluent** style. Existing app preferences take precedence.
4. Review the runtime checklist and choose **Install**. Missing runtimes are prepared offline and may request administrator approval.
5. After installation, choose **Open PyDeck**.

Setup embeds the MSI and Microsoft runtime installers. You do not need Visual Studio or the .NET/Windows SDK. Shared runtimes remain installed after PyDeck is removed.

**Cancel** stops after the active installer returns; an operation already in progress may finish. A restart-required result stops later steps without restarting the PC automatically. Use **Open logs** for this run's `setup.log`, `result.json` and installer logs under `%LocalAppData%\PyDeck-Setup-{GUID}`.

## 📋 Runtime requirements

| Dependency | Required for | Official source |
| --- | --- | --- |
| Windows 11 x64 | App and installers | — |
| .NET Runtime 10.0.x x64, stable | GUI; Desktop Runtime 10 / SDK 10 also provide it | [.NET 10](https://dotnet.microsoft.com/download/dotnet/10.0) |
| Windows App Runtime 2.x x64, minimum 2.5.1.0 | GUI; registered for the current user | [Windows App SDK](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable x64 | MSI / unpackaged GUI | [Microsoft downloads](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) |
| Python Install Manager 26.3+ | Python installation changes | [Python for Windows](https://www.python.org/downloads/windows/) |

The standalone MSI does not install shared runtimes. Installed shortcuts open `PyDeck.Launcher.exe`, which starts the GUI when ready or shows the dependency checklist. After preparing dependencies, choose **Check again**, then **Open PyDeck**.

The standalone Dependencies EXE only checks readiness and opens download links; its **Open PyDeck** button stays disabled. Return to the installed shortcut to launch the app.

The GUI can open without PIM. Install it from Python's official page, then use **Check again**, **Auto-detect** or select its executable in Settings. Local builds can be used without PIM; see [Python management](https://github.com/DM10cn/PyDeck/blob/main/docs/MANAGEMENT.md).

## 🛠️ Standalone MSI

Open the MSI, choose the installation folder, shortcuts and initial style, then install. The default folder is `%LocalAppData%\Programs\PyDeck`; Start menu is selected by default. With both shortcuts disabled, run `PyDeck.Launcher.exe` from that folder. Keep the entire installed folder together.

The MSI wizard is in English; its native folder picker follows Windows. Both interface styles are included and can be changed later in **Settings → Appearance**.

For unattended current-user installation:

```powershell
msiexec /i PyDeck-0.7.2-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0 PYDECKSTYLE=Material
```

Shortcut properties accept `0` or `1`; `PYDECKSTYLE` accepts `Material` or `Fluent`. These defaults do not overwrite saved app preferences.

## 🔧 Repair, upgrade and removal

Setup offers **Repair** for the same MSI ProductCode and retains its installation directory. A newer MSI upgrades the existing per-user installation; downgrades are blocked. A different MSI with the same version cannot repair the installed package: remove that installation first or use a newer version.

Choose **Uninstall…** in Setup or remove PyDeck through **Settings → Apps → Installed apps**. Removal keeps Python installations, virtual environments, shared runtimes and app preferences.

MSI and MSIX are separate installation formats. When switching, remove the previous format first. For an existing historical MSIX download, use the [archived MSIX instructions](https://github.com/DM10cn/PyDeck/blob/main/docs/archive/INSTALL-MSIX.md).

📖 [Python management](https://github.com/DM10cn/PyDeck/blob/main/docs/MANAGEMENT.md) · [Build Python](https://github.com/DM10cn/PyDeck/blob/main/docs/BUILD_PYTHON.md) · [Dynamic colors](https://github.com/DM10cn/PyDeck/blob/main/docs/MONET.md)
