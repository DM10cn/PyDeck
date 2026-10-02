# Checks embedded payloads without executing any installer or changing runtime registrations.
[CmdletBinding()]
param([Parameter(Mandatory)][string]$SetupPath, [Parameter(Mandatory)][string]$MsiPath)
$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $PSScriptRoot
$exe = (Resolve-Path -LiteralPath $SetupPath).Path
$work = Join-Path $root ('artifacts\setup-verify-' + [Guid]::NewGuid().ToString('N'))
New-Item -ItemType Directory -Path $work | Out-Null
$stdout = Join-Path $work 'output.txt'
$stderr = Join-Path $work 'error.txt'
$process = Start-Process -FilePath $exe -ArgumentList '--verify-payloads' -PassThru -WindowStyle Hidden -RedirectStandardOutput $stdout -RedirectStandardError $stderr
if (!$process.WaitForExit(120000)) { throw "Payload verifier still running as process $($process.Id); no installers are launched by this mode." }
$process.Refresh()
if ($process.ExitCode -ne 0) { throw "Setup payload verification failed: $($process.ExitCode). See $stdout" }
$resultPath = (Get-Content -LiteralPath $stdout -Raw -Encoding utf8).Trim()
$result = Get-Content -LiteralPath $resultPath -Raw -Encoding utf8 | ConvertFrom-Json
if (!$result.verificationOnly -or $result.outcome -ne 'success' -or $result.exitCode -ne 0 -or @($result.payloads).Count -ne 4) { throw 'Incomplete setup verification.' }
$prerequisites = @(Get-Content -LiteralPath (Join-Path $root 'packaging/setup/prerequisites.json') -Raw | ConvertFrom-Json)
for ($i = 0; $i -lt 3; $i++) {
    if ($result.payloads[$i].file -ne $prerequisites[$i].file -or $result.payloads[$i].sha256 -ne $prerequisites[$i].sha256) { throw 'Embedded prerequisite differs from reviewed metadata.' }
}
if ($result.payloads[3].file -ne 'PyDeck.msi' -or $result.payloads[3].sha256 -ne (Get-FileHash -LiteralPath $MsiPath -Algorithm SHA256).Hash) { throw 'Embedded MSI differs from the standalone MSI.' }
Copy-Item -LiteralPath $resultPath -Destination (Join-Path $work 'result.json')
Write-Output 'PASS all three offline runtime installers and MSI verified by extraction and SHA256; no installer executed.'
Write-Output "Evidence: $work"
