# 📦 Install PyDeck

**English** · [简体中文](INSTALL.zh-CN.md) · [🏠 Project](https://github.com/DM10cn/PyDeck)

🚧 **0.5.1 preview · Windows 11 x64**

Download assets from [GitHub Releases](https://github.com/DM10cn/PyDeck/releases). Choose **one** installation format. MSI and MSIX do not upgrade each other; uninstall the previous format before switching. Neither installer removes your Python installations or intentionally deletes your PyDeck preferences.

## 📋 Prepare the prerequisites

🧰 The native prerequisite window runs without .NET, Windows App Runtime, WebView2, or a separately installed VC++ runtime. Missing dependencies have **Download** buttons that open official Microsoft sources. Install the x64 packages, choose **Check again**, then **Open PyDeck**. The window does not run installers, elevate itself, or change certificate trust. English is the default, with Simplified Chinese, Traditional Chinese (Taiwan), and Japanese available.

For MSIX, run the standalone `PyDeck-Dependencies-0.5.1-win-x64.exe` before installing the package if needed. Windows can block MSIX installation on a missing framework dependency before any in-package window can run. The standalone tool stays outside that dependency chain; after checking, return to the MSIX installer.

| Dependency | Official source |
| --- | --- |
| .NET Runtime 10, x64 | [.NET 10 downloads](https://dotnet.microsoft.com/download/dotnet/10.0) — choose **.NET Runtime**, or Desktop Runtime / SDK |
| Windows App Runtime 2.5.1 or newer compatible 2.x, x64 | [Windows App SDK downloads](https://learn.microsoft.com/windows/apps/windows-app-sdk/downloads) |
| Visual C++ v14 Redistributable, x64 | [Microsoft's supported downloads](https://learn.microsoft.com/cpp/windows/latest-supported-vc-redist) — required by the unpackaged/MSI runtime path |
| Python Install Manager | [Python on Windows](https://docs.python.org/3/using/windows.html) |

🔌 **No .NET or Windows App Runtime is bundled or automatically downloaded by PyDeck.** Python Install Manager is also separate. Install the prerequisites while online before using offline Python bundles.

The optional `Test-Prerequisites.ps1` release asset performs read-only checks. If your manager is not on PATH, select it in PyDeck Settings.

## 🛠️ MSI

1. Download `PyDeck-0.5.1-win-x64.msi`
2. Choose the installation folder, or use **Browse…** to open the Windows folder picker
3. Choose **Create a desktop shortcut** and **Add PyDeck to the Start menu** as needed; only Start menu is selected by default
4. Install, then open **PyDeck** from your chosen shortcut

The MSI installs for the current user, with `%LocalAppData%\Programs\PyDeck` as the default. You can enter another writable folder or select one in the native folder picker. Repair and later MSI upgrades retain your folder and shortcut choices. If you disable both shortcuts, open `PyDeck.Launcher.exe` in the chosen folder.

It checks Windows 11 before installation, but allows .NET to be installed later. Both shortcuts open the native prerequisite launcher, which enters the full app once prerequisites are detected. Keep the entire installed folder together; opening `PyDeck.exe` directly bypasses the prerequisite window.

🧑‍💻 For an unattended current-user installation, the same options are available as MSI properties (`0` = off, `1` = on):

```powershell
msiexec /i PyDeck-0.5.1-win-x64.msi /qn INSTALLFOLDER="D:\Apps\PyDeck" DESKTOPSHORTCUT=1 STARTMENUSHORTCUT=0
```

Use a folder your account can write to. The native folder picker does not require .NET. MSIX uses Windows-managed placement and does not expose these MSI options.

🐍 Missing Python Install Manager does not block the GUI. Choose **Download Python Install Manager** in the empty state or Settings, install the manager from Python's official Windows page, then choose **Check again** or **Auto-detect**. PyDeck reconnects without restarting. These actions open the official download page; they do not silently install PIM or Python.

The preview uses a self-signed certificate, so Windows will not recognize it as a publicly trusted publisher. MSI does not require importing the preview certificate to install. Review the download source before choosing to proceed with any Windows prompt.

Newer MSI versions upgrade the same per-user installation and older versions are blocked. Uninstall from **Settings → Apps → Installed apps**. The installer removes its own files and shortcut; it does not uninstall Python or shared runtimes.

## 🪟 MSIX — self-signed preview

MSIX requires a trusted signing certificate. This preview is **not publicly trusted**. Only trust the preview publisher if you have verified the files and intend to test PyDeck. The private signing key is never distributed.

1. Download `PyDeck-0.5.1-win-x64.msix`, `PyDeck-preview.cer`, and `SHA256SUMS.txt` from the same release
2. Compare file hashes with `SHA256SUMS.txt`, using `Get-FileHash -Algorithm SHA256`
3. Inspect the certificate: publisher **CN=DM10cn**, thumbprint **3912817E181E5EA9AF0DED6BC51E332E99BBDD16**
4. Import the public certificate into **Local Computer → Trusted People**, then open the MSIX

An administrator can perform the explicit trust step in PowerShell:

```powershell
Import-Certificate -FilePath .\PyDeck-preview.cer -CertStoreLocation Cert:\LocalMachine\TrustedPeople
```

Then install the package as your normal desktop user:

```powershell
Add-AppxPackage -Path .\PyDeck-0.5.1-win-x64.msix
```

Do **not** import this certificate into Trusted Root Certification Authorities. PyDeck's scripts do not import certificates or change system trust automatically. This preview certificate expires on **2028-09-23**; the packages are not timestamped for long-term distribution.

MSIX declares a dependency on `Microsoft.WindowsAppRuntime.2`, minimum `2.5.1.0`. A missing dependency must be installed first. .NET 10 remains a separate machine prerequisite. The package is a full-trust desktop application and keeps PIM configuration and Python files outside MSIX virtualization.

🧹 After removing all packages signed by this preview certificate, you may remove the certificate from Trusted People using `certlm.msc`. Keep it if you still need those packages.

## 🔁 Updates and data

MSI uses a stable upgrade identity; MSIX uses the `DM10cn.PyDeck` package name and `CN=DM10cn` publisher. Keep those identities for compatible future updates. Replacing the publisher or switching installer formats requires a migration plan.

Settings remain under `%LocalAppData%\PyDeck`. Python installations, PIM configuration, and shared runtimes are independent of PyDeck's installer. Close PyDeck before updating or removing it, and let any Python operation finish first.

## 🗜️ Source archives

`PyDeck-0.5.1-source.zip` and `PyDeck-0.5.1-source.tar.gz` contain only files from the release commit. Build output, logs, local settings, package caches, and signing keys are excluded. The GitHub-generated source links are also available.

## 🧪 Preview limits

This release is a preview. Local installation and activation checks do not replace clean-machine, accessibility, every DPI configuration, or complete Python install/update/uninstall acceptance testing. See the repository's [feature status](https://github.com/DM10cn/PyDeck/blob/main/docs/FEATURES.md).
