param([string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
if (!$BuildDirectory) { $BuildDirectory = (Get-Content -LiteralPath (Join-Path $projectRoot 'artifacts\latest-build.txt') -Raw).Trim() }
$appPath = Join-Path $BuildDirectory 'PyDeck.Launcher.exe'
if (!(Test-Path -LiteralPath $appPath)) { throw 'Build the app with scripts/Build.ps1 -Publish first.' }
$outputDirectory = Join-Path $projectRoot ('artifacts\window-chrome-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$results = @()
foreach ($design in @('Fluent', 'Material')) {
    foreach ($systemTitleBar in @($false, $true)) {
        $caseDirectory = Join-Path $outputDirectory ($design + '-' + $systemTitleBar.ToString().ToLowerInvariant())
        $profileDirectory = Join-Path $caseDirectory 'preferences'
        New-Item -ItemType Directory -Path $profileDirectory -Force | Out-Null
        $initialSettings = @{ Design = $design; Theme = 'Dark'; Language = 'zh-CN'; Transparency = 'Off'; UseSystemTitleBar = $systemTitleBar }
        $initialSettings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $profileDirectory 'settings.json') -Encoding utf8
        $initialSettings | ConvertTo-Json | Set-Content -LiteralPath (Join-Path $caseDirectory 'initial-settings.json') -Encoding utf8
        $appArguments = @('--smoke-test', ('"' + $caseDirectory + '"'), '--workspace-only', '--window-chrome-only')
        $appProcess = Start-Process -FilePath $appPath -ArgumentList $appArguments -WorkingDirectory $BuildDirectory -WindowStyle Hidden -PassThru
        if (!$appProcess.WaitForExit(60000)) { throw "Window chrome probe is still running as process $($appProcess.Id). Output: $caseDirectory" }
        if ($appProcess.ExitCode -ne 0) { throw "Window chrome probe exited with code $($appProcess.ExitCode): $caseDirectory" }
        $result = Get-Content -LiteralPath (Join-Path $caseDirectory 'result.json') -Raw | ConvertFrom-Json
        if (!$result.passed) { throw $result.error }
        $results += @{ design = $design; systemTitleBar = $systemTitleBar; passed = $true; result = (Join-Path $caseDirectory 'window-chrome.json') }
        Write-Output "PASS $design / UseSystemTitleBar=$systemTitleBar"
    }
}
@{ passed = $true; build = $BuildDirectory; cases = $results } | ConvertTo-Json -Depth 5 |
    Set-Content -LiteralPath (Join-Path $outputDirectory 'result.json') -Encoding utf8
Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\latest-window-chrome.txt') -Value $outputDirectory -Encoding utf8
Write-Output "Window chrome results: $outputDirectory"
