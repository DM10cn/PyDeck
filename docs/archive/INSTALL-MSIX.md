# 🪟 Historical 0.7.1-fix MSIX — self-signed package

**English** · [简体中文](INSTALL-MSIX.zh-CN.md) · [Current installation](../INSTALL.md)

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
