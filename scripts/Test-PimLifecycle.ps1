[CmdletBinding()]
param([string]$SeedBundleDirectory, [string]$InstallerDirectory)
$ErrorActionPreference='Stop'
$repo=Split-Path -Parent $PSScriptRoot
$output=Join-Path $repo ('artifacts\pim-e2e-'+(Get-Date -Format 'yyyyMMdd-HHmmss'))
New-Item -ItemType Directory -Path $output | Out-Null
$env:DOTNET_CLI_HOME=Join-Path $repo '.local\dotnet'
$env:NUGET_PACKAGES=Join-Path $repo '.local\packages'
dotnet publish (Join-Path $repo 'tests\PimGui.E2E') -c Release --self-contained false -o (Join-Path $output 'harness') --nologo
if($LASTEXITCODE -ne 0){throw 'E2E build failed'}
foreach($version in @('25.2','26.3')) {
    $package=Join-Path $output "pim-$version.msi"
    if($InstallerDirectory) { Copy-Item -LiteralPath (Join-Path $InstallerDirectory "pim-$version.msi") -Destination $package }
    else {
        curl.exe --silent --show-error --fail --location --retry 3 --output $package "https://www.python.org/ftp/python/pymanager/python-manager-$version.msi"
        if($LASTEXITCODE -ne 0){throw 'Official PIM download failed'}
    }
    $signature=Get-AuthenticodeSignature -LiteralPath $package
    if($signature.Status -ne 'Valid' -or $signature.SignerCertificate.Subject -notmatch 'O=Python Software Foundation,'){throw 'PIM publisher verification failed'}
}
$fixture=Join-Path $output 'seed-fixture'
New-Item -ItemType Directory -Path $fixture | Out-Null
if($SeedBundleDirectory) {
    Copy-Item -LiteralPath (Join-Path $SeedBundleDirectory 'index.json') -Destination $fixture
    $index=Get-Content -LiteralPath (Join-Path $fixture 'index.json') -Raw | ConvertFrom-Json
    foreach($runtime in $index.versions) {
        if($runtime.url -ne [IO.Path]::GetFileName($runtime.url) -or !$runtime.url.EndsWith('.zip')){throw 'Seed must be a flat local ZIP bundle'}
        Copy-Item -LiteralPath (Join-Path $SeedBundleDirectory $runtime.url) -Destination $fixture
    }
} else {
    $package=Get-AppxPackage '*PythonManager*' | Select-Object -First 1
    if(!$package){throw 'Supply an official older Python offline bundle with -SeedBundleDirectory'}
    $bundled=Join-Path $package.InstallLocation 'bundled'
    $index=Get-Content -LiteralPath (Join-Path $bundled 'fallback-index.json') -Raw | ConvertFrom-Json
    foreach($runtime in $index.versions) {
        $archive="$($runtime.id)-$($runtime.'sort-version').zip"
        Copy-Item -LiteralPath (Join-Path $bundled $archive) -Destination $fixture
        $runtime.url=$archive
    }
    $index | ConvertTo-Json -Depth 40 | Set-Content (Join-Path $fixture 'index.json') -Encoding utf8
}
$inside=@'
param([string]$HostComputer)
$ErrorActionPreference='Stop'
if($env:COMPUTERNAME -eq $HostComputer){throw 'Refusing lifecycle tests on the host'}
try {
    New-Item -ItemType Directory C:\PyDeckE2E -Force | Out-Null
    Set-Content C:\PyDeckE2E\ISOLATED $env:COMPUTERNAME
    foreach($version in @('25.2','26.3')) {
        $installer=Start-Process msiexec.exe -ArgumentList @('/i',"C:\PyDeckResults\pim-$version.msi",'/qn','/norestart','/L*v',"C:\PyDeckResults\pim-$version-install.log") -WindowStyle Hidden -PassThru
        if(!$installer.WaitForExit(180000) -or $installer.ExitCode -notin @(0,3010)){throw "PIM $version setup failed"}
        $manager='C:\Program Files\PyManager\pymanager.exe'
        if(!(Test-Path $manager)){throw 'PIM MSI installation layout changed'}
        $phase=if($version -eq '25.2'){'seed'}else{'upgraded'}
        & C:\HostDotnet\dotnet.exe C:\PyDeckResults\harness\PimGui.E2E.dll $manager $phase "C:\PyDeckResults\$phase.json" > "C:\PyDeckResults\$phase-output.txt" 2>&1
        if($LASTEXITCODE -ne 0){throw "PIM $phase checks failed"}
    }
    Set-Content C:\PyDeckResults\complete.txt 'passed'
} catch { $_ | Out-String | Set-Content C:\PyDeckResults\failure.txt; exit 1 }
'@
Set-Content (Join-Path $output 'run.ps1') $inside -Encoding utf8
$sandboxId=$null
try {
    $created=(& wsb start --config '<Configuration><VGpu>Disable</VGpu><ClipboardRedirection>Disable</ClipboardRedirection><MemoryInMB>4096</MemoryInMB></Configuration>' --raw | Out-String) | ConvertFrom-Json
    if(!$created.Id){throw 'Windows Sandbox did not start'}
    $sandboxId=([guid]$created.Id).ToString()
    wsb share --id $sandboxId -f $output -s C:\PyDeckResults --allow-write --raw
    if($LASTEXITCODE -ne 0){throw 'Sandbox result mapping failed'}
    $dotnetRoot=Split-Path -Parent (Get-Command dotnet.exe).Source
    wsb share --id $sandboxId -f $dotnetRoot -s C:\HostDotnet --raw
    if($LASTEXITCODE -ne 0){throw 'Sandbox test runtime mapping failed'}
    $command='powershell.exe -NoProfile -ExecutionPolicy Bypass -File C:\PyDeckResults\run.ps1 -HostComputer '+$env:COMPUTERNAME
    wsb exec --id $sandboxId --run-as System --command $command --raw
    if(!(Test-Path (Join-Path $output 'complete.txt'))){throw "Lifecycle checks did not complete: $output"}
    Write-Output "PASS isolated lifecycle acceptance: $output"
} finally {
    if($sandboxId){wsb stop --id $sandboxId --raw}
    Write-Output "Evidence: $output"
}
