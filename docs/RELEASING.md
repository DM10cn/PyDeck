# 🚀 Release workflow

**English** · [简体中文](RELEASING.zh-CN.md) · [🏠 Home](../README.md)

## 🧰 Tools

Use the [development toolchain](DEVELOPMENT.md), PowerShell 7, **WiX 7.0.0**, and Windows SDK **MakeAppx / SignTool**. Install WiX's matching extensions:

```powershell
wix extension add -g WixToolset.UI.wixext/7.0.0
wix extension add -g WixToolset.Netfx.wixext/7.0.0
```

WiX 7 requires acceptance of its [OSMF terms](https://docs.firegiant.com/wix/osmf/). Review your eligibility and obligations before accepting; repository scripts do not accept the EULA for you.

## 🔏 Signing

For a local preview certificate:

```powershell
$thumbprint = .\scripts\New-PreviewCertificate.ps1
```

This creates or reuses a code-signing certificate in `CurrentUser\My`, with a non-exportable private key. It does not import a trusted root or export a PFX. Keep the signing account and key available for future updates. A developer's newly generated certificate is not the official release certificate even if its subject has the same text.

The release script signs the app executable, MSI, and MSIX and exports only the public `.cer`. Self-signed previews require an explicit trust step for MSIX users; see [installation](INSTALL.md). Public production signing is future work. Current preview packages have no timestamp.

## 📦 Build

Review and commit the changes first. Package version comes from `PimGui.App.csproj` and must be three numeric components. MSI uses that version; MSIX adds a fourth zero. Increase the installer version for each upgrade, including subsequent previews.

```powershell
.\scripts\Build-Release.ps1 -CertificateThumbprint $thumbprint
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release
```

`Build-Release.ps1` restores locked dependencies, runs core checks, publishes without debug symbols or bundled runtimes, generates per-user MSI components with stable identities, validates/creates MSIX, and signs the output. Internal files and logs remain under ignored `artifacts/`; only `assets/` is intended for publication.

`-AllowUnsigned` permits local packaging experiments; the resulting MSIX must not be advertised as ready to install. `-SkipChecks` is for packaging-only iteration after core checks have passed, not the final release build. No script automatically installs dependencies or changes certificate trust.

## 🧪 Validate

`Test-Release.ps1` checks package identity, external runtimes, excluded private/debug files, the MSIX block map and cryptographic signature, signer identity, MSI version, per-user scope, and upgrade identity. A self-signed certificate's chain remains untrusted unless the tester explicitly trusts it.

Also test MSI install → launch → uninstall, upgrade, downgrade rejection, and missing-prerequisite behavior in a disposable environment. Test signed MSIX installation on a machine where its preview certificate has been deliberately trusted.

For the installed MSI app, pass its directory to `scripts/Smoke-Test.ps1`. For an installed or development-registered MSIX:

```powershell
.\scripts\Smoke-Packaged.ps1
```

This activates the actual MSIX identity, then runs the existing read-only GUI checks with isolated preferences. Development registration verifies package activation but does not prove the production certificate-trust installation path. Do not overwrite another installation while testing. Remove only the test installation or registration you created.

## 🗜️ Source and publication

```powershell
.\scripts\Export-Source.ps1 -ReleaseDirectory $release
```

Export requires a clean Git checkout matching the package build's recorded commit. ZIP and tar.gz archives use `git archive`, so ignored files and signing keys stay out. The script includes bilingual install instructions, the read-only prerequisite check, and SHA-256 checksums for all release assets.

Publish a GitHub **pre-release** with an immutable version tag pointing to that same commit. Upload only the reviewed files in `assets/`, verify the uploaded hashes, and describe tested behavior and remaining limits. Do not upload `build.json`, `work/`, logs, screenshots, PFX files, package caches, or arbitrary contents of `artifacts/`.

## 🔁 Stable identities

| Item | Value |
| --- | --- |
| MSI UpgradeCode | `D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA` |
| MSI install scope | Current user |
| MSIX package name | `DM10cn.PyDeck` |
| MSIX publisher | `CN=DM10cn` |
| MSIX application ID | `App` |

MSIX filesystem/registry virtualization is disabled for the desktop app so PIM configuration, interpreter files, and the cross-instance lock remain shared with unpackaged tools. Neither format bundles .NET, Windows App Runtime, or PIM. MSI and MSIX are separate installation channels and do not perform automatic cross-format migration.
