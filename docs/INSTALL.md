# 📦 Install PyDeck

**English** · [简体中文](INSTALL.zh-CN.md) · [🏠 Project](https://github.com/DM10cn/PyDeck)

📦 **0.6.0 · Windows 11 x64**

Download assets from [GitHub Releases](https://github.com/DM10cn/PyDeck/releases). Choose **one** installation format. MSI and MSIX do not upgrade each other; uninstall the previous format before switching. Neither installer removes your Python installations or intentionally deletes your PyDeck preferences.

## 📋 Prepare the prerequisites

This page is the reference for end-user dependencies. Build tools belong in the [development guide](https://github.com/DM10cn/PyDeck/blob/main/docs/DEVELOPMENT.md).

| Dependency | When needed | Official source |
| --- | --- | --- |
| Windows 11, x64 | PyDeck, its installers, and the native helper; Windows 10 support is deferred | — |
| .NET Runtime 10.0.x, x64, stable | Full GUI, both MSI and MSIX | [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0); Desktop Runtime 10 / SDK 10 also provide this runtime |
| Windows App Runtime 2.x, x64, minimum 2.5.1.0 | Full GUI, both formats; registered for the current user, with a compatible 2.x update accepted | [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable, x64 | MSI / unpackaged GUI; Windows resolves framework dependencies when installing MSIX | [Microsoft's supported downloads](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) |
| Python Install Manager | Python management operations; the GUI can open without it | [Official Python Windows downloads](https://www.python.org/downloads/windows/) |

🔌 PyDeck does not bundle or automatically download/install these shared runtimes or PIM. Its native helper and MSI folder browser statically link their C++ support libraries, so those small components themselves do not require .NET, Windows App Runtime, WebView2, or a separately installed VC++ runtime. Prepare the GUI dependencies and PIM before using offline Python bundles.

### 🧰 Two dependency tools

| Entry | Behavior |
| --- | --- |
| Installed `PyDeck.Launcher.exe` / PyDeck shortcut | Starts the GUI when dependencies are ready; otherwise shows the dependency window. Install missing packages yourself, choose **Check again**, then **Open PyDeck** |
| Standalone `PyDeck-Dependencies-0.6.0-win-x64.exe` | Always opens the checklist and download links. **Open PyDeck stays disabled**: this tool does not install or launch the app, even beside an app executable. Close it and return to the installer or installed shortcut |

The **Download** buttons open official Microsoft sources. The tools do not execute installers, elevate themselves, or change certificate trust. The standalone checklist checks the unpackaged prerequisites, including VC++; MSIX installation itself is governed by Windows package-dependency checks.

For MSIX, use the standalone helper before installation if needed: a missing framework can block the package before its bundled launcher can run. The helper cannot bypass MSIX dependencies or certificate trust.

🌏 The app and helper offer English, Simplified Chinese, Traditional Chinese (Taiwan), and Japanese. The app saves its language; the helper starts in English each time. The MSI wizard currently uses English, and the native folder picker uses Windows language settings.

The optional `Test-Prerequisites.ps1` asset is a supplementary read-only checklist of common install locations and PIM on PATH. Its warnings are not the launcher's exact readiness decision: a manager outside PATH can be selected in Settings, and missing PIM does not block GUI startup. Use `PyDeck.Launcher.exe --check` for the native launcher's read-only runtime status.

### 🐍 Connect Python Install Manager

Version **0.6.0** requires PIM **26.3 or later** for installation changes. Older or unrecognized managers are limited to compatible read-only queries; reconnect after upgrading. See [Python management](https://github.com/DM10cn/PyDeck/blob/v0.6.0/docs/MANAGEMENT.md). Published 0.5.1 installers predate this capability gate.

Once the GUI runtimes are ready, you can open PyDeck without PIM. Choose **Download Python Install Manager** in the empty state or Settings, install it from Python's official Windows page, then choose **Check again** or **Auto-detect**. You can also select its executable in Settings. These buttons open the download page or reconnect; they do not install PIM themselves, and reconnect does not require restarting PyDeck.

## 🛠️ MSI

1. Download `PyDeck-0.6.0-win-x64.msi`
2. Choose the installation folder, or use **Browse…** to open the Windows folder picker
3. Choose **Create a desktop shortcut** and **Add PyDeck to the Start menu** as needed; only Start menu is selected by default
4. Install, then open **PyDeck** from your chosen shortcut

The MSI installs for the current user, with `%LocalAppData%\Programs\PyDeck` as the default. You can enter another writable folder or select one in the native folder picker. Repair and later MSI upgrades retain your folder and shortcut choices. If you disable both shortcuts, open `PyDeck.Launcher.exe` in the chosen folder.

It checks Windows 11 before installation, but allows GUI runtime dependencies to be installed later. Both shortcuts open the native prerequisite launcher, which enters the full app once its runtime checks pass. Keep the entire installed folder together; opening `PyDeck.exe` directly bypasses the prerequisite window.

🧑‍💻 For an unattended current-user installation, the same options are available as MSI properties (`0` = off, `1` = on):

```powershell
msiexec /i PyDeck-0.6.0-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0
```

Use a folder your account can write to. The native folder picker does not require .NET. MSIX uses Windows-managed placement and does not expose these MSI options.

Version 0.6.0 still uses a self-signed certificate, so Windows will not recognize it as a publicly trusted publisher. MSI does not require importing the preview certificate to install. Review the download source before choosing to proceed with any Windows prompt.

Newer MSI versions upgrade the same per-user installation and older versions are blocked. Uninstall from **Settings → Apps → Installed apps**. The installer removes its own files and selected shortcuts; it does not uninstall Python or shared runtimes.

## 🪟 MSIX — self-signed package

MSIX requires a trusted signing certificate. This package is **not publicly trusted**. Only trust the publisher if you have verified the files and intend to use PyDeck. The private signing key is never distributed.

1. Download `PyDeck-0.6.0-win-x64.msix`, `PyDeck-preview.cer`, and `SHA256SUMS.txt` from the same release
2. Compare file hashes with `SHA256SUMS.txt`, using `Get-FileHash -Algorithm SHA256`
3. Inspect the certificate: publisher **CN=DM10cn**, thumbprint **3912817E181E5EA9AF0DED6BC51E332E99BBDD16**
4. Import the public certificate into **Local Computer → Trusted People**, then open the MSIX

An administrator can perform the explicit trust step in PowerShell:

```powershell
Import-Certificate -FilePath .\PyDeck-preview.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Then install the package as your normal desktop user:

```powershell
Add-AppxPackage -Path .\PyDeck-0.6.0-win-x64.msix
```

Do **not** import this certificate into Trusted Root Certification Authorities. PyDeck's scripts do not import certificates or change system trust automatically. This preview certificate expires on **2028-09-23**; the packages are not timestamped for long-term distribution.

MSIX declares a dependency on `Microsoft.WindowsAppRuntime.2`, minimum `2.5.1.0`. A missing dependency must be installed first. .NET 10 remains a separate machine prerequisite. The package is a full-trust desktop application and keeps PIM configuration and Python files outside MSIX virtualization.

🧹 After removing all packages signed by this preview certificate, you may remove the certificate from Trusted People using `certlm.msc`. Keep it if you still need those packages.

## 🔁 Updates and data

MSI uses a stable upgrade identity; MSIX uses the `DM10cn.PyDeck` package name and `CN=DM10cn` publisher. Keep those identities for compatible future updates. Replacing the publisher or switching installer formats requires a migration plan.

Settings remain under `%LocalAppData%\PyDeck`. Python installations, PIM configuration, and shared runtimes are independent of PyDeck's installer. Close PyDeck before updating or removing it, and let any Python operation finish first.

## 🗜️ Source archives

Use **Source code (zip)** or **Source code (tar.gz)** under the release's Assets section. GitHub generates these archives from the release tag; PyDeck does not upload duplicate source packages. `SHA256SUMS.txt` covers the uploaded assets, not GitHub-generated archives. Source and attached guides are snapshots; later documentation corrections live in the [current repository guide](https://github.com/DM10cn/PyDeck/blob/main/docs/INSTALL.md) without replacing the tagged source or signed packages.

## 🧪 Validation limits

Stable release status does not imply coverage of every environment. Local installation and activation checks do not replace clean-machine GUI, accessibility, or every DPI configuration. Python lifecycle acceptance runs in a disposable Sandbox. See the repository's [feature status](https://github.com/DM10cn/PyDeck/blob/main/docs/FEATURES.md).
