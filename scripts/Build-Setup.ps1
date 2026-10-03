[CmdletBinding()]
param(
    [Parameter(Mandatory)][string]$MsiPath,
    [Parameter(Mandatory)][string]$OutputDirectory,
    [string]$CacheDirectory,
    [switch]$AllowUnsignedMsi,
    [switch]$Checks
)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$CacheDirectory) { $CacheDirectory = Join-Path $repoRoot '.local\setup-prerequisites' }
$cache = [IO.Path]::GetFullPath($CacheDirectory)
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $cache, $output -Force | Out-Null
$msi = (Resolve-Path -LiteralPath $MsiPath).Path
if ([IO.Path]::GetExtension($msi) -ne '.msi') { throw 'A PyDeck MSI is required.' }
$installer = New-Object -ComObject WindowsInstaller.Installer
$database = $installer.OpenDatabase($msi, 0)
function Read-Property([string]$Name) {
    $view = $database.OpenView(('SELECT `Value` FROM `Property` WHERE `Property` = ''' + $Name + ''''))
    try { [void]$view.Execute(); $record = $view.Fetch(); if ($record) { $record.StringData(1) } }
    finally { [void]$view.Close() }
}
try {
    if ((Read-Property 'ProductName') -ne 'PyDeck' -or (Read-Property 'UpgradeCode') -ne '{D43AFAF7-DFE0-4AC1-A0A3-8F73AD6F89CA}') { throw 'Unexpected MSI identity.' }
    $version = Read-Property 'ProductVersion'
    $productCode = Read-Property 'ProductCode'
    if ($productCode -notmatch '^\{[A-Fa-f0-9-]{36}\}$') { throw 'Invalid embedded MSI product code.' }
} finally { [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($database); [void][Runtime.InteropServices.Marshal]::FinalReleaseComObject($installer) }
$msiSignature = Get-AuthenticodeSignature -LiteralPath $msi
if (!$AllowUnsignedMsi -and (!$msiSignature.SignerCertificate -or $msiSignature.Status -in 'HashMismatch','NotSigned')) { throw 'Sign the MSI first, or explicitly use -AllowUnsignedMsi for local checks.' }
[xml]$project = Get-Content -LiteralPath (Join-Path $repoRoot 'src/PimGui.App/PimGui.App.csproj') -Raw
if ($version -ne [string]$project.Project.PropertyGroup.Version) { throw 'MSI version does not match this source checkout.' }
$label = [string]$project.Project.PropertyGroup.InformationalVersion
if ($label -notmatch '^\d+\.\d+\.\d+(?:-[A-Za-z0-9][A-Za-z0-9.-]*)?$') { throw 'Invalid setup version label.' }
$dependencies = @(Get-Content -LiteralPath (Join-Path $repoRoot 'packaging/setup/prerequisites.json') -Raw | ConvertFrom-Json)
if ($dependencies.Count -ne 3) { throw 'Expected .NET, Windows App Runtime, and Visual C++ in that order.' }
$payloads = @()
$names = @('.NET Runtime 10 x64', 'Windows App Runtime 2.5.1+ x64', 'Visual C++ v14 x64')
for ($i = 0; $i -lt $dependencies.Count; $i++) {
    $dependency = $dependencies[$i]
    $uri = [uri]$dependency.url
    if ($uri.Scheme -ne 'https' -or $uri.Host -notin 'builds.dotnet.microsoft.com','aka.ms' -or
        $dependency.file -notmatch '^[A-Za-z0-9_.-]+\.exe$' -or $dependency.sha256 -notmatch '^[a-f0-9]{64}$') { throw 'Invalid pinned prerequisite metadata.' }
    $path = Join-Path $cache $dependency.file
    if (!(Test-Path -LiteralPath $path)) {
        $partial = Join-Path $cache ($dependency.file + '.' + [Guid]::NewGuid().ToString('N') + '.download')
        try {
            Invoke-WebRequest -Uri $dependency.url -OutFile $partial
            if ((Get-FileHash -LiteralPath $partial -Algorithm SHA256).Hash -ne $dependency.sha256) { throw "Downloaded prerequisite changed: $($dependency.file). Review and pin new metadata before building." }
            Move-Item -LiteralPath $partial -Destination $path
        } finally { if (Test-Path -LiteralPath $partial) { Remove-Item -LiteralPath $partial } }
    }
    if ((Get-Item -LiteralPath $path).Length -ne $dependency.length -or (Get-FileHash -LiteralPath $path -Algorithm SHA256).Hash -ne $dependency.sha256) { throw "Prerequisite hash mismatch: $($dependency.file)" }
    $signature = Get-AuthenticodeSignature -LiteralPath $path
    if ($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch '(^|, )O=Microsoft Corporation(,|$)') { throw "Prerequisite is not validly Microsoft-signed: $($dependency.file)" }
    $payloads += [pscustomobject]@{id=301+$i;file=$dependency.file;path=$path;label=$names[$i];sha256=$dependency.sha256}
}
$payloads += [pscustomobject]@{id=304;file='PyDeck.msi';path=$msi;label='PyDeck MSI';sha256=(Get-FileHash -LiteralPath $msi -Algorithm SHA256).Hash.ToLowerInvariant()}
$work = Join-Path $repoRoot ('artifacts\setup-build-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$header = @('#pragma once', '#include <array>', 'struct PayloadInfo { int id; const wchar_t* file; const wchar_t* label; const wchar_t* sha256; };', ('constexpr const wchar_t* SetupVersion = L"' + $label + '";'), ('constexpr const wchar_t* PayloadProductCode = L"' + $productCode + '";'), ('constexpr const wchar_t* PayloadProductVersion = L"' + $version + '";'), 'constexpr std::array<PayloadInfo, 4> Payloads{{')
$resources = @('#include <windows.h>', '#pragma code_page(65001)')
foreach ($payload in $payloads) {
    $header += ('    {{{0}, L"{1}", L"{2}", L"{3}"}},' -f $payload.id, $payload.file, $payload.label, $payload.sha256)
    $resources += ('{0} RCDATA "{1}"' -f $payload.id, $payload.path.Replace('\','\\'))
}
$header += '}};'
[IO.File]::WriteAllLines((Join-Path $work 'Payloads.h'), $header, [Text.UTF8Encoding]::new($false))
[IO.File]::WriteAllLines((Join-Path $work 'Payloads.rc'), $resources, [Text.UTF8Encoding]::new($false))
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$toolset = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object Name -match '^10\.0\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$compiler = Join-Path $toolset.FullName 'bin\Hostx64\x64\cl.exe'
$dumpbin = Join-Path $toolset.FullName 'bin\Hostx64\x64\dumpbin.exe'
$rc = Join-Path $sdkRoot ('bin\' + $sdk.Name + '\x64\rc.exe')
$includes = @(('/I' + (Join-Path $toolset.FullName 'include')), ('/I' + $work))
foreach ($part in @('ucrt','shared','um')) { $includes += '/I' + (Join-Path $sdk.FullName $part) }
$libraries = @('/LIBPATH:' + (Join-Path $toolset.FullName 'lib\x64'))
foreach ($part in @('ucrt','um')) { $libraries += '/LIBPATH:' + (Join-Path $sdkRoot ('Lib\' + $sdk.Name + '\' + $part + '\x64')) }
$common = @('/nologo','/std:c++20','/MT','/O2','/W4','/WX','/utf-8','/EHsc','/guard:cf','/DUNICODE','/D_UNICODE','/D_WIN32_WINNT=0x0A00') + $includes
$link = @('/link','/INCREMENTAL:NO','/DYNAMICBASE','/NXCOMPAT','/HIGHENTROPYVA','/guard:cf') + $libraries + @('user32.lib','advapi32.lib','shell32.lib','comctl32.lib','bcrypt.lib','ole32.lib','uuid.lib','msi.lib','gdi32.lib')
function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed: $LASTEXITCODE" }
}
$previousPath = $env:PATH
$env:PATH = (Split-Path -Parent $rc) + ';' + (Split-Path -Parent $compiler) + ';' + $previousPath
Push-Location (Join-Path $repoRoot 'src/PyDeck.Setup')
try {
    $uiResource = Join-Path $work 'Setup.res'
    $payloadResource = Join-Path $work 'Payloads.res'
    Invoke-Native $rc (@('/nologo','/fo',$uiResource) + $includes + @('Setup.rc'))
    Invoke-Native $rc (@('/nologo','/fo',$payloadResource) + $includes + @((Join-Path $work 'Payloads.rc')))
    $exe = Join-Path $output "PyDeck-Setup-$label-win-x64.exe"
    Invoke-Native $compiler ($common + @('Setup.cpp',$uiResource,$payloadResource,"/Fo$work\Setup.obj","/Fe$exe") + $link + @('/SUBSYSTEM:WINDOWS','/MANIFEST:EMBED','/MANIFESTINPUT:Setup.manifest'))
    $imports = & $dumpbin /nologo /dependents $exe
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect setup imports.' }
    $dlls = @($imports | Where-Object { $_ -match '^\s+([\w.-]+\.dll)\s*$' } | ForEach-Object { $_.Trim().ToLowerInvariant() })
    $allowed = @('kernel32.dll','user32.dll','advapi32.dll','shell32.dll','comctl32.dll','bcrypt.dll','ole32.dll','gdi32.dll','msi.dll')
    if (!$dlls.Count -or @($dlls | Where-Object { $_ -notin $allowed }).Count) { throw ('Unexpected setup DLL dependency: ' + ($dlls -join ', ')) }
    Write-Output ('PASS Setup requires only Windows system DLLs: ' + ($dlls -join ', '))
    if ($Checks) {
        $test = Join-Path $work 'Setup.Checks.exe'
        Invoke-Native $compiler ($common + @('/wd4505',(Join-Path $repoRoot 'tests/Setup.Checks.cpp'),$uiResource,$payloadResource,"/Fo$work\Checks.obj","/Fe$test") + $link + @('/SUBSYSTEM:CONSOLE','/MANIFEST:EMBED','/MANIFESTINPUT:Setup.manifest'))
        Invoke-Native $test @()
    }
    [ordered]@{version=$version;releaseLabel=$label;setup=$exe;msi=$msi;msiSha256=$payloads[3].sha256;prerequisites=$dependencies;sourceCommit=(git -C $repoRoot rev-parse HEAD).Trim();sourceDirty=[bool](git -C $repoRoot status --porcelain);checked=[bool]$Checks} |
        ConvertTo-Json -Depth 5 | Set-Content -LiteralPath (Join-Path $work 'setup-build.json') -Encoding utf8
    Set-Content -LiteralPath (Join-Path $repoRoot 'artifacts/latest-setup.txt') -Value $exe -Encoding utf8
    Write-Output "Setup: $exe"
    Write-Output "Evidence: $work\setup-build.json"
} finally { Pop-Location; $env:PATH = $previousPath }
