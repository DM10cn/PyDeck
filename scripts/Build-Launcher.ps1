[CmdletBinding()]
param([Parameter(Mandatory)][string]$OutputDirectory, [switch]$Checks, [string]$InstallerActionsDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$vswhere = Join-Path ${env:ProgramFiles(x86)} 'Microsoft Visual Studio\Installer\vswhere.exe'
$vsRoot = & $vswhere -latest -products * -property installationPath
$toolset = Get-ChildItem -LiteralPath (Join-Path $vsRoot 'VC\Tools\MSVC') -Directory | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$toolset) { throw 'Install the Visual Studio MSVC x64/x86 build tools component.' }
$sdkRoot = Join-Path ${env:ProgramFiles(x86)} 'Windows Kits\10'
$sdk = Get-ChildItem -LiteralPath (Join-Path $sdkRoot 'Include') -Directory | Where-Object Name -match '^10\.0\.\d+\.\d+$' | Sort-Object { [version]$_.Name } -Descending | Select-Object -First 1
if (!$sdk) { throw 'Install the Windows SDK desktop C++ headers and libraries.' }
$compiler = Join-Path $toolset.FullName 'bin\Hostx64\x64\cl.exe'
$dumpbin = Join-Path $toolset.FullName 'bin\Hostx64\x64\dumpbin.exe'
$rc = Join-Path $sdkRoot ('bin\' + $sdk.Name + '\x64\rc.exe')
$output = [IO.Path]::GetFullPath($OutputDirectory)
New-Item -ItemType Directory -Path $output -Force | Out-Null
$work = Join-Path $repoRoot ('artifacts\launcher-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
function Invoke-Native([string]$File, [string[]]$Arguments) {
    & $File @Arguments
    if ($LASTEXITCODE -ne 0) { throw "$File failed: $LASTEXITCODE" }
}
$includes = @('/I' + (Join-Path $toolset.FullName 'include'))
foreach ($part in @('ucrt', 'shared', 'um')) { $includes += '/I' + (Join-Path $sdk.FullName $part) }
$libraries = @('/LIBPATH:' + (Join-Path $toolset.FullName 'lib\x64'))
foreach ($part in @('ucrt', 'um')) { $libraries += '/LIBPATH:' + (Join-Path $sdkRoot ('Lib\' + $sdk.Name + '\' + $part + '\x64')) }
$common = @('/nologo', '/std:c++20', '/MT', '/O2', '/W4', '/WX', '/utf-8', '/EHsc', '/guard:cf', '/DUNICODE', '/D_UNICODE', '/D_WIN32_WINNT=0x0A00') + $includes
$link = @('/link', '/INCREMENTAL:NO', '/DYNAMICBASE', '/NXCOMPAT', '/HIGHENTROPYVA', '/guard:cf') + $libraries + @('user32.lib', 'advapi32.lib', 'shell32.lib', 'comctl32.lib')
$previousPath = $env:PATH
$env:PATH = (Split-Path -Parent $rc) + ';' + (Split-Path -Parent $compiler) + ';' + $previousPath
Push-Location (Join-Path $repoRoot 'src\PyDeck.Launcher')
try {
    $resource = Join-Path $work 'Launcher.res'
    Invoke-Native $rc (@('/nologo', '/fo', $resource) + $includes + @('Launcher.rc'))
    $exe = Join-Path $output 'PyDeck.Launcher.exe'
    Invoke-Native $compiler ($common + @('Launcher.cpp', $resource, "/Fo$work\Launcher.obj", "/Fe$exe") + $link + @('/SUBSYSTEM:WINDOWS', '/MANIFEST:EMBED', '/MANIFESTINPUT:Launcher.manifest'))
    $imports = & $dumpbin /nologo /dependents $exe
    if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect native launcher imports.' }
    $dlls = @($imports | Where-Object { $_ -match '^\s+([\w.-]+\.dll)\s*$' } | ForEach-Object { $_.Trim().ToLowerInvariant() })
    $allowed = @('kernel32.dll', 'user32.dll', 'advapi32.dll', 'shell32.dll', 'comctl32.dll', 'gdi32.dll')
    if (!$dlls.Count -or @($dlls | Where-Object { $_ -notin $allowed }).Count) { throw ('Unexpected launcher DLL dependency: ' + ($dlls -join ', ')) }
    Write-Output ('PASS native launcher imports only Windows system DLLs: ' + ($dlls -join ', '))
    if ($InstallerActionsDirectory) {
        $actionsDirectory = [IO.Path]::GetFullPath($InstallerActionsDirectory)
        New-Item -ItemType Directory -Path $actionsDirectory -Force | Out-Null
        $actions = Join-Path $actionsDirectory 'PyDeck.InstallerActions.dll'
        Invoke-Native $compiler ($common + @('/LD', (Join-Path $repoRoot 'packaging\msi\InstallerActions.cpp'), "/Fo$work\InstallerActions.obj", "/Fe$actions") + $link + @('msi.lib', 'ole32.lib', 'uuid.lib', "/IMPLIB:$work\InstallerActions.lib"))
        $actionImports = & $dumpbin /nologo /dependents $actions
        if ($LASTEXITCODE -ne 0) { throw 'Cannot inspect native installer action imports.' }
        $actionDlls = @($actionImports | Where-Object { $_ -match '^\s+([\w.-]+\.dll)\s*$' } | ForEach-Object { $_.Trim().ToLowerInvariant() })
        if (!$actionDlls.Count -or @($actionDlls | Where-Object { $_ -notin ($allowed + @('msi.dll', 'ole32.dll')) }).Count) { throw 'Installer UI actions must import only Windows system DLLs.' }
        Write-Output 'PASS native MSI folder browser and option actions require no external runtimes'
    }
    if ($Checks) {
        $test = Join-Path $work 'Launcher.Checks.exe'
        Invoke-Native $compiler ($common + @('/wd4505', (Join-Path $repoRoot 'tests\Launcher.Checks.cpp'), $resource, "/Fo$work\Checks.obj", "/Fe$test") + $link + @('/SUBSYSTEM:CONSOLE', '/MANIFEST:EMBED', '/MANIFESTINPUT:Launcher.manifest'))
        Invoke-Native $test @()
    }
    Write-Output "Native launcher: $exe"
} finally { Pop-Location; $env:PATH = $previousPath }
