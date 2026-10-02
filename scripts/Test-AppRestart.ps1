param([Parameter(Mandatory)][string]$BuildDirectory)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$output = Join-Path $projectRoot ('artifacts\restart-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$app = Join-Path $BuildDirectory 'PyDeck.Launcher.exe'
$process = Start-Process -FilePath $app -ArgumentList @('--smoke-test', ('"' + $output + '"'), '--restart-only') -WorkingDirectory $BuildDirectory -WindowStyle Hidden -PassThru
$resultFile = Join-Path $output 'result.json'
$deadline = [DateTime]::UtcNow.AddSeconds(45)
while (!(Test-Path -LiteralPath $resultFile) -and [DateTime]::UtcNow -lt $deadline) { Start-Sleep -Milliseconds 100 }
if (!(Test-Path -LiteralPath $resultFile)) { throw "Restart verification timed out. Evidence: $output" }
# The restarted fixture writes the completed JSON before exiting; a sharing retry covers that final flush.
$result = $null
for ($attempt = 0; $attempt -lt 10; $attempt++) {
    try { $result = Get-Content -LiteralPath $resultFile -Raw | ConvertFrom-Json; break }
    catch { if ($attempt -eq 9) { throw }; Start-Sleep -Milliseconds 100 }
}
if (!$result.passed) { throw $result.error }
if (!$process.WaitForExit(5000)) { throw 'Original launcher did not exit after app restart' }
$restarted = Get-Process -Id $result.restartedPid -ErrorAction SilentlyContinue
if ($restarted -and !$restarted.WaitForExit(5000)) { throw 'Restarted isolated fixture did not exit' }
Write-Output "PASS actual Windows app restart: PID $($result.originalPid) -> $($result.restartedPid), style $($result.design)"
Write-Output "Isolated restart evidence: $output"
