# 📦 Install PyDeck

**English** · [简体中文](INSTALL.zh-CN.md) · [🏠 Project](https://github.com/DM10cn/PyDeck)

📦 **0.7.2 · Windows 11 x64 · MSI / Setup**

Download the **unsigned 0.7.2** MSI and Setup from the [GitHub release](https://github.com/DM10cn/PyDeck/releases/tag/v0.7.2), together with the installation notes and SHA-256 checksums. The historical 0.7.1-fix assets remain unchanged. MSI and MSIX do not upgrade each other; uninstall the previous format before switching. Removal keeps Python installations and PyDeck preferences.

## 📦 0.7.2 downloads

| File | Purpose |
| --- | --- |
| `PyDeck-Setup-0.7.2-win-x64.exe` | Offline MSI installation and maintenance, with runtime preparation |
| `PyDeck-0.7.2-win-x64.msi` | Standalone current-user installation, with folder, shortcut, and initial interface options |
| `PyDeck-Dependencies-0.7.2-win-x64.exe` | Standalone dependency checklist and official download links |

This build uses `-MsiOnly`: it produces no new MSIX or signing certificate. The MSIX/certificate instructions below apply only to the historical **0.7.1-fix** release. Use `SHA256SUMS.txt` from the 0.7.2 release for these downloads; historical checksums do not apply.

## 🧰 Offline Setup

**`PyDeck-Setup-0.7.2-win-x64.exe` is the primary MSI installation entry.** It uses a simple native Windows window and embeds the Microsoft runtime installers plus the standalone MSI. Ordinary users need runtimes, **not the .NET SDK, Windows SDK, or Visual Studio**. Previously published 0.7.1-fix files remain unchanged and use the separate MSI wizard.

1. Let Python operations finish and exit PyDeck, including its tray icon; minimizing the window is not an exit
2. Open Setup normally, without **Run as administrator**. It selects English, Simplified Chinese, Traditional Chinese (Taiwan), or Japanese from Windows; you can change the language
3. Choose a writable installation folder, desktop/Start menu shortcuts, and the initial **Material 3 Expressive** or **Windows Fluent** style. Existing app preferences take precedence
4. Review the runtime checklist and choose **Install**. Compatible runtimes are skipped; missing ones are prepared offline and may request administrator approval
5. MSI runs silently while the same Setup window displays the current stage, progress activity, completion or error code. After successful installation, choose **Open PyDeck**

For an installed MSI with the same ProductCode, Setup offers **Repair** and retains its installation directory. A newer package can upgrade an older installation. A different MSI with the same version is a conflict, not a repair: use a newer version or remove the existing package first. Installing over a newer installed version is blocked.

**Uninstall…** asks for confirmation, then removes the detected current-user product through Windows Installer's cached MSI. The uninstall execution path skips embedded-payload extraction and runtime checks/installation. It removes PyDeck's installed files and shortcuts while keeping Python, virtual environments, shared runtimes, and app preferences. You can also uninstall from **Settings → Apps → Installed apps**.

📦 Runtime EXEs are embedded in Setup, not copied into the installed app folder. The GUI remains framework-dependent. Shared runtimes remain after PyDeck is removed. Setup and the launcher share architecture, version, file, and current-user registration checks. Detection is a readiness check, not a full runtime integrity scan.

⏳ Progress identifies the current installer rather than inventing an overall percentage. **Cancel** requests a stop after the active installer returns; an install or uninstall already in progress may finish. Setup never kills Windows Installer. Failed rechecks, UAC cancellation, and installer failures stop the chain; fix the issue and retry. A restart-required result stops later steps and does not restart the PC automatically. **Open logs** opens this run's private `%LocalAppData%\PyDeck-Setup-{GUID}` folder with `setup.log`, `result.json`, and available vendor/MSI logs. Extracted installers are removed after exit; cleanup failures are logged.

🪟 For MSIX, select **Prepare dependencies only** (or run Setup with `--dependencies-only`). This prepares .NET and registers Windows App Runtime for the desktop user; it does not install the MSI or a separate VC++ redistributable. Install MSIX afterward using the certificate instructions below. MSIX/App Installer can resolve declared MSIX framework dependencies when their source is available; it does **not** run the ordinary .NET 10 EXE installer. An offline `.msix` alone is not a complete prerequisite bundle. Setup does not change certificate trust or install PIM.

🔎 `--preview` opens the Setup UI with installation, uninstallation, and app launch disabled; it does not run an installer. `--check` reports readiness. `--verify-payloads` extracts and checks embedded files, writes a report, then removes the extracted files without running them. These modes do not replace clean-machine installation acceptance.

## 📋 Prepare the prerequisites

This page is the reference for end-user dependencies. Build tools belong in the [development guide](https://github.com/DM10cn/PyDeck/blob/main/docs/DEVELOPMENT.md).

| Dependency | When needed | Official source |
| --- | --- | --- |
| Windows 11, x64 | PyDeck, its installers, and the native helper; Windows 10 support is deferred | — |
| .NET Runtime 10.0.x, x64, stable | Full GUI, both MSI and MSIX | [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0); Desktop Runtime 10 / SDK 10 also provide this runtime |
| Windows App Runtime 2.x, x64, minimum 2.5.1.0 | Full GUI, both formats; registered for the current user, with a compatible 2.x update accepted | [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable, x64 | MSI / unpackaged GUI; Windows resolves framework dependencies when installing MSIX | [Microsoft's supported downloads](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) |
| Python Install Manager | Python management operations; the GUI can open without it | [Official Python Windows downloads](https://www.python.org/downloads/windows/) |

🔌 The app and standalone MSI/MSIX do not bundle or install these shared runtimes or PIM. **Setup** provides the runtime installation flow above. The native helper, Setup, and MSI folder browser statically link their C++ support libraries, so those components themselves do not require .NET, Windows App Runtime, WebView2, or a separately installed VC++ runtime. Prepare the GUI dependencies and PIM before using offline Python bundles.

### 🧰 Two dependency tools

| Entry | Behavior |
| --- | --- |
| Installed `PyDeck.Launcher.exe` / PyDeck shortcut | Starts the GUI when dependencies are ready; otherwise shows the dependency window. Install missing packages yourself, choose **Check again**, then **Open PyDeck** |
| Standalone `PyDeck-Dependencies-0.7.2-win-x64.exe` | Always opens the checklist and download links. **Open PyDeck stays disabled**: this tool does not install or launch the app, even beside an app executable. Close it and return to the installer or installed shortcut |

The **Download** buttons open official Microsoft sources. The tools do not execute installers, elevate themselves, or change certificate trust. The standalone checklist checks the unpackaged prerequisites, including VC++; MSIX installation itself is governed by Windows package-dependency checks.

For MSIX, use the standalone helper before installation if needed: a missing framework can block the package before its bundled launcher can run. The helper cannot bypass MSIX dependencies or certificate trust.

🌏 The app, helper, and Setup offer English, Simplified Chinese, Traditional Chinese (Taiwan), and Japanese. The app saves its language; the standalone helper starts in English, while Setup initially follows Windows. Opening the standalone MSI directly still uses its English wizard; the native folder picker uses Windows language settings.

The optional `Test-Prerequisites.ps1` asset is a supplementary read-only checklist of common install locations and PIM on PATH. Its warnings are not the launcher's exact readiness decision: a manager outside PATH can be selected in Settings, and missing PIM does not block GUI startup. Use `PyDeck.Launcher.exe --check` for the native launcher's read-only runtime status.

### 🐍 Connect Python Install Manager

PyDeck requires PIM **26.3 or later** for installation changes. Older or unrecognized managers are limited to compatible read-only queries; reconnect after upgrading. See [Python management](https://github.com/DM10cn/PyDeck/blob/main/docs/MANAGEMENT.md).

Once the GUI runtimes are ready, you can open PyDeck without PIM. Choose **Download Python Install Manager** in the empty state or Settings, install it from Python's official Windows page, then choose **Check again** or **Auto-detect**. You can also select its executable in Settings. These buttons open the download page or reconnect; they do not install PIM themselves, and reconnect does not require restarting PyDeck.

## 🛠️ MSI

1. Download and open `PyDeck-0.7.2-win-x64.msi`
2. Choose the installation folder, or use **Browse…** to open the Windows folder picker
3. Choose **Create a desktop shortcut** and **Add PyDeck to the Start menu** as needed; only Start menu is selected by default
4. Choose the initial **Material 3 Expressive** (default) or **Windows Fluent** interface
5. Install, then open **PyDeck** from your chosen shortcut

The MSI installs for the current user, with `%LocalAppData%\Programs\PyDeck` as the default. You can enter another writable folder or select one in the native folder picker. Repair and later MSI upgrades retain your folder and shortcut choices. If you disable both shortcuts, open `PyDeck.Launcher.exe` in the chosen folder.

Both interface styles are included in the same package. The installer choice is a first-launch default only: existing PyDeck or legacy app settings take precedence during upgrades and reinstalls. You can change the saved style later in **Settings → Appearance**.

It checks Windows 11 before installation, but allows GUI runtime dependencies to be installed later. Both shortcuts open the native prerequisite launcher, which enters the full app once its runtime checks pass. Keep the entire installed folder together; opening `PyDeck.exe` directly bypasses the prerequisite window.

🧑‍💻 For an unattended current-user installation, the same options are available as MSI properties (`0` = off, `1` = on):

```powershell
msiexec /i PyDeck-0.7.2-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0 PYDECKSTYLE=Material
```

`PYDECKSTYLE` accepts exactly `Material` or `Fluent`; omitting it uses the previous installer choice, or Material for a new installation. It does not overwrite saved app preferences. Use a folder your account can write to. The native folder picker does not require .NET. MSIX uses Windows-managed placement and does not expose these MSI options.

The 0.7.2 MSI, Setup, and dependency helper are **unsigned**. They do not use the old preview certificate and do not require importing it. Verify the download source and release hashes before proceeding with a Windows prompt.

Newer MSI versions upgrade the same per-user installation and older versions are blocked. For the maintenance choices and version-conflict checks, use Setup as described above. The standalone MSI and Windows **Installed apps** remain available; removal affects installer-owned files and shortcuts, not Python or shared runtimes.

## 🪟 Historical 0.7.1-fix MSIX — self-signed package

This section is retained for the published **0.7.1-fix** MSIX only. The 0.7.2 release does not include an MSIX or a new certificate. The historical MSIX requires a trusted signing certificate and is **not publicly trusted**. Only trust the publisher if you have verified the files and intend to use that package. The private signing key is never distributed.

1. Download `PyDeck-0.7.1-fix-win-x64.msix`, `PyDeck-preview.cer`, and `SHA256SUMS.txt` from the same release
2. Compare file hashes with `SHA256SUMS.txt`, using `Get-FileHash -Algorithm SHA256`
3. Inspect the certificate: publisher **CN=DM10cn**, thumbprint **3912817E181E5EA9AF0DED6BC51E332E99BBDD16**
4. Import the public certificate into **Local Computer → Trusted People**, then open the MSIX

An administrator can perform the explicit trust step in PowerShell:

```powershell
Import-Certificate -FilePath .\PyDeck-preview.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Then install the package as your normal desktop user:

```powershell
Add-AppxPackage -Path .\PyDeck-0.7.1-fix-win-x64.msix
```

Do **not** import this certificate into Trusted Root Certification Authorities. PyDeck's scripts do not import certificates or change system trust automatically. This preview certificate expires on **2028-09-23**; the packages are not timestamped for long-term distribution.

MSIX declares a dependency on `Microsoft.WindowsAppRuntime.2`, minimum `2.5.1.0`. A missing dependency must be installed first. .NET 10 remains a separate machine prerequisite. The package is a full-trust desktop application and keeps PIM configuration and Python files outside MSIX virtualization.

MSIX includes both interface styles. Choose between them in the app's **Settings → Appearance**; it has no MSI-style installation options page. Without existing preferences or an installer default, the app starts in Material.

🧹 After removing all packages signed by this preview certificate, you may remove the certificate from Trusted People using `certlm.msc`. Keep it if you still need those packages.

## 🔁 Updates and data

MSI uses a stable upgrade identity; MSIX uses the `DM10cn.PyDeck` package name and `CN=DM10cn` publisher. Keep those identities for compatible future updates. Replacing the publisher or switching installer formats requires a migration plan.

Settings remain under `%LocalAppData%\PyDeck`. Python installations, PIM configuration, and shared runtimes are independent of PyDeck's installer. Close PyDeck before updating or removing it, and let any Python operation finish first.

### 🎨 First launch and appearance

Fluent and Material 3 Expressive use separate presentations. Material defaults to local wallpaper colors; Settings also provides manual base colors, Balanced / Expressive palettes, and **Two colors**, PyDeck's `DualSource` extension. Two colors uses distinct wallpaper candidates when available or two manually applied colors. It is not a complete Google CMF port. The [color-engine notes](https://github.com/DM10cn/PyDeck/blob/v0.7.1/docs/MONET.md) document pinned AOSP/MCU sources and the 2021 color specification. Wallpapers are not copied or uploaded.

Changing interface style saves the selection and opens **Restart now / Later**. Restart now stays gray for **1.8 seconds** and remains unavailable while any task is active. Choose Later to keep working; the saved interface takes effect at the next launch. A rejected restart request displays a message so you can close and reopen the app yourself. Light/dark theme, language, and explicitly applied colors do not need a restart.

## 🗜️ Source archives

Use **Source code (zip)** or **Source code (tar.gz)** under the release's Assets section. GitHub generates these archives from the release tag; PyDeck does not upload duplicate source packages. `SHA256SUMS.txt` covers the uploaded assets, not GitHub-generated archives. Source and attached guides are snapshots; later documentation corrections live in the [current repository guide](https://github.com/DM10cn/PyDeck/blob/main/docs/INSTALL.md) without replacing the tagged source or signed packages.

## 🧪 Validation limits

The **0.7.2** build and 20 native installer check groups passed, including button callbacks with simulated external effects; see [the interaction report](INSTALLER-CHECKS.md). Actual installation, repair, upgrade, uninstallation, and app smoke tests were not run. The uninstall optimization removes unnecessary extraction and dependency work; no before/after timing or percentage improvement is claimed. These checks do not establish installation acceptance.

The historical **0.7.1-fix** packages also lack complete installation acceptance, including runtime preparation, MSI upgrades, and MSIX activation. Historical Python lifecycle results from a disposable Sandbox do not certify either release. See the [published release details](https://github.com/DM10cn/PyDeck/releases/tag/v0.7.1) and [feature status](https://github.com/DM10cn/PyDeck/blob/main/docs/FEATURES.md) for their stated scope.
