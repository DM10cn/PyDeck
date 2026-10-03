# 🚀 Release workflow

**English** · [简体中文](RELEASING.zh-CN.md) · [🏠 Home](../README.md)

## 🧰 Tools and version

Use the [development toolchain](DEVELOPMENT.md), PowerShell 7, WiX 7.0.0 and Windows SDK MakeAppx / SignTool. Install `WixToolset.UI.wixext/7.0.0` with `wix extension add -g`. Review the [WiX OSMF terms](https://docs.firegiant.com/wix/osmf/); scripts do not accept them on your behalf.

Commit the source before building. `Version` in `PimGui.App.csproj` supplies the three-part MSI version; optional `InformationalVersion` supplies display/asset names and must begin with that version. MSIX appends `.0`. Increase the numeric version for normal upgrades: a label change alone does not create an upgrade.

## 📦 MSI and offline Setup

The current distribution is unsigned MSI/Setup. Select that route explicitly:

```powershell
.\scripts\Build-Release.ps1 -AllowUnsigned -MsiOnly
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release -AllowUnsigned
```

The build restores locked packages, checks the native color engine and core code, publishes the app, checks/builds the launcher and MSI actions, creates MSI and builds/checks Setup. Matching native color hashes may reuse a previously checked build. `-SkipChecks` skips regression runs and is recorded in `build.json`; compilation alone does not establish those results.

Setup embeds the MSI and unmodified Microsoft runtime installers. `packaging/setup/prerequisites.json` pins their URLs, lengths and SHA-256 values; the build also checks Microsoft signatures. Downloads are cached in ignored `.local/setup-prerequisites`. Review upstream changes before changing pins.

To rebuild Setup from an existing unsigned matching-version MSI:

```powershell
.\scripts\Build-Setup.ps1 -MsiPath <msi> -OutputDirectory .\artifacts\setup-test -AllowUnsignedMsi -Checks
.\scripts\Test-Setup.ps1 -SetupPath <setup-exe> -MsiPath <msi>
```

`Test-Release` requires every expected file, checks MSI identity and option wiring, and verifies Setup's embedded installers without installing them. Unsigned metadata requires `-AllowUnsigned`. `msiOnly` controls MSIX expectations; older metadata without that field expects both formats. Missing required packages fail the check.

## 🔏 Optional signed packages and MSIX

```powershell
$thumbprint = .\scripts\New-PreviewCertificate.ps1
.\scripts\Build-Release.ps1 -CertificateThumbprint $thumbprint
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release
```

Omitting `-MsiOnly` also builds MSIX. Signing covers app, launcher, packages and Setup; only the public certificate is exported. The preview key is non-exportable in `CurrentUser\My`; creating it does not establish public trust. Scripts do not change certificate trust. The MSIX publisher must match the certificate subject.

For signed metadata, `Test-Release` still requires the matching certificate and signers even with `-AllowUnsigned`. MSIX checks include identity, external-runtime declarations, excluded files, block hashes and the CMS signature. Unsigned MSIX can be inspected but is not ready for normal installation. Historical preview trust instructions are [archived](archive/INSTALL-MSIX.md).

## 🧪 Installation acceptance

Package inspection does not exercise installation or the GUI. On disposable Windows machines, test missing runtimes, UAC cancellation, restart handling, offline installation, repair, upgrade, downgrade rejection and removal. Preserve actual results under ignored artifacts; do not carry historical pass counts into new builds.

- `Test-MsiOptions.ps1 -ReleaseDirectory <release>` installs/removes isolated fixtures for folder, shortcut, style, repair and rollback checks.
- `Test-MsiUpgrade.ps1 -ReleaseDirectory <new> -PreviousReleaseDirectory <previous>` tests real package replacement and refuses an existing PyDeck installation.
- `Smoke-Test.ps1 -BuildDirectory <installed-folder>` checks MSI GUI behavior; Windows PowerShell `Smoke-Packaged.ps1` checks MSIX activation. Both need their documented prerequisites.

These opt-in checks can install/remove test products. Run only in the intended test environment. General core, native and GUI procedures are in [Development](DEVELOPMENT.md); snapshots are in the [archive](archive/README.md).

## 🗜️ Publication

`Export-Source.ps1 -ReleaseDirectory <release>` prepares bilingual install guides, the prerequisite script and hashes. It requires a clean checkout matching `build.json`'s source commit. Review `assets/` before upload: publish only the intended packages, guides and checksum manifest. Use GitHub's automatic source ZIP/TAR links; do not upload duplicate source archives, logs, caches, keys or `build.json`.

Tag the exact build commit as `v<version>`. Release descriptions use English and Simplified Chinese sections with concise feature bullets and a few emoji, approximately 160–200 English words and 160–200 Chinese characters per language. Keep development check records out of the product description. The repository README describes current capabilities; GitHub Releases holds version changes.

Documentation/tooling maintenance can go to `main` without rebuilding unchanged published binaries or moving their tag. Attached documents remain snapshots unless deliberately refreshed; after an attachment change, regenerate its checksum entry and verify remote asset hashes.

## 🔁 Stable identities

| Item | Value |
| --- | --- |
| MSI UpgradeCode | `D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA` |
| MSI scope | Current user |
| MSIX package / publisher / app ID | `DM10cn.PyDeck` / `CN=DM10cn` / `App` |

Keep stable MSI component identities and transactional old-product removal after `InstallInitialize`. Each upgrade is a full MSI. Preserve app preferences and venv records outside its payload. MSI/MSIX do not migrate across formats automatically; neither standalone package bundles shared runtimes. Setup supplies separate runtime installers.
