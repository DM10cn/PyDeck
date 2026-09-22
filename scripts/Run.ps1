$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$latestPath = Join-Path $projectRoot 'artifacts\latest-build.txt'
if (!(Test-Path -LiteralPath $latestPath)) { throw 'Run scripts/Build.ps1 -Publish first.' }
$directory = (Get-Content -LiteralPath $latestPath -Raw).Trim()
Start-Process -FilePath (Join-Path $directory 'PyDeck.exe') -WorkingDirectory $directory
