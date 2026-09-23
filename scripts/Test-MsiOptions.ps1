# Installer-only integration checks use separate product, component, registry and shortcut identities.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory, [string]$WixCommand = 'wix')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
$metadata = Get-Content -LiteralPath (Join-Path $release 'build.json') -Raw | ConvertFrom-Json
$testId = [Guid]::NewGuid().ToString('N')
$name = 'PyDeck Installer Check ' + $testId.Substring(0, 8)
$key = 'Software\DM10cn\PyDeck\InstallerChecks\' + $testId
$work = Join-Path $release ('work\installer-checks-' + $testId)
New-Item -ItemType Directory -Path $work | Out-Null
$packageSource = (Get-Content -LiteralPath (Join-Path $repoRoot 'packaging\msi\Package.wxs') -Raw).
    Replace('Name="PyDeck"', ('Name="' + $name + '"')).
    Replace('D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA', [Guid]::NewGuid().ToString()).
    Replace('Software\DM10cn\PyDeck\Installer', $key)
$packageFile = Join-Path $work 'Package.wxs'
[IO.File]::WriteAllText($packageFile, $packageSource, [Text.UTF8Encoding]::new($false))
[xml]$payloadSource = (Get-Content -LiteralPath (Join-Path $release 'work\Payload.wxs') -Raw).Replace('Software\DM10cn\PyDeck\Installer', $key)
foreach ($component in $payloadSource.Wix.Fragment.ComponentGroup.Component) { $component.Guid = [Guid]::NewGuid().ToString() }
$payloadFile = Join-Path $work 'Payload.wxs'
$payloadSource.Save($payloadFile)
$baseVersion = [version]$metadata.version
$upgradeVersion = '{0}.{1}.{2}' -f $baseVersion.Major, $baseVersion.Minor, ($baseVersion.Build + 1)
$packages = @{}
foreach ($version in @($metadata.version, $upgradeVersion)) {
    $path = Join-Path $work ("Check-$version.msi")
    & $WixCommand build $packageFile (Join-Path $repoRoot 'packaging\msi\InstallOptions.wxs') $payloadFile -arch x64 -ext WixToolset.UI.wixext/7.0.0 -d "ProductVersion=$version" -d "PayloadDir=$($metadata.payload)" -d ('LicenseRtf=' + (Join-Path $release 'work\License.rtf')) -d ('InstallerActions=' + (Join-Path $release 'work\native\PyDeck.InstallerActions.dll')) -pdbtype none -intermediatefolder (Join-Path $work "wix-$version") -o $path
    if ($LASTEXITCODE -ne 0) { throw 'Installer fixture build failed.' }
    $packages[$version] = $path
}
$desktop = Join-Path ([Environment]::GetFolderPath('DesktopDirectory')) ($name + '.lnk')
$menuFolder = Join-Path ([Environment]::GetFolderPath('Programs')) $name
$menu = Join-Path $menuFolder ($name + '.lnk')
if ((Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath $menuFolder)) { throw 'Fixture shortcut collision.' }
$script:step = 0
function Invoke-Msi([string]$Verb, [string]$Package, [string[]]$Properties = @()) {
    $script:step++
    $arguments = @($Verb, ('"' + $Package + '"'), '/qn', '/norestart', '/l*v', ('"' + (Join-Path $work "step-$script:step.log") + '"')) + $Properties
    $process = Start-Process msiexec.exe -ArgumentList $arguments -WindowStyle Hidden -PassThru
    if (!$process.WaitForExit(60000)) { throw "MSI fixture is still running as $($process.Id); inspect $work" }
    if ($process.ExitCode -ne 0) { throw "MSI $Verb failed: $($process.ExitCode); inspect step-$script:step.log" }
}
function Assert-Installation([string]$Folder, [int]$DesktopEnabled, [int]$MenuEnabled) {
    if (!(Test-Path -LiteralPath (Join-Path $Folder 'PyDeck.Launcher.exe'))) { throw 'Custom install folder was not used.' }
    $state = Get-ItemProperty -LiteralPath ('HKCU:\' + $key)
    if ($state.InstallFolder.TrimEnd('\') -ne $Folder.TrimEnd('\') -or $state.DesktopShortcutEnabled -ne [string]$DesktopEnabled -or $state.StartMenuShortcutEnabled -ne [string]$MenuEnabled) { throw 'Installer choices were not saved correctly.' }
    $shell = New-Object -ComObject WScript.Shell
    try {
        foreach ($shortcut in @(@($desktop, $DesktopEnabled), @($menu, $MenuEnabled))) {
            if ((Test-Path -LiteralPath $shortcut[0]) -ne [bool]$shortcut[1]) { throw "Unexpected shortcut state: $($shortcut[0])" }
            if ($shortcut[1]) {
                $link = $shell.CreateShortcut($shortcut[0])
                if ($link.TargetPath -ne (Join-Path $Folder 'PyDeck.Launcher.exe') -or $link.WorkingDirectory.TrimEnd('\') -ne $Folder.TrimEnd('\')) { throw 'Shortcut does not launch from the chosen installation folder.' }
                [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($link)
            }
        }
    } finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($shell) }
}
$cases = @(
    @{ Name = 'defaults'; Desktop = 0; Menu = 1; Options = @() },
    @{ Name = 'no-shortcuts'; Desktop = 0; Menu = 0; Options = @('DESKTOPSHORTCUT=0', 'STARTMENUSHORTCUT=0') },
    @{ Name = 'desktop-only'; Desktop = 1; Menu = 0; Options = @('DESKTOPSHORTCUT=1', 'STARTMENUSHORTCUT=0') },
    @{ Name = 'both'; Desktop = 1; Menu = 1; Options = @('DESKTOPSHORTCUT=1', 'STARTMENUSHORTCUT=1') }
)
foreach ($case in $cases) {
    $folder = Join-Path $work ('安装目录 with spaces\' + $case.Name)
    New-Item -ItemType Directory -Path $folder -Force | Out-Null
    $sentinel = Join-Path $folder 'user-file.txt'
    [IO.File]::WriteAllText($sentinel, 'This unrelated file must survive uninstall.')
    $active = $null
    try {
        Invoke-Msi '/i' $packages[$metadata.version] (@('INSTALLFOLDER="' + $folder + '"') + $case.Options)
        $active = $packages[$metadata.version]
        Assert-Installation $folder $case.Desktop $case.Menu
        Write-Output "PASS $($case.Name): custom Unicode/spaced folder and shortcut targets"
        if ($case.Name -eq 'desktop-only') {
            Invoke-Msi '/fa' $active
            Assert-Installation $folder 1 0
            Write-Output 'PASS repair retains installation folder and shortcut choices'
            Invoke-Msi '/i' $packages[$upgradeVersion]
            $active = $packages[$upgradeVersion]
            Assert-Installation $folder 1 0
            Write-Output 'PASS major upgrade retains installation folder and shortcut choices'
        }
    } finally { if ($active) { Invoke-Msi '/x' $active } }
    if ((Test-Path -LiteralPath $desktop) -or (Test-Path -LiteralPath $menu) -or (Test-Path -LiteralPath (Join-Path $folder 'PyDeck.Launcher.exe')) -or (Test-Path -LiteralPath ('HKCU:\' + $key))) { throw 'Uninstall left fixture payload, shortcuts or installer preferences.' }
    if (!(Test-Path -LiteralPath $sentinel)) { throw 'Uninstall removed an unrelated user file.' }
    Write-Output "PASS $($case.Name): uninstall removes only installer-owned files and shortcuts"
}
Write-Output "Installer option checks passed. Local evidence: $work"
