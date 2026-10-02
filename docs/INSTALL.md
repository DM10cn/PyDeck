# 📦 Install PyDeck

**English** · [简体中文](INSTALL.zh-CN.md) · [🏠 Project](https://github.com/DM10cn/PyDeck)

📦 **0.7.1-fix · Windows 11 x64**

Download assets from [GitHub Releases](https://github.com/DM10cn/PyDeck/releases). Choose **one** installation format. MSI and MSIX do not upgrade each other; uninstall the previous format before switching. Neither installer removes your Python installations or intentionally deletes your PyDeck preferences.

## 📥 Release downloads

| File | Purpose |
| --- | --- |
| `PyDeck-Setup-0.7.1-fix-win-x64.exe` | Offline runtime preparation and the MSI installation wizard |
| `PyDeck-0.7.1-fix-win-x64.msi` | Standalone current-user installation, with folder, shortcut, and initial interface options |
| `PyDeck-0.7.1-fix-win-x64.msix` | Windows-managed package; prepare its runtimes and certificate trust first |
| `PyDeck-Dependencies-0.7.1-fix-win-x64.exe` | Standalone dependency checklist and official download links |
| `PyDeck-preview.cer` | Public signing certificate for the MSIX trust step |
| `INSTALL.md` / `INSTALL.zh-CN.md` | English and Simplified Chinese installation instructions |
| `Test-Prerequisites.ps1` | Optional read-only prerequisite checklist |
| `SHA256SUMS.txt` | SHA-256 checksums for the uploaded release files |

## 🧰 Offline Setup

**`PyDeck-Setup-0.7.1-fix-win-x64.exe`** embeds the Microsoft runtime installers and the same standalone MSI. Ordinary users need runtimes, **not the .NET SDK, Windows SDK, or Visual Studio**.

1. Open Setup normally, without **Run as administrator**
2. Review the plan: compatible runtimes show **Ready - skip**, missing or incomplete runtimes show **install**
3. Choose the initial **Material 3 Expressive** or **Windows Fluent** interface; existing app preferences take precedence
4. Choose **Continue** to install missing .NET Runtime, Windows App Runtime, and (for MSI) VC++ runtime from the embedded offline installers; Windows may request administrator approval
5. Setup rechecks each dependency, then opens the MSI wizard with its folder, shortcut, and initial interface choices

📦 Runtime EXEs are embedded in Setup, not copied into the installed app folder. The GUI remains framework-dependent. Shared runtimes remain after PyDeck is removed. Setup and the launcher share architecture, version, file, and current-user registration checks. Detection is a readiness check, not a full runtime integrity scan.

⏳ Progress shows the current installer, without invented percentages. Cancel stops after the current installer returns; it does not kill Windows Installer or remove runtimes already installed. Failed rechecks, UAC cancellation, and installer failures prevent the next step. A restart-required result stops before the next installer; Setup never restarts the PC automatically. **Open logs** opens this run's private `%LocalAppData%\PyDeck-Setup-{GUID}` folder, containing `setup.log`, `result.json`, and available vendor/MSI logs. Extracted installers are removed after exit; cleanup failures are logged.

🪟 For MSIX, select **Prepare dependencies only** (or run Setup with `--dependencies-only`). This prepares .NET and registers Windows App Runtime for the desktop user; it does not install the MSI or a separate VC++ redistributable. Install MSIX afterward using the certificate instructions below. MSIX/App Installer can resolve declared MSIX framework dependencies when their source is available; it does **not** run the ordinary .NET 10 EXE installer. An offline `.msix` alone is not a complete prerequisite bundle. Setup does not change certificate trust or install PIM.

🔎 `PyDeck-Setup-0.7.1-fix-win-x64.exe --check` reports readiness without installing anything. `--verify-payloads` extracts and checks embedded payloads, writes a report, then removes the extracted files; it never runs an installer. These checks do not replace clean-machine installation acceptance.

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
| Standalone `PyDeck-Dependencies-0.7.1-fix-win-x64.exe` | Always opens the checklist and download links. **Open PyDeck stays disabled**: this tool does not install or launch the app, even beside an app executable. Close it and return to the installer or installed shortcut |

The **Download** buttons open official Microsoft sources. The tools do not execute installers, elevate themselves, or change certificate trust. The standalone checklist checks the unpackaged prerequisites, including VC++; MSIX installation itself is governed by Windows package-dependency checks.

For MSIX, use the standalone helper before installation if needed: a missing framework can block the package before its bundled launcher can run. The helper cannot bypass MSIX dependencies or certificate trust.

🌏 The app and helper offer English, Simplified Chinese, Traditional Chinese (Taiwan), and Japanese. The app saves its language; the helper starts in English each time. The MSI wizard currently uses English, and the native folder picker uses Windows language settings.

The optional `Test-Prerequisites.ps1` asset is a supplementary read-only checklist of common install locations and PIM on PATH. Its warnings are not the launcher's exact readiness decision: a manager outside PATH can be selected in Settings, and missing PIM does not block GUI startup. Use `PyDeck.Launcher.exe --check` for the native launcher's read-only runtime status.

### 🐍 Connect Python Install Manager

Version **0.7.1-fix** requires PIM **26.3 or later** for installation changes. Older or unrecognized managers are limited to compatible read-only queries; reconnect after upgrading. See [Python management](https://github.com/DM10cn/PyDeck/blob/v0.7.1/docs/MANAGEMENT.md).

Once the GUI runtimes are ready, you can open PyDeck without PIM. Choose **Download Python Install Manager** in the empty state or Settings, install it from Python's official Windows page, then choose **Check again** or **Auto-detect**. You can also select its executable in Settings. These buttons open the download page or reconnect; they do not install PIM themselves, and reconnect does not require restarting PyDeck.

## 🛠️ MSI

1. Download `PyDeck-0.7.1-fix-win-x64.msi`
2. Choose the installation folder, or use **Browse…** to open the Windows folder picker
3. Choose **Create a desktop shortcut** and **Add PyDeck to the Start menu** as needed; only Start menu is selected by default
4. Choose the initial **Material 3 Expressive** (default) or **Windows Fluent** interface
5. Install, then open **PyDeck** from your chosen shortcut

The MSI installs for the current user, with `%LocalAppData%\Programs\PyDeck` as the default. You can enter another writable folder or select one in the native folder picker. Repair and later MSI upgrades retain your folder and shortcut choices. If you disable both shortcuts, open `PyDeck.Launcher.exe` in the chosen folder.

Both interface styles are included in the same package. The installer choice is a first-launch default only: existing PyDeck or legacy app settings take precedence during upgrades and reinstalls. You can change the saved style later in **Settings → Appearance**.

It checks Windows 11 before installation, but allows GUI runtime dependencies to be installed later. Both shortcuts open the native prerequisite launcher, which enters the full app once its runtime checks pass. Keep the entire installed folder together; opening `PyDeck.exe` directly bypasses the prerequisite window.

🧑‍💻 For an unattended current-user installation, the same options are available as MSI properties (`0` = off, `1` = on):

```powershell
msiexec /i PyDeck-0.7.1-fix-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0 PYDECKSTYLE=Material
```

`PYDECKSTYLE` accepts exactly `Material` or `Fluent`; omitting it uses the previous installer choice, or Material for a new installation. It does not overwrite saved app preferences. Use a folder your account can write to. The native folder picker does not require .NET. MSIX uses Windows-managed placement and does not expose these MSI options.

Version 0.7.1-fix still uses a self-signed certificate, so Windows will not recognize it as a publicly trusted publisher. MSI does not require importing the preview certificate to install. Review the download source before choosing to proceed with any Windows prompt.

Newer MSI versions upgrade the same per-user installation and older versions are blocked. Uninstall from **Settings → Apps → Installed apps**. The installer removes its own files and selected shortcuts; it does not uninstall Python or shared runtimes.

## 🪟 MSIX — self-signed package

MSIX requires a trusted signing certificate. This package is **not publicly trusted**. Only trust the publisher if you have verified the files and intend to use PyDeck. The private signing key is never distributed.

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

The final **0.7.1-fix** packages do not have a complete automated regression or installation acceptance result. Final GUI interaction, real restarts, runtime installation, MSI upgrades, and MSIX activation still require manual acceptance. Static package checks establish file/metadata properties only. Historical Python lifecycle results used a disposable Sandbox and do not certify these packages. See the [release details](https://github.com/DM10cn/PyDeck/releases) and [feature status](https://github.com/DM10cn/PyDeck/blob/main/docs/FEATURES.md) for the stated scope.
