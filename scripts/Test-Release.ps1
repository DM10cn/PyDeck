[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
Add-Type -AssemblyName System.IO.Compression.FileSystem
Add-Type -AssemblyName System.Security.Cryptography.Pkcs
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$metadata = Get-Content -LiteralPath (Join-Path $release 'build.json') -Raw | ConvertFrom-Json
$assets = Join-Path $release 'assets'
$msixPath = Join-Path $assets ("PyDeck-{0}-win-x64.msix" -f $metadata.version)
$msiPath = Join-Path $assets ("PyDeck-{0}-win-x64.msi" -f $metadata.version)
$certificate = [Security.Cryptography.X509Certificates.X509Certificate2]::new((Join-Path $assets 'PyDeck-preview.cer'))
if (!$metadata.signed -or $metadata.certificateThumbprint -ne $certificate.Thumbprint) { throw 'Signing metadata does not match the public certificate.' }
if ($certificate.HasPrivateKey) { throw 'A release certificate must not contain a private key.' }
foreach ($path in @($msiPath, $msixPath, (Join-Path $metadata.payload 'PyDeck.exe'))) {
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if (!$signature.SignerCertificate -or $signature.SignerCertificate.Thumbprint -ne $certificate.Thumbprint) { throw "Unexpected signer: $path" }
    if ($signature.Status -eq 'HashMismatch' -or $signature.Status -eq 'NotSigned') { throw "Invalid signature: $path" }
}
$archive = [IO.Compression.ZipFile]::OpenRead($msixPath)
function Read-EntryText([string]$Name) {
    $entry = $archive.GetEntry($Name)
    if (!$entry) { throw "Missing MSIX entry: $Name" }
    $reader = [IO.StreamReader]::new($entry.Open())
    try { $reader.ReadToEnd() } finally { $reader.Dispose() }
}
try {
    [xml]$manifest = Read-EntryText 'AppxManifest.xml'
    if ($manifest.Package.Identity.Name -ne 'DM10cn.PyDeck' -or $manifest.Package.Identity.Publisher -ne $certificate.Subject -or $manifest.Package.Identity.Version -ne ($metadata.version + '.0')) { throw 'Unexpected MSIX identity.' }
    $runtimeDependency = $manifest.Package.Dependencies.PackageDependency
    if ($runtimeDependency.Name -ne 'Microsoft.WindowsAppRuntime.2' -or $runtimeDependency.MinVersion -ne '2.5.1.0') { throw 'Missing external Windows App Runtime dependency.' }
    $configuration = Read-EntryText 'PyDeck.runtimeconfig.json' | ConvertFrom-Json
    if ($configuration.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App') { throw 'The .NET runtime must remain external.' }
    foreach ($entry in $archive.Entries) {
        if ($entry.FullName -match '(?i)(^|/)(coreclr\.dll|hostpolicy\.dll|System\.Private\.CoreLib\.dll|Microsoft\.UI\.Xaml\.dll)$|\.(pfx|p12|pdb|key|log)$') { throw "Forbidden package payload: $($entry.FullName)" }
    }
    [xml]$map = Read-EntryText 'AppxBlockMap.xml'
    foreach ($file in $map.BlockMap.File) {
        $entry = $archive.GetEntry($file.Name.Replace('\', '/'))
        if (!$entry -or $entry.Length -ne [long]$file.Size) { throw "Block map file mismatch: $($file.Name)" }
        $stream = $entry.Open()
        try {
            foreach ($block in @($file.Block)) {
                $bytes = [byte[]]::new(65536)
                $count = 0
                while ($count -lt $bytes.Length -and ($read = $stream.Read($bytes, $count, $bytes.Length - $count)) -gt 0) { $count += $read }
                $hasher = [Security.Cryptography.SHA256]::Create()
                try { $hash = [Convert]::ToBase64String($hasher.ComputeHash($bytes, 0, $count)) } finally { $hasher.Dispose() }
                if ($hash -ne $block.Hash) { throw "MSIX content hash mismatch: $($file.Name)" }
            }
            if ($stream.ReadByte() -ne -1) { throw 'Block map did not cover the complete file.' }
        } finally { $stream.Dispose() }
    }
    $signatureStream = $archive.GetEntry('AppxSignature.p7x').Open()
    $memory = [IO.MemoryStream]::new()
    try { $signatureStream.CopyTo($memory); $signatureBytes = $memory.ToArray() } finally { $signatureStream.Dispose(); $memory.Dispose() }
    if ([Text.Encoding]::ASCII.GetString($signatureBytes, 0, 4) -ne 'PKCX') { throw 'Invalid MSIX signature header.' }
    $cms = [Security.Cryptography.Pkcs.SignedCms]::new()
    $cms.Decode([byte[]]$signatureBytes[4..($signatureBytes.Length - 1)])
    $cms.CheckSignature($true) # Verify cryptography without silently trusting the preview root.
    if ($cms.SignerInfos.Count -ne 1 -or $cms.SignerInfos[0].Certificate.Thumbprint -ne $certificate.Thumbprint) { throw 'Unexpected MSIX CMS signer.' }
    Write-Output "PASS MSIX identity, external runtimes, $(@($map.BlockMap.File).Count) block-mapped files, and cryptographic signature."
} finally { $archive.Dispose() }
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msiPath, 0)
function Read-MsiProperty([string]$Name) {
    $view = $database.OpenView(('SELECT `Value` FROM `Property` WHERE `Property` = ''' + $Name + ''''))
    try { [void]$view.Execute(); $record = $view.Fetch(); if ($record) { $record.StringData(1) } } finally { [void]$view.Close() }
}
try {
    if ((Read-MsiProperty 'ProductVersion') -ne $metadata.version -or (Read-MsiProperty 'ProductName') -ne 'PyDeck') { throw 'Unexpected MSI version or name.' }
    if ((Read-MsiProperty 'UpgradeCode') -ne '{D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA}') { throw 'MSI upgrade identity changed.' }
    if (Read-MsiProperty 'ALLUSERS') { throw 'MSI must remain per-user.' }
    Write-Output 'PASS MSI version, per-user scope, and stable upgrade identity.'
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
Write-Output 'Preview signatures are not publicly trusted. This check does not change certificate trust.'
