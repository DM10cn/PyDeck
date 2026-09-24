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
foreach ($path in @($msiPath, $msixPath, (Join-Path $metadata.payload 'PyDeck.exe'), (Join-Path $metadata.payload 'PyDeck.Launcher.exe'), (Join-Path $assets ("PyDeck-Dependencies-{0}-win-x64.exe" -f $metadata.version)))) {
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
    if ($manifest.Package.Applications.Application.Executable -ne 'PyDeck.Launcher.exe') { throw 'MSIX must start through the native dependency launcher.' }
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
    $sequences = @{}
    foreach ($action in @('InstallInitialize', 'RemoveExistingProducts', 'InstallFiles')) {
        $view = $database.OpenView(('SELECT `Sequence` FROM `InstallExecuteSequence` WHERE `Action` = ''' + $action + ''''))
        try { [void]$view.Execute(); $row = $view.Fetch(); if (!$row) { throw "Missing upgrade action: $action" }; $sequences[$action] = $row.IntegerData(1) }
        finally { [void]$view.Close() }
    }
    if ($sequences.RemoveExistingProducts -le $sequences.InstallInitialize -or $sequences.RemoveExistingProducts -ge $sequences.InstallFiles) { throw 'MSI must use transactional replacement upgrades.' }
    foreach ($shortcut in @('LaunchPyDeck', 'DesktopPyDeck')) {
        $view = $database.OpenView(('SELECT `Name`, `Target` FROM `Shortcut` WHERE `Shortcut` = ''' + $shortcut + ''''))
        try {
            [void]$view.Execute(); $record = $view.Fetch()
            if (!$record -or $record.StringData(1) -notmatch '(^|\|)PyDeck$' -or $record.StringData(2) -ne '[INSTALLFOLDER]PyDeck.Launcher.exe') { throw 'MSI shortcut has unexpected branding or bypasses the native launcher.' }
        } finally { [void]$view.Close() }
    }
    $expectations = @(
        @('SELECT `Target` FROM `CustomAction` WHERE `Action` = ''PyDeckBrowseInstallFolder''', 'BrowseInstallFolder'),
        @('SELECT `Property` FROM `Control` WHERE `Dialog_` = ''PyDeckInstallOptionsDlg'' AND `Control` = ''Folder''', 'INSTALLFOLDER'),
        @('SELECT `Type` FROM `Control` WHERE `Dialog_` = ''PyDeckInstallOptionsDlg'' AND `Control` = ''Folder''', 'PathEdit'),
        @('SELECT `Argument` FROM `ControlEvent` WHERE `Dialog_` = ''PyDeckInstallOptionsDlg'' AND `Control_` = ''Browse'' AND `Event` = ''DoAction''', 'PyDeckBrowseInstallFolder'),
        @('SELECT `Argument` FROM `ControlEvent` WHERE `Dialog_` = ''PyDeckInstallOptionsDlg'' AND `Control_` = ''Browse'' AND `Event` = ''[INSTALLFOLDER]''', '[INSTALLFOLDER]'),
        @('SELECT `Condition` FROM `Component` WHERE `Component` = ''DesktopShortcut''', 'DESKTOPSHORTCUT = "1"'),
        @('SELECT `Condition` FROM `Component` WHERE `Component` = ''StartMenuShortcut''', 'STARTMENUSHORTCUT = "1"')
    )
    foreach ($expectation in $expectations) {
        $view = $database.OpenView($expectation[0])
        try { [void]$view.Execute(); $record = $view.Fetch(); if (!$record -or $record.StringData(1) -ne $expectation[1]) { throw "MSI installation option wiring mismatch: $($expectation[1])" } }
        finally { [void]$view.Close() }
    }
    $view = $database.OpenView('SELECT `Condition` FROM `LaunchCondition`')
    try {
        [void]$view.Execute()
        while ($record = $view.Fetch()) { if ($record.StringData(1) -match '(?i)DOTNET|NET10') { throw 'MSI must allow installation before .NET is installed.' } }
    } finally { [void]$view.Close() }
    Write-Output 'PASS MSI PyDeck branding, native folder browser, optional shortcuts, version, per-user scope, and stable upgrade identity.'
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
Write-Output 'Preview signatures are not publicly trusted. This check does not change certificate trust.'
