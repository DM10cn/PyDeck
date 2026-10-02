# 🚀 Release workflow

**English** · [简体中文](RELEASING.zh-CN.md) · [🏠 Home](../README.md)

## 📋 0.7.1-fix validation scope

**2026-10-03 · Asia/Taipei:** display/download version **0.7.1-fix**, MSI **0.7.1**, MSIX **0.7.1.0**, tag **v0.7.1**. This release uses compilation, packaging and static package inspection; automated regression scripts are not run. GUI interaction, installation, upgrade/rollback and MSIX certificate-trust installation remain pending manual verification. Record actual build/signing/package results without carrying forward historical test counts. The commands below describe the general development and release workflow.

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

The release script signs the app executable, MSI, and MSIX and exports only the public `.cer`. Self-signed packages, including 0.6.0, require an explicit trust step for MSIX users; see [installation](INSTALL.md). Public production signing is future work. Current packages have no timestamp. Stable release status does not make the certificate publicly trusted; `PyDeck-preview.cer` keeps its historical filename and signing identity.

The native `PyDeck.Launcher.exe` is also signed. An identical signed copy is exported as `PyDeck-Dependencies-<version>-win-x64.exe`. Its filename selects standalone checker behavior: it never launches a sibling app. Include that helper among the release assets. MSI permits installation before GUI runtimes are present; its shortcuts and MSIX activation enter through the installed launcher. MSIX's external framework dependency remains mandatory. End-user requirements and recovery steps are maintained in [INSTALL.md](INSTALL.md).

## 📦 Build

An optional `InformationalVersion` supplies the app display label and asset filenames (for example, `0.7.1-fix`). It must start with the numeric `Version`. Installer identity and the stable Git tag remain numeric; the build records both values. A display-label change alone is not an installer upgrade: subsequent releases must increase `Version`.

Review and commit the changes first. Package version comes from `PimGui.App.csproj` and must be three numeric components. MSI uses that version; MSIX adds a fourth zero. Increase the installer version for each upgrade, including subsequent previews.

```powershell
.\scripts\Build-Release.ps1 -CertificateThumbprint $thumbprint
$release = (Get-Content .\artifacts\latest-release.txt -Raw).Trim()
.\scripts\Test-Release.ps1 -ReleaseDirectory $release
```

`Build-Release.ps1` restores locked dependencies, runs core and native checks, publishes without debug symbols or bundled shared runtimes, generates per-user MSI components with stable identities, validates/creates MSIX, and signs the output. It compiles the native launcher and MSI action DLL with static C++ support libraries. Internal files and logs remain under ignored `artifacts/`; only `assets/` is intended for publication.

📦 **From 0.7.1-fix:** it also creates and signs `PyDeck-Setup-<version>-win-x64.exe`, containing the already-signed MSI and unmodified Microsoft runtime installers. This is an additional offline entry point; retain the standalone MSI and MSIX. The app remains framework-dependent. Existing releases are unchanged.

`packaging/setup/prerequisites.json` pins the official download URLs, lengths and SHA-256 values. `Build-Setup.ps1` downloads missing files into ignored `.local/setup-prerequisites`, verifies their hashes and Microsoft signatures, then embeds them. A changed download fails the build; review the new official version and its signature before updating the pin. Do not commit cached installers. To build and verify Setup against an existing matching-version MSI locally:

```powershell
.\scripts\Build-Setup.ps1 -MsiPath <signed-msi> -OutputDirectory .\artifacts\setup-test -Checks
.\scripts\Test-Setup.ps1 -SetupPath <setup-exe> -MsiPath <signed-msi>
```

Standalone `Build-Setup.ps1` produces an unsigned outer EXE for local checks; release signing occurs in `Build-Release.ps1`. `-AllowUnsignedMsi` is only for local fixtures. Native Setup is statically linked and shares the launcher's prerequisite detector. It runs dependencies sequentially before opening MSI, outside the MSI transaction; the existing folder and shortcut choices remain available.

`-AllowUnsigned` permits local packaging experiments; the resulting MSIX must not be advertised as ready to install. `-SkipChecks` selects compilation and packaging without the core/native test runs. When using it, disclose the skipped validation in the release notes; build success does not establish passing regression or installation tests. No script automatically installs dependencies or changes certificate trust.

## 🧪 Validate

`Test-Release.ps1` checks package identity, external runtimes, excluded private/debug files, the MSIX block map and cryptographic signature, signer identity, MSI branding, folder-browser wiring, optional shortcuts, version, per-user scope, and upgrade identity. It inspects packages; it does not install them or exercise the GUI. A self-signed certificate's chain remains untrusted unless the tester explicitly trusts it.

For builds containing Setup, it also verifies the outer signer and invokes `Test-Setup.ps1`. That check extracts and hashes all four embedded installers, compares them with the reviewed pins and standalone MSI, and retains its report without running any installer. Native checks cover dependency combinations, post-install rechecks, cancellation, restart results, tampered payloads, file locking and four-language controls. Before release, separately test actual missing-runtime installation, UAC cancellation, standard-user registration, failure/retry, reboot/resume, offline use, MSI upgrade and dependencies-only MSIX preparation on disposable machines. Automated state tests do not prove those installation paths.

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

The script retains its historical name but now prepares only bilingual install instructions, the read-only prerequisite check, and SHA-256 checksums for uploaded release assets. It requires a clean Git checkout matching the package build's recorded commit and rejects leftover manually generated source archives in `assets/`. Use GitHub's automatic **Source code (zip)** / **Source code (tar.gz)** links; do not upload duplicate archives or include generated source downloads in `SHA256SUMS.txt`.

Starting with **0.6.0**, publish a normal GitHub release with an immutable `v<version>` tag pointing to that same commit, and mark it Latest. Historical 0.4.0 / 0.5.1 entries use `-beta` release tags and remain ordinary releases; they are not Latest. Original tags may remain as compatibility references to the unchanged commits. Upload only the reviewed files in `assets/`, verify the uploaded hashes, and describe tested behavior and remaining limits. Do not upload `build.json`, `work/`, logs, screenshots, PFX files, package caches, or arbitrary contents of `artifacts/`.

📝 Documentation-only corrections can be committed to `main` without rebuilding an unchanged application or incrementing its version. Keep the release tag and signed packages intact. Link corrected repository guides from the release notes; attached guides and tagged source remain snapshots of the original release commit. If obsolete attachments are explicitly removed, update the checksum manifest to list only retained assets without changing their hashes.

## 🔁 Stable identities

| Item | Value |
| --- | --- |
| MSI UpgradeCode | `D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA` |
| MSI install scope | Current user |
| MSIX package name | `DM10cn.PyDeck` |
| MSIX publisher | `CN=DM10cn` |
| MSIX application ID | `App` |

MSIX filesystem/registry virtualization is disabled for the desktop app so PIM configuration, interpreter files, and the cross-instance lock remain shared with unpackaged tools. Neither standalone package bundles .NET, Windows App Runtime, or PIM. The new Setup entry point includes shared-runtime installers, not self-contained application runtimes. MSI and MSIX are separate installation channels and do not perform automatic cross-format migration.

## 🔁 MSI replacement upgrades — from 0.6.1

Every release ships a full MSI, not an MSP/binary-delta patch. Running a newer MSI upgrades in one installation transaction without requiring manual removal first. Keep the UpgradeCode, per-user scope and stable component identities; schedule old-product removal after InstallInitialize so a failed upgrade can restore it. Increment the three-part version for every release.

For automated MSI acceptance, run `Test-MsiOptions.ps1`. Its isolated fixtures verify retained folder/shortcuts, removed obsolete installer-owned files, preserved unrelated user files, and rollback after an intentional upgrade failure. Also validate the previous published MSI → new MSI on a disposable installation, or mark that path pending when it has not been exercised. App preferences and venv records remain outside the MSI payload. This policy applies to MSI only; MSIX remains managed by Windows. Do not call full-package replacement a reduced-size delta download.

Use `Test-MsiUpgrade.ps1 -ReleaseDirectory <new> -PreviousReleaseDirectory <previous>` to compare installed file hashes and preserve application data / installer choices across the previous published MSI. It refuses to overwrite an existing PyDeck installation. `-SkipGui` permits installer-only checks in a disposable system without WinUI prerequisites; run GUI smoke checks separately and report that boundary.
