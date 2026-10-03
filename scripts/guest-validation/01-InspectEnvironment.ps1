#Requires -Version 5.1
<#
Read-only guest preflight for PyDeck. Run as the guest user who will
use PyDeck. Writes only its report directory; no installs, downloads, elevation,
execution-policy changes, application launches or Hyper-V operations.
#>
[CmdletBinding()]
param([string]$OutputDirectory, [string]$ReferenceRelease)

$ErrorActionPreference = 'Stop'
# Windows PowerShell 5.1 can evaluate parameter defaults before PSScriptRoot
# is populated. Resolve the default only after entering the script body.
if ([string]::IsNullOrWhiteSpace($OutputDirectory)) {
    $scriptDirectory = $PSScriptRoot
    if ([string]::IsNullOrWhiteSpace($scriptDirectory) -and $MyInvocation.MyCommand.Path) {
        $scriptDirectory = Split-Path -Parent $MyInvocation.MyCommand.Path
    }
    if ([string]::IsNullOrWhiteSpace($scriptDirectory)) {
        throw 'Cannot determine the script directory. Specify -OutputDirectory with a writable folder.'
    }
    $OutputDirectory = Join-Path $scriptDirectory 'PyDeck-Diagnostics'
}
$runName = '01-environment-' + (Get-Date -Format 'yyyyMMdd-HHmmss') + '-' + [guid]::NewGuid().ToString('N').Substring(0, 8)
$runDirectory = Join-Path ([IO.Path]::GetFullPath($OutputDirectory)) $runName
[void][IO.Directory]::CreateDirectory($runDirectory)
$logPath = Join-Path $runDirectory 'checks.log'
$encoding = New-Object Text.UTF8Encoding($true)
[IO.File]::WriteAllText($logPath, '', $encoding)
$checks = New-Object 'System.Collections.Generic.List[object]'
$script:sectionFailures = 0

function Protect-Text([string]$Text) {
    # Do not collect usernames, machine names, IP addresses, environment dumps
    # or app settings. Mask profile paths in findings and exception messages.
    if ($env:USERPROFILE) { $Text = $Text -replace [regex]::Escape($env:USERPROFILE), '<USERPROFILE>' }
    return ($Text -replace '(?i)[A-Z]:\\Users\\[^\\\s"<>]+', '<USERPROFILE>')
}
function Add-Check([string]$Id, [string]$Status, [string]$Message, $Details = $null) {
    $messageSafe = Protect-Text $Message
    $checks.Add([pscustomobject]@{ id = $Id; status = $Status; message = $messageSafe; details = $Details })
    $line = '[{0}] {1}: {2}' -f $Status, $Id, $messageSafe
    [IO.File]::AppendAllText($logPath, $line + [Environment]::NewLine, $encoding)
    Write-Host $line
}
function Inspect-Section([string]$Name, [scriptblock]$Action) {
    Write-Host ('Checking ' + $Name + ' ...')
    try { & $Action } catch {
        $script:sectionFailures++
        Add-Check $Name 'ERROR' $_.Exception.Message
    }
}
function Read-Registry64([Microsoft.Win32.RegistryHive]$Hive, [string]$Path, [string[]]$Names) {
    $baseKey = [Microsoft.Win32.RegistryKey]::OpenBaseKey($Hive, [Microsoft.Win32.RegistryView]::Registry64)
    $key = $null
    try {
        $key = $baseKey.OpenSubKey($Path)
        $values = @{}
        foreach ($name in $Names) { $values[$name] = if ($null -ne $key) { $key.GetValue($name) } else { $null } }
        return $values
    } finally { if ($null -ne $key) { $key.Dispose() }; $baseKey.Dispose() }
}
function Get-PeArchitecture([string]$Path) {
    $stream = [IO.File]::Open($Path, [IO.FileMode]::Open, [IO.FileAccess]::Read, [IO.FileShare]::ReadWrite)
    $reader = New-Object IO.BinaryReader($stream)
    try {
        if ($stream.Length -lt 64 -or $reader.ReadUInt16() -ne 0x5A4D) { return 'Unknown' }
        $stream.Position = 60; $offset = $reader.ReadInt32()
        if ($offset -lt 64 -or $offset -gt $stream.Length - 6) { return 'Unknown' }
        $stream.Position = $offset
        if ($reader.ReadUInt32() -ne 0x4550) { return 'Unknown' }
        switch ($reader.ReadUInt16()) { 0x8664 { return 'x64' }; 0x014c { return 'x86' }; 0xaa64 { return 'ARM64' }; default { return 'Unknown' } }
    } finally { $reader.Dispose() }
}

Write-Host 'PyDeck guest validation - step 01 (read-only)'
Write-Host 'A failed prerequisite is a finding, not a script crash. No software will be installed.'
Inspect-Section 'windows' {
    $os = Read-Registry64 LocalMachine 'SOFTWARE\Microsoft\Windows NT\CurrentVersion' @('ProductName','EditionID','DisplayVersion','CurrentBuildNumber','UBR','InstallationType')
    $build = 0
    if (![int]::TryParse([string]$os.CurrentBuildNumber, [ref]$build)) { throw 'Cannot determine the Windows build number' }
    $architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
    # Registry ProductName can still say Windows 10 on Windows 11. Use the
    # build for the client family and retain the raw registry data separately.
    $family = if ($os.InstallationType -eq 'Client') { if ($build -ge 22000) { 'Windows 11' } else { 'Windows 10' } } else { $os.ProductName }
    Add-Check 'windows.version' 'INFO' ('{0}; edition {1}; build {2}.{3}; {4}' -f $family, $os.EditionID, $build, $os.UBR, $architecture) $os
    $supported = $architecture -eq 'AMD64' -and $build -ge 22000
    Add-Check 'windows.currentRelease' $(if ($supported) { 'PASS' } else { 'FAIL' }) 'PyDeck requires Windows 11 x64 (build 22000+); this script does not bypass that gate.'
    if (!$supported -and $architecture -eq 'AMD64' -and $build -ge 19041) {
        Add-Check 'windows.compatibilityCandidate' 'INFO' 'Candidate for a separate Windows 10 compatibility build, not a supported or validated release target.'
    }
    Add-Check 'powershell' $(if ([Environment]::Is64BitProcess) { 'PASS' } else { 'WARN' }) ('PowerShell {0}; 64-bit process: {1}. Use 64-bit Windows PowerShell for the next steps.' -f $PSVersionTable.PSVersion, [Environment]::Is64BitProcess)
}
Inspect-Section 'dotnet' {
    $registration = Read-Registry64 LocalMachine 'SOFTWARE\dotnet\Setup\InstalledVersions\x64' @('InstallLocation')
    $origin = 'default location'
    $programFiles64 = if ($env:ProgramW6432) { $env:ProgramW6432 } else { $env:ProgramFiles }
    $root = Join-Path $programFiles64 'dotnet'
    if ($env:DOTNET_ROOT_X64) { $root = $env:DOTNET_ROOT_X64; $origin = 'DOTNET_ROOT_X64' }
    elseif ($env:DOTNET_ROOT) { $root = $env:DOTNET_ROOT; $origin = 'DOTNET_ROOT' }
    elseif ($registration.InstallLocation) { $root = [string]$registration.InstallLocation; $origin = 'x64 registry registration' }
    $runtimes = @(Get-ChildItem -LiteralPath (Join-Path $root 'shared\Microsoft.NETCore.App') -Directory -ErrorAction SilentlyContinue | Where-Object Name -match '^10\.0\.\d+$')
    $complete = @($runtimes | Where-Object {
        (Test-Path -LiteralPath (Join-Path $_.FullName 'coreclr.dll')) -and
        (Get-PeArchitecture (Join-Path $_.FullName 'coreclr.dll')) -eq 'x64' -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'hostpolicy.dll')) -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'System.Private.CoreLib.dll'))
    })
    $hosts = @(Get-ChildItem -LiteralPath (Join-Path $root 'host\fxr') -Directory -ErrorAction SilentlyContinue | Where-Object {
        $_.Name -match '^\d+\.\d+\.\d+$' -and [version]$_.Name -ge [version]'10.0.0' -and
        (Test-Path -LiteralPath (Join-Path $_.FullName 'hostfxr.dll')) -and
        (Get-PeArchitecture (Join-Path $_.FullName 'hostfxr.dll')) -eq 'x64'
    })
    $ready = $complete.Count -gt 0 -and $hosts.Count -gt 0 -and (Test-Path -LiteralPath (Join-Path $root 'dotnet.exe'))
    Add-Check 'dotnet.runtime' $(if ($ready) { 'PASS' } else { 'FAIL' }) ('.NET Runtime 10.0 stable x64; effective root from ' + $origin) @{
        root = $root; versionsFound = @($runtimes.Name); completeVersions = @($complete.Name); compatibleHosts = @($hosts.Name)
    }
}
Inspect-Section 'windowsAppRuntime' {
    $packages = @(Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2' -ErrorAction Stop)
    $readyPackages = @($packages | Where-Object {
        [string]$_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'2.5.1.0' -and
        (Test-Path -LiteralPath (Join-Path $_.InstallLocation 'Microsoft.UI.Xaml.dll'))
    })
    Add-Check 'windowsAppRuntime.registration' $(if ($readyPackages.Count) { 'PASS' } else { 'FAIL' }) 'Windows App Runtime 2.x x64, minimum 2.5.1.0, registered for this user with Microsoft.UI.Xaml.dll present.' @(
        $packages | Select-Object Name,Version,Architecture,Status
    )
}
Inspect-Section 'visualCpp' {
    $vc = Read-Registry64 LocalMachine 'SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' @('Installed','Major','Version')
    $systemDirectory = if ([Environment]::Is64BitOperatingSystem -and ![Environment]::Is64BitProcess) { Join-Path $env:SystemRoot 'Sysnative' } else { Join-Path $env:SystemRoot 'System32' }
    $missing = @('msvcp140.dll','vcruntime140.dll','vcruntime140_1.dll' | Where-Object { !(Test-Path -LiteralPath (Join-Path $systemDirectory $_)) })
    $ready = $vc.Installed -eq 1 -and $vc.Major -eq 14 -and $missing.Count -eq 0
    Add-Check 'visualCpp.runtime' $(if ($ready) { 'PASS' } else { 'FAIL' }) 'Visual C++ v14 x64 for MSI / unpackaged PyDeck; MSIX resolves its own framework dependencies.' @{ version = $vc.Version; missingFiles = $missing }
}
Inspect-Section 'pythonManager' {
    $manager = Get-Command pymanager.exe -CommandType Application -ErrorAction SilentlyContinue | Select-Object -First 1
    Add-Check 'pythonManager.discovery' $(if ($manager) { 'PASS' } else { 'WARN' }) 'PIM on PATH (not executed); optional for opening the GUI. A manager elsewhere can be selected in Settings.' @{ path = $(if ($manager) { $manager.Source } else { $null }) }
}
Inspect-Section 'existingInstallation' {
    $msi = Read-Registry64 CurrentUser 'Software\DM10cn\PyDeck\Installer' @('InstallFolder','DesktopShortcutEnabled','StartMenuShortcutEnabled')
    Add-Check 'pydeck.msi' 'INFO' 'Existing per-user MSI registration; no settings or application data were read.' $msi
    $packages = @(Get-AppxPackage -Name 'DM10cn.PyDeck' -ErrorAction Stop | Select-Object Name,Version,Architecture,Status)
    Add-Check 'pydeck.msix' 'INFO' 'Existing per-user MSIX registration.' $packages
}

$failed = @($checks | Where-Object status -eq 'FAIL')
$errors = @($checks | Where-Object status -eq 'ERROR')
$warnings = @($checks | Where-Object status -eq 'WARN')
$status = if ($errors.Count) { 'INCOMPLETE_COLLECTION' } elseif ($failed.Count) { 'PREREQUISITES_BLOCKED' } else { 'PREFLIGHT_ONLY' }
$report = [ordered]@{
    schemaVersion = 1; step = '01-environment'; collectedAt = [DateTimeOffset]::Now.ToString('o')
    referenceRelease = $ReferenceRelease; status = $status
    collectionComplete = ($script:sectionFailures -eq 0)
    limitations = @('Read-only prerequisite inspection, not an installation or GUI compatibility pass.',
        'PIM was located, not executed; its version and Python operations remain untested.',
        'No runtime downloads, network requests, credential collection or application-data collection.',
        'The native launcher --check remains authoritative for its own launch decision.')
    checks = @($checks.ToArray())
}
# JSON paths contain escaped backslashes: use escaped profile values here, not
# the plain-text sanitizer, to keep the resulting JSON well formed.
$json = $report | ConvertTo-Json -Depth 10
$profileJson = if ($env:USERPROFILE) { ($env:USERPROFILE | ConvertTo-Json -Compress).Trim('"') } else { '' }
if ($profileJson) { $json = $json -replace [regex]::Escape($profileJson), '<USERPROFILE>' }
$json = $json -replace '(?i)[A-Z]:\\\\Users\\\\[^\\"<>]+', '<USERPROFILE>'
[IO.File]::WriteAllText((Join-Path $runDirectory 'result.json'), $json, $encoding)
$summary = @(
    'PyDeck - step 01 environment report', ('Status: ' + $status),
    ('Failed prerequisites: {0}; warnings: {1}; collection errors: {2}' -f $failed.Count, $warnings.Count, $errors.Count), '',
    'This is not a GUI, MSI or MSIX compatibility pass.',
    'Send this step ZIP back for review before running installation or upgrade steps.', '',
    'Checks:'
) + @($checks | ForEach-Object { '[{0}] {1}: {2}' -f $_.status, $_.id, $_.message })
[IO.File]::WriteAllText((Join-Path $runDirectory 'summary.txt'), (Protect-Text ($summary -join [Environment]::NewLine)), $encoding)
$archive = $runDirectory + '.zip'
try {
    Compress-Archive -LiteralPath @((Join-Path $runDirectory 'checks.log'), (Join-Path $runDirectory 'result.json'), (Join-Path $runDirectory 'summary.txt')) -DestinationPath $archive -ErrorAction Stop
    Write-Host ('SEND BACK: ' + $archive)
} catch {
    Write-Warning ('Could not create ZIP: ' + (Protect-Text $_.Exception.Message))
    Write-Host ('SEND BACK the three report files in: ' + $runDirectory)
}
Write-Host 'Step finished. Review summary.txt; successful script completion does not mean compatibility passed.'
