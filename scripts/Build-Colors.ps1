[CmdletBinding()]
param([string]$OutputDirectory, [switch]$Checks)
$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest
$repoRoot = Split-Path -Parent $PSScriptRoot
if (!$OutputDirectory) { $OutputDirectory = Join-Path $repoRoot 'artifacts\native-colors' }
$output = [IO.Path]::GetFullPath($OutputDirectory)
$source = Join-Path $repoRoot 'src\PyDeck.Colors'
$vendor = Join-Path $source 'vendor\material-color-utilities'
New-Item -ItemType Directory -Path $output -Force | Out-Null
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vswhere -latest -products * -requires Microsoft.VisualStudio.Component.VC.Tools.x86.x64 -property installationPath
$toolset = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object Name -match '^10\.0\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$toolset -or !$sdk) { throw 'PyDeck color engine requires the installed MSVC x64 toolset and Windows SDK.' }
$compiler = Join-Path $toolset.FullName 'bin\Hostx64\x64\cl.exe'
$linker = Join-Path $toolset.FullName 'bin\Hostx64\x64\link.exe'
$dumpbin = Join-Path $toolset.FullName 'bin\Hostx64\x64\dumpbin.exe'
$inputFiles = @(Get-ChildItem -LiteralPath $source -Recurse -File | Where-Object Extension -in '.h','.cpp','.cc','.json') + @(Get-Item -LiteralPath $PSCommandPath,(Join-Path $repoRoot 'tests\Colors.Checks.cpp'))
$hashText = $compiler + '|' + $sdk.Name + '|' + (($inputFiles | Sort-Object FullName | ForEach-Object { $_.FullName + ':' + (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash }) -join '|')
$sourceHash = [Convert]::ToHexString([Security.Cryptography.SHA256]::HashData([Text.Encoding]::UTF8.GetBytes($hashText)))
$dll = Join-Path $output 'PyDeck.Colors.dll'
$stamp = Join-Path $output 'build.json'
$lockFile = $null
$deadline = [DateTime]::UtcNow.AddSeconds(120)
while (!$lockFile) {
    try { $lockFile = [IO.File]::Open((Join-Path $output 'build.lock'),[IO.FileMode]::OpenOrCreate,[IO.FileAccess]::ReadWrite,[IO.FileShare]::None) }
    catch [IO.IOException] { if ([DateTime]::UtcNow -ge $deadline) { throw 'Another native color build is still running.' }; Start-Sleep -Milliseconds 100 }
}
$previousPath = $env:PATH
try {
    $previous = if (Test-Path -LiteralPath $stamp) { Get-Content -LiteralPath $stamp -Raw | ConvertFrom-Json } else { $null }
    $upToDate = $previous -and $previous.sourceHash -eq $sourceHash -and (Test-Path -LiteralPath $dll) -and ((Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash -eq $previous.dllSha256)
    if ($upToDate -and (!$Checks -or $previous.checked)) { Write-Output "Native colors unchanged: $dll"; return }
    $work = Join-Path $output ('work\' + $sourceHash.Substring(0,16))
    New-Item -ItemType Directory -Path $work -Force | Out-Null
    $includes = @(('/I' + (Join-Path $toolset.FullName 'include')),('/I' + $source),('/I' + $vendor),('/I' + (Join-Path $source 'compat')))
    foreach ($part in @('ucrt','shared','um')) { $includes += '/I' + (Join-Path $sdk.FullName $part) }
    $libraries = @('/LIBPATH:' + (Join-Path $toolset.FullName 'lib\x64'))
    foreach ($part in @('ucrt','um')) { $libraries += '/LIBPATH:' + (Join-Path $sdkRoot ('Lib\' + $sdk.Name + '\' + $part + '\x64')) }
    $common = @('/nologo','/c','/std:c++20','/MT','/O2','/W3','/EHsc','/utf-8','/guard:cf','/D_USE_MATH_DEFINES','/DPYDECK_COLORS_BUILD','/DWIN32_LEAN_AND_MEAN','/DNOMINMAX') + $includes
    $linkOptions = @('/NOLOGO','/INCREMENTAL:NO','/DYNAMICBASE','/NXCOMPAT','/HIGHENTROPYVA','/guard:cf') + $libraries
    $env:PATH = (Split-Path -Parent $compiler) + ';' + $previousPath
    function Invoke-Native([string]$Label, [string]$File, [string[]]$Arguments) {
        $lines = & $File @Arguments 2>&1
        $code = $LASTEXITCODE
        $lines | Tee-Object -FilePath (Join-Path $work ($Label + '.log'))
        if ($code -ne 0) { throw "$Label failed: $code" }
    }
    if (!$upToDate) {
        $vendorSources = @(Get-ChildItem -LiteralPath (Join-Path $vendor 'cpp') -Recurse -Filter '*.cc' | Sort-Object FullName | Select-Object -ExpandProperty FullName)
        # Upstream uses bounded size_t->int conversions; retain its algorithms.
        Invoke-Native 'mcu-compile' $compiler ($common + @('/wd4244','/wd4267',"/Fo$work\") + $vendorSources)
        Invoke-Native 'adapter-compile' $compiler ($common + @('/W4','/WX',"/Fo$work\",(Join-Path $source 'AospSeed.cpp'),(Join-Path $source 'Colors.cpp')))
        $objects = @(Get-ChildItem -LiteralPath $work -Filter '*.obj' | Where-Object Name -ne 'Colors.Checks.obj' | Select-Object -ExpandProperty FullName)
        Invoke-Native 'dll-link' $linker ($linkOptions + @('/DLL',"/OUT:$dll","/IMPLIB:$output\PyDeck.Colors.lib") + $objects)
        $imports = & $dumpbin /nologo /dependents $dll
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect native color DLL imports.' }
        $dependencies = @($imports | Where-Object { $_ -match '^\s+([\w.-]+\.dll)\s*$' } | ForEach-Object { $_.Trim().ToLowerInvariant() })
        if (!$dependencies.Count -or @($dependencies | Where-Object { $_ -notin @('kernel32.dll') }).Count) { throw 'Color DLL must use only Windows system dependencies.' }
        $imports | Set-Content -LiteralPath (Join-Path $work 'imports.log') -Encoding utf8
    }
    if ($Checks) {
        $test = Join-Path $output 'Colors.Checks.exe'
        $testFlags = @($common | Where-Object { $_ -ne '/DPYDECK_COLORS_BUILD' }) + @('/W4','/WX',"/Fo$work\Colors.Checks.obj")
        Invoke-Native 'checks-compile' $compiler ($testFlags + @((Join-Path $repoRoot 'tests\Colors.Checks.cpp')))
        $testObjects = @(Get-ChildItem -LiteralPath $work -Filter '*.obj' | Where-Object Name -ne 'Colors.obj' | Select-Object -ExpandProperty FullName)
        Invoke-Native 'checks-link' $linker ($linkOptions + @('/SUBSYSTEM:CONSOLE',"/OUT:$test",(Join-Path $output 'PyDeck.Colors.lib')) + $testObjects)
        Invoke-Native 'checks' $test @()
    }
    [ordered]@{ sourceHash=$sourceHash; dllSha256=(Get-FileHash -LiteralPath $dll -Algorithm SHA256).Hash; checked=[bool]$Checks; output=$dll; evidence=$work; upstreamCommit='5b3618b16fdc3825e21d5679bafd144662088ea1'; specVersion='2021'; roleCount=49 } | ConvertTo-Json | Set-Content -LiteralPath $stamp -Encoding utf8
    Write-Output "Native color engine: $dll"
} finally { $env:PATH = $previousPath; if ($lockFile) { $lockFile.Dispose() } }
