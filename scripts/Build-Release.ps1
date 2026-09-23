[CmdletBinding()]
param(
    [string]$CertificateThumbprint,
    [switch]$AllowUnsigned,
    [switch]$SkipChecks,
    [string]$WixCommand = 'wix'
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $repoRoot '.local\dotnet'
$env:NUGET_PACKAGES = Join-Path $repoRoot '.local\packages'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'

function Invoke-Checked([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed with exit code $LASTEXITCODE" }
}
function Escape-Xml([string]$Value) { [Security.SecurityElement]::Escape($Value) }
function Stable-Id([string]$Value) {
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($Value.ToLowerInvariant()))
    [Convert]::ToHexString($hash)[0..23] -join ''
}
function Stable-ComponentGuid([string]$RelativePath) {
    $hash = [Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes('DM10cn.PyDeck/x64/' + $RelativePath.ToLowerInvariant()))
    $hex = [Convert]::ToHexString($hash)
    # UUIDv8: stable, project-scoped component identity across package versions.
    [Guid]::ParseExact(($hex.Substring(0, 12) + '8' + $hex.Substring(13, 3) + 'A' + $hex.Substring(17, 15)), 'N').ToString('D')
}
function Find-SdkTools {
    $sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\bin'
    $candidates = Get-ChildItem -LiteralPath $sdkRoot -Directory | Where-Object Name -match '^10\.0\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending
    foreach ($candidate in $candidates) {
        $directory = Join-Path $candidate.FullName 'x64'
        if ((Test-Path -LiteralPath (Join-Path $directory 'makeappx.exe')) -and (Test-Path -LiteralPath (Join-Path $directory 'signtool.exe'))) { return $directory }
    }
    throw 'Windows SDK x64 MakeAppx and SignTool are required.'
}

Push-Location $repoRoot
try {
    if (!$CertificateThumbprint -and !$AllowUnsigned) { throw 'Supply -CertificateThumbprint, or explicitly use -AllowUnsigned for local packaging checks.' }
    $certificate = $null
    if ($CertificateThumbprint) {
        if ($CertificateThumbprint -notmatch '^[A-Fa-f0-9]{40}$') { throw 'Invalid certificate thumbprint.' }
        $certificate = Get-Item -LiteralPath "Cert:\CurrentUser\My\$CertificateThumbprint"
        if (!$certificate.HasPrivateKey -or $certificate.NotAfter -le (Get-Date) -or $certificate.NotBefore -gt (Get-Date)) { throw 'A valid signing certificate with a private key is required.' }
    }
    $sdkTools = Find-SdkTools
    Invoke-Checked $WixCommand @('--version')
    [xml]$project = Get-Content -LiteralPath 'src/PimGui.App/PimGui.App.csproj' -Raw
    $version = [string]$project.Project.PropertyGroup.Version
    if ($version -notmatch '^\d+\.\d+\.\d+$') { throw 'The release version must have three numeric components.' }
    $output = Join-Path $repoRoot ('artifacts\release-' + $version + '-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
    $work = Join-Path $output 'work'
    $assets = Join-Path $output 'assets'
    $payload = Join-Path $work 'payload'
    New-Item -ItemType Directory -Path $work, $assets, $payload | Out-Null
    $sourceCommit = (git rev-parse HEAD).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'A Git checkout is required.' }
    $sourceDirty = [bool](git status --porcelain --untracked-files=normal)
    Invoke-Checked dotnet @('restore', 'PimGui.slnx', '--locked-mode', '-p:Platform=x64', '--nologo')
    if (!$SkipChecks) { Invoke-Checked dotnet @('run', '--project', 'tests/PimGui.Checks/PimGui.Checks.csproj', '-c', 'Release', '--no-restore') }
    Invoke-Checked dotnet @('publish', 'src/PimGui.App/PimGui.App.csproj', '-c', 'Release', '-p:Platform=x64', '--no-restore', '--self-contained', 'false', '-p:DebugType=None', '-p:DebugSymbols=false', '-p:PublishReadyToRun=false', "-p:PathMap=$repoRoot=/_/PyDeck", '-o', $payload, '--nologo')
    $runtimeConfig = Get-Content -LiteralPath (Join-Path $payload 'PyDeck.runtimeconfig.json') -Raw | ConvertFrom-Json
    if ($runtimeConfig.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App') { throw 'Expected an external .NET runtime.' }
    foreach ($forbidden in @('coreclr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'Microsoft.UI.Xaml.dll', 'onnxruntime.dll', 'DirectML.dll')) {
        if (Test-Path -LiteralPath (Join-Path $payload $forbidden)) { throw "Unexpected bundled runtime: $forbidden" }
    }
    if (Get-ChildItem -LiteralPath $payload -Recurse -File -Filter '*.pdb') { throw 'Debug symbols must not be included in public packages.' }
    Copy-Item -LiteralPath 'LICENSE','THIRD-PARTY-NOTICES.md' -Destination $payload
    $notices = Join-Path $payload 'ThirdPartyNotices'
    New-Item -ItemType Directory -Path $notices | Out-Null
    $lock = Get-Content -LiteralPath 'src/PimGui.App/packages.lock.json' -Raw | ConvertFrom-Json
    $dependencyVersions = @{}
    foreach ($framework in $lock.dependencies.PSObject.Properties) {
        foreach ($dependency in $framework.Value.PSObject.Properties) {
            if ($dependency.Value.type -ne 'Project') { $dependencyVersions[$dependency.Name] = $dependency.Value.resolved }
        }
    }
    $assetsFile = Get-Content -LiteralPath 'src/PimGui.App/obj/project.assets.json' -Raw | ConvertFrom-Json
    foreach ($framework in $assetsFile.project.frameworks.PSObject.Properties) {
        foreach ($dependency in $framework.Value.downloadDependencies) { $dependencyVersions[$dependency.name] = $dependency.version.Trim('[', ']') -split ',' | Select-Object -First 1 }
    }
    foreach ($name in $dependencyVersions.Keys) {
        if ($name -notmatch '^Microsoft\.(WindowsAppSDK\.(Base|Runtime|Foundation|InteractiveExperiences|WinUI)|Web\.WebView2|Windows\.SDK\.NET\.Ref)$') { continue }
        $packageDirectory = Join-Path $env:NUGET_PACKAGES ($name.ToLowerInvariant() + '\' + $dependencyVersions[$name])
        foreach ($notice in Get-ChildItem -LiteralPath $packageDirectory -File | Where-Object Name -match '(?i)license|notice') {
            Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $notices ($name + '-' + $notice.Name))
        }
    }
    $sdkLicenseRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10\Licenses'
    foreach ($notice in Get-ChildItem -LiteralPath $sdkLicenseRoot -Recurse -File | Where-Object Name -in 'sdk_license.rtf','sdk_third_party_notices.rtf' | Sort-Object FullName -Descending | Group-Object Name | ForEach-Object { $_.Group[0] }) {
        Copy-Item -LiteralPath $notice.FullName -Destination (Join-Path $notices $notice.Name)
    }
    $dotnetRoot = Split-Path -Parent (Get-Command dotnet).Source
    foreach ($name in @('LICENSE.txt', 'ThirdPartyNotices.txt')) {
        $notice = Join-Path $dotnetRoot $name
        if (Test-Path -LiteralPath $notice) { Copy-Item -LiteralPath $notice -Destination (Join-Path $notices ('DotNet-' + $name)) }
    }
    if ($certificate) {
        Invoke-Checked (Join-Path $sdkTools 'signtool.exe') @('sign', '/fd', 'SHA256', '/sha1', $CertificateThumbprint, '/s', 'My', (Join-Path $payload 'PyDeck.exe'))
        Export-Certificate -Cert $certificate -FilePath (Join-Path $assets 'PyDeck-preview.cer') | Out-Null
    }

    # Generate explicit per-user components with stable HKCU key paths; no recursive installer deletion.
    $fragment = [Text.StringBuilder]::new()
    [void]$fragment.AppendLine('<Wix xmlns="http://wixtoolset.org/schemas/v4/wxs"><Fragment>')
    $directories = @(Get-ChildItem -LiteralPath $payload -Directory -Recurse | Sort-Object FullName)
    $directoryIds = @{ '' = 'INSTALLFOLDER' }
    foreach ($directory in $directories) {
        $relative = [IO.Path]::GetRelativePath($payload, $directory.FullName)
        $directoryIds[$relative] = 'D_' + (Stable-Id $relative)
    }
    foreach ($directory in $directories) {
        $relative = [IO.Path]::GetRelativePath($payload, $directory.FullName)
        $parentRelative = [IO.Path]::GetDirectoryName($relative)
        $parentId = $directoryIds[$parentRelative]
        [void]$fragment.AppendLine(('<DirectoryRef Id="{0}"><Directory Id="{1}" Name="{2}" /></DirectoryRef>' -f $parentId, $directoryIds[$relative], (Escape-Xml $directory.Name)))
    }
    [void]$fragment.AppendLine('<ComponentGroup Id="PayloadFiles">')
    $cleanedDirectories = @{}
    foreach ($file in Get-ChildItem -LiteralPath $payload -File -Recurse | Sort-Object FullName) {
        $relative = [IO.Path]::GetRelativePath($payload, $file.FullName)
        $directoryRelative = [IO.Path]::GetDirectoryName($relative)
        $id = Stable-Id $relative
        [void]$fragment.AppendLine(('<Component Id="C_{0}" Directory="{1}" Guid="{3}"><File Id="F_{0}" Source="{2}" /><RegistryValue Root="HKCU" Key="Software\DM10cn\PyDeck\Installer\Files" Name="{0}" Type="integer" Value="1" KeyPath="yes" />' -f $id, $directoryIds[$directoryRelative], (Escape-Xml $file.FullName), (Stable-ComponentGuid $relative)))
        if ($directoryRelative -and !$cleanedDirectories.ContainsKey($directoryRelative)) {
            [void]$fragment.AppendLine(('<RemoveFolder Id="R_{0}" On="uninstall" />' -f $id))
            $cleanedDirectories[$directoryRelative] = $true
        }
        [void]$fragment.AppendLine('</Component>')
    }
    [void]$fragment.AppendLine('</ComponentGroup></Fragment></Wix>')
    $fragmentPath = Join-Path $work 'Payload.wxs'
    [IO.File]::WriteAllText($fragmentPath, $fragment.ToString(), [Text.UTF8Encoding]::new($false))
    $licenseText = (Get-Content -LiteralPath LICENSE -Raw).Replace('\', '\\').Replace('{', '\{').Replace('}', '\}') -replace '\r?\n', '\par '
    $licenseRtf = Join-Path $work 'License.rtf'
    [IO.File]::WriteAllText($licenseRtf, '{\rtf1\ansi\deff0 {\fonttbl {\f0 Segoe UI;}}\f0\fs18 ' + $licenseText + '}', [Text.Encoding]::ASCII)
    $msi = Join-Path $assets "PyDeck-$version-win-x64.msi"
    Invoke-Checked $WixCommand @('build', 'packaging/msi/Package.wxs', $fragmentPath, '-arch', 'x64', '-ext', 'WixToolset.UI.wixext/7.0.0', '-ext', 'WixToolset.Netfx.wixext/7.0.0', '-d', "ProductVersion=$version", '-d', "PayloadDir=$payload", '-d', "LicenseRtf=$licenseRtf", '-pdbtype', 'none', '-intermediatefolder', (Join-Path $work 'wix'), '-o', $msi)

    $msixStage = Join-Path $work 'msix'
    New-Item -ItemType Directory -Path $msixStage | Out-Null
    Get-ChildItem -LiteralPath $payload | Copy-Item -Destination $msixStage -Recurse
    # Packaging requires fixed-size assets; reuse the selected artwork without visual redesign.
    Add-Type -AssemblyName System.Drawing
    $icon = [Drawing.Image]::FromFile((Join-Path $repoRoot 'src/PimGui.App/Assets/AppIcon.png'))
    try {
        foreach ($item in @(@('StoreLogo.png', 50), @('Square44x44Logo.png', 44), @('Square150x150Logo.png', 150))) {
            $bitmap = [Drawing.Bitmap]::new([int]$item[1], [int]$item[1])
            $graphics = [Drawing.Graphics]::FromImage($bitmap)
            try {
                $graphics.InterpolationMode = [Drawing.Drawing2D.InterpolationMode]::HighQualityBicubic
                $graphics.DrawImage($icon, 0, 0, [int]$item[1], [int]$item[1])
                $bitmap.Save((Join-Path $msixStage ('Assets\' + $item[0])), [Drawing.Imaging.ImageFormat]::Png)
            } finally { $graphics.Dispose(); $bitmap.Dispose() }
        }
    } finally { $icon.Dispose() }
    [xml]$manifest = Get-Content -LiteralPath 'packaging/msix/AppxManifest.xml' -Raw
    $manifest.Package.Identity.Version = "$version.0"
    if ($certificate -and $manifest.Package.Identity.Publisher -ne $certificate.Subject) { throw 'Certificate subject must match the MSIX publisher identity.' }
    $manifest.Save((Join-Path $msixStage 'AppxManifest.xml'))
    $msix = Join-Path $assets "PyDeck-$version-win-x64.msix"
    Invoke-Checked (Join-Path $sdkTools 'makeappx.exe') @('pack', '/d', $msixStage, '/p', $msix, '/h', 'SHA256', '/o')
    if ($certificate) {
        foreach ($package in @($msi, $msix)) {
            Invoke-Checked (Join-Path $sdkTools 'signtool.exe') @('sign', '/fd', 'SHA256', '/sha1', $CertificateThumbprint, '/s', 'My', $package)
        }
    }
    $metadata = [ordered]@{ version = $version; sourceCommit = $sourceCommit; sourceDirty = $sourceDirty; signed = [bool]$certificate; certificateThumbprint = $CertificateThumbprint; certificateExpires = $(if ($certificate) { $certificate.NotAfter.ToUniversalTime().ToString('o') } else { $null }); assets = $assets; payload = $payload }
    $metadata | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $output 'build.json') -Encoding utf8
    Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts\latest-release.txt') -Value $output -Encoding utf8
    Write-Output "Release packages: $assets"
    if ($sourceDirty) { Write-Warning 'Built from a working tree with changes. Commit and rebuild before public release.' }
    if (!$certificate) { Write-Warning 'Unsigned validation packages only; do not publish as installable MSIX.' }
} finally { Pop-Location }
