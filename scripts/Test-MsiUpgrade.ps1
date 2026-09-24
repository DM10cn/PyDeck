[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory, [Parameter(Mandatory)][string]$PreviousReleaseDirectory, [switch]$SkipGui)
$ErrorActionPreference = 'Stop'
$repo = Split-Path -Parent $PSScriptRoot
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$previous = (Resolve-Path -LiteralPath $PreviousReleaseDirectory).Path
$metadata = Get-Content -LiteralPath (Join-Path $release 'build.json') -Raw | ConvertFrom-Json
$old = Get-Content -LiteralPath (Join-Path $previous 'build.json') -Raw | ConvertFrom-Json
if ([version]$old.version -ge [version]$metadata.version) { throw 'The previous release must be older.' }
$msi = Join-Path $release ('assets/PyDeck-' + $metadata.version + '-win-x64.msi')
$oldMsi = Join-Path $previous ('assets/PyDeck-' + $old.version + '-win-x64.msi')
foreach ($file in @($msi, $oldMsi)) { if (!(Test-Path -LiteralPath $file)) { throw 'Both release MSI files are required.' } }
$target = Join-Path $release 'work/upgrade-installed'
$menu = Join-Path ([Environment]::GetFolderPath('Programs')) 'PyDeck'
$desktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) 'PyDeck.lnk'
$key = 'HKCU:\Software\DM10cn\PyDeck\Installer'
if ((Test-Path $key) -or (Test-Path $menu) -or (Test-Path $desktop) -or (Test-Path $target) -or (Get-Process PyDeck -ErrorAction SilentlyContinue)) { throw 'An existing installation or running app prevents isolated validation.' }
# Observe existing application data without seeding or changing user preferences.
$data = Join-Path ([Environment]::GetFolderPath('LocalApplicationData')) 'PyDeck'
function Data-Digests {
    $result = @{}
    if (Test-Path -LiteralPath $data) { foreach ($file in Get-ChildItem -LiteralPath $data -File -Recurse) { $result[$file.FullName] = (Get-FileHash -LiteralPath $file.FullName).Hash } }
    return $result
}
$before = Data-Digests
$step = 0
function Run-Msi([string]$Verb, [string]$Package, [string[]]$Properties = @()) {
    $script:step++
    $log = Join-Path $release ('upgrade-step-' + $script:step + '.log')
    $arguments = @($Verb, ('"' + $Package + '"'), '/qn', '/norestart', '/l*v', ('"' + $log + '"')) + $Properties
    $process = Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(240000) -or $process.ExitCode -ne 0) { throw "MSI validation failed; inspect $log" }
}
$active = $null
try {
    Run-Msi '/i' $oldMsi @('INSTALLFOLDER="' + $target + '"', 'DESKTOPSHORTCUT=0', 'STARTMENUSHORTCUT=1')
    $active = $oldMsi
    $sentinel = Join-Path $target 'upgrade-user-file.txt'
    [IO.File]::WriteAllText($sentinel, 'Unrelated user content')
    Run-Msi '/i' $msi
    $active = $msi
    $state = Get-ItemProperty -LiteralPath $key
    if ($state.InstallFolder.TrimEnd('\') -ne $target.TrimEnd('\') -or $state.DesktopShortcutEnabled -ne '0' -or $state.StartMenuShortcutEnabled -ne '1') { throw 'Upgrade lost installer choices.' }
    if (!(Test-Path (Join-Path $menu 'PyDeck.lnk')) -or (Test-Path $desktop)) { throw 'Upgrade lost shortcut choices.' }
    $files = @(Get-ChildItem -LiteralPath $metadata.payload -Recurse -File)
    foreach ($file in $files) {
        $relative = [IO.Path]::GetRelativePath($metadata.payload, $file.FullName)
        $actual = Join-Path $target $relative
        if (!(Test-Path -LiteralPath $actual) -or (Get-FileHash -LiteralPath $actual).Hash -ne (Get-FileHash -LiteralPath $file.FullName).Hash) { throw "Upgrade payload mismatch: $relative" }
    }
    foreach ($file in Get-ChildItem -LiteralPath $old.payload -Recurse -File) {
        $relative = [IO.Path]::GetRelativePath($old.payload, $file.FullName)
        if (!(Test-Path -LiteralPath (Join-Path $metadata.payload $relative)) -and (Test-Path -LiteralPath (Join-Path $target $relative))) { throw "Obsolete payload remains: $relative" }
    }
    if ((Get-Content -LiteralPath $sentinel -Raw) -ne 'Unrelated user content') { throw 'Upgrade altered unrelated files.' }
    $after = Data-Digests
    if ($before.Count -ne $after.Count) { throw 'Upgrade changed the application data file set.' }
    foreach ($path in $before.Keys) { if ($before[$path] -ne $after[$path]) { throw 'Upgrade changed application data.' } }
    if (!$SkipGui) { & (Join-Path $repo 'scripts/Smoke-Test.ps1') -BuildDirectory $target }
    Write-Output "PASS published MSI $($old.version) -> $($metadata.version): $($files.Count) file hashes, obsolete-file removal, path/shortcut retention, unchanged application data; GUI skipped: $SkipGui"
} finally { if ($active) { Run-Msi '/x' $active } }
if ((Test-Path $menu) -or (Test-Path $desktop) -or (Test-Path $key) -or (Test-Path (Join-Path $target 'PyDeck.Launcher.exe'))) { throw 'Test installation left installer-owned state.' }
Write-Output 'PASS uninstall cleanup preserves the unrelated user file'
