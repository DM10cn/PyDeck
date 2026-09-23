param([string]$BuildDirectory, [string]$OfflineFixture)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$BuildDirectory) { $BuildDirectory = (Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\latest-build.txt') -Raw).Trim() }
$appPath = Join-Path $BuildDirectory 'PyDeck.Launcher.exe'
if (!(Test-Path -LiteralPath $appPath)) { throw 'Build the app with scripts/Build.ps1 -Publish first.' }
$outputDirectory = Join-Path $projectRoot ('artifacts\smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$appArguments = @('--smoke-test', ('"' + $outputDirectory + '"'))
if ($OfflineFixture) { $appArguments += @('--offline-fixture', ('"' + $OfflineFixture + '"')) }
$appProcess = Start-Process -FilePath $appPath -ArgumentList $appArguments -WorkingDirectory $BuildDirectory -WindowStyle Hidden -PassThru
if (!$appProcess.WaitForExit(60000)) { throw "Smoke test is still running as process $($appProcess.Id). Output: $outputDirectory" }
if ($appProcess.ExitCode -ne 0) { throw "GUI exited with code $($appProcess.ExitCode)." }
$resultPath = Join-Path $outputDirectory 'result.json'
if (!(Test-Path -LiteralPath $resultPath)) { throw 'GUI did not produce a smoke-test result.' }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if (!$result.passed) { throw $result.error }
$result.checks | ForEach-Object { Write-Output "PASS $_" }
Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\latest-smoke.txt') -Value $outputDirectory -Encoding utf8
Write-Output "Screenshots and result: $outputDirectory"
