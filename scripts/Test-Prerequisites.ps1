# Read-only prerequisite check; nothing is installed or downloaded.
[CmdletBinding()]
param()
$ErrorActionPreference = 'Stop'
$results = @()
$build = [int](Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\Windows NT\CurrentVersion').CurrentBuildNumber
$architecture = if ($env:PROCESSOR_ARCHITEW6432) { $env:PROCESSOR_ARCHITEW6432 } else { $env:PROCESSOR_ARCHITECTURE }
$results += [pscustomobject]@{ Dependency = 'Windows 11 x64'; Ready = ($build -ge 22000 -and $architecture -eq 'AMD64') }
$dotnet = Join-Path ${env:ProgramW6432} 'dotnet\dotnet.exe'
$netReady = $false
if (Test-Path -LiteralPath $dotnet) { $netReady = [bool](@(& $dotnet --list-runtimes) -match '^Microsoft\.NETCore\.App 10\.0\.\d+ ') }
$results += [pscustomobject]@{ Dependency = '.NET Runtime 10 x64'; Ready = $netReady }
$runtime = @(Get-AppxPackage -Name 'Microsoft.WindowsAppRuntime.2' | Where-Object { $_.Architecture -eq 'X64' -and [version]$_.Version -ge [version]'2.5.1.0' })
$results += [pscustomobject]@{ Dependency = 'Windows App Runtime 2.5.1+ x64'; Ready = ($runtime.Count -gt 0) }
$vc = Get-ItemProperty 'HKLM:\SOFTWARE\Microsoft\VisualStudio\14.0\VC\Runtimes\x64' -ErrorAction SilentlyContinue
$results += [pscustomobject]@{ Dependency = 'Visual C++ v14 Redistributable x64'; Ready = ($null -ne $vc -and $vc.Installed -eq 1) }
$manager = Get-Command pymanager.exe -ErrorAction SilentlyContinue
$results += [pscustomobject]@{ Dependency = 'Python Install Manager on PATH'; Ready = ($null -ne $manager) }
$results | Format-Table -AutoSize
if ($results.Ready -contains $false) { Write-Warning 'Check INSTALL.md for missing prerequisites. A manager outside PATH can be selected in PyDeck Settings.' }
