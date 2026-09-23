# 🚀 Release workflow

**English** · [简体中文](RELEASING.zh-CN.md) · [🏠 Home](../README.md)

## 🧰 Tools

Use the [development toolchain](DEVELOPMENT.md), PowerShell 7, **WiX 7.0.0**, and Windows SDK **MakeAppx / SignTool**. These are packaging tools, not end-user dependencies. Install the matching WiX UI extension:

```powershell
wix extension add -g WixToolset.UI.wixext/7.0.0
```

WiX 7 requires acceptance of its [OSMF terms](https://docs.firegiant.com/wix/osmf/). Review your eligibility and obligations before accepting; repository scripts do not accept the EULA for you.

## 🔏 Signing

For a local preview certificate:

```powershell
$thumbprint = .\scripts\New-PreviewCertificate.ps1
```

This creates or reuses a code-signing certificate in `CurrentUser\My`, with a non-exportable private key. It does not import a trusted root or export a PFX. Keep the signing account and key available for future updates. A developer's newly generated certificate is not the official release certificate even if its subject has the same text.

The release script signs the app executable, MSI, and MSIX and exports only the public `.cer`. Self-signed previews require an explicit trust step for MSIX users; see [installation](INSTALL.md). Public production signing is future work. Current preview packages have no timestamp.

The native `PyDeck.Launcher.exe` is also signed. An identical signed copy is exported as `PyDeck-Dependencies-<version>-win-x64.exe`. Its filename selects standalone checker behavior: it never launches a sibling app. Include that helper among the release assets. MSI permits installation before GUI runtimes are present; its shortcuts and MSIX activation enter through the installed launcher. MSIX's external framework dependency remains mandatory. End-user requirements and recovery steps are maintained in [INSTALL.md](INSTALL.md).

## 📦 Build

Review and commit the changes first. Package version comes from `PimGui.App.csproj` and must be three numeric components. MSI uses that version; MSIX adds a fourth zero. Increase the installer version for each upgrade, including subsequent previews.

```powershell
.\scripts\Build-Release.ps1 -CertificateThumbprint $thumbprint
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release
```

`Build-Release.ps1` restores locked dependencies, runs core and native checks, publishes without debug symbols or bundled shared runtimes, generates per-user MSI components with stable identities, validates/creates MSIX, and signs the output. It compiles the native launcher and MSI action DLL with static C++ support libraries. Internal files and logs remain under ignored `artifacts/`; only `assets/` is intended for publication.

`-AllowUnsigned` permits local packaging experiments; the resulting MSIX must not be advertised as ready to install. `-SkipChecks` skips core and native tests for packaging-only iteration after those tests have passed; do not use it for the final release build. No script automatically installs dependencies or changes certificate trust.

## 🧪 Validate

`Test-Release.ps1` checks package identity, external runtimes, excluded private/debug files, the MSIX block map and cryptographic signature, signer identity, MSI branding, folder-browser wiring, optional shortcuts, version, per-user scope, and upgrade identity. It inspects packages; it does not install them or exercise the GUI. A self-signed certificate's chain remains untrusted unless the tester explicitly trusts it.

🗂️ The MSI uses a native `IFileOpenDialog` folder picker and stores the chosen folder and shortcut flags in the current user's installer preferences. When passed `-InstallerActionsDirectory`, `Build-Launcher.ps1` also compiles the `/MT` custom-action DLL and verifies system-only imports; `Build-Release.ps1` supplies that argument. The DLL is embedded in the MSI, not shipped as an application dependency.

```powershell
.\scripts\Test-MsiOptions.ps1 -ReleaseDirectory $release
```

This opt-in test installs and removes isolated MSI fixtures with unique product, component, registry, and shortcut identities. It checks all four shortcut combinations, Unicode / spaced paths, repair, upgrade retention, and preservation of unrelated files during uninstall. It does not replace an existing PyDeck installation. Also exercise the real wizard's Browse, selection, cancellation, and Next / Back navigation before release; command-line installation alone does not validate the UI.

Also test MSI install → launch → uninstall, upgrade, downgrade rejection, and missing-prerequisite behavior in a disposable environment. Test signed MSIX installation on a machine where its preview certificate has been deliberately trusted.

For the installed MSI app, pass the chosen directory to `scripts/Smoke-Test.ps1 -BuildDirectory <folder>`. Both GUI smoke paths require PIM and online catalog access. For an installed or development-registered MSIX, use Windows PowerShell for the Appx cmdlets:

```powershell
powershell.exe -NoProfile -File .\scripts\Smoke-Packaged.ps1
```

This activates the actual MSIX identity, then runs the existing read-only GUI checks with isolated preferences. Development registration verifies package activation but does not prove the production certificate-trust installation path. Do not overwrite another installation while testing. Remove only the test installation or registration you created.

Record actual results and remaining limits in [FEATURES.md](FEATURES.md) and its Chinese edition. The steps here are a release procedure, not a claim that every listed acceptance check has passed.

## 🗜️ Source and publication

```powershell
.\scripts\Export-Source.ps1 -ReleaseDirectory $release
```

Export requires a clean Git checkout matching the package build's recorded commit. ZIP and tar.gz archives use `git archive`, so ignored files and signing keys stay out. The script includes bilingual install instructions, the read-only prerequisite check, and SHA-256 checksums for all release assets.

Publish a GitHub **pre-release** with an immutable version tag pointing to that same commit. Upload only the reviewed files in `assets/`, verify the uploaded hashes, and describe tested behavior and remaining limits. Do not upload `build.json`, `work/`, logs, screenshots, PFX files, package caches, or arbitrary contents of `artifacts/`.

📝 Documentation-only corrections can be committed to `main` without rebuilding an unchanged application or incrementing its version. Keep the release tag, signed packages, source archives, and existing checksums intact. Link corrected repository guides from the release notes; attached guides and archived source remain snapshots of the original release commit.

## 🔁 Stable identities

| Item | Value |
| --- | --- |
| MSI UpgradeCode | `D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA` |
| MSI install scope | Current user |
| MSIX package name | `DM10cn.PyDeck` |
| MSIX publisher | `CN=DM10cn` |
| MSIX application ID | `App` |

MSIX filesystem/registry virtualization is disabled for the desktop app so PIM configuration, interpreter files, and the cross-instance lock remain shared with unpackaged tools. Neither format bundles .NET, Windows App Runtime, or PIM. MSI and MSIX are separate installation channels and do not perform automatic cross-format migration.
