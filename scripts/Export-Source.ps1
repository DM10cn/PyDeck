[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if (git status --porcelain --untracked-files=normal) { throw 'Commit reviewed changes before exporting release source.' }
    $release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
    $metadata = Get-Content -LiteralPath (Join-Path $release 'build.json') -Raw | ConvertFrom-Json
    $head = (git rev-parse HEAD).Trim()
    if ($metadata.sourceDirty -or $metadata.sourceCommit -ne $head) { throw 'Packages must be rebuilt from this clean commit before exporting source.' }
    $version = $metadata.version
    $destination = Join-Path $release 'assets'
    foreach ($format in @('zip', 'tar.gz')) {
        $archive = Join-Path $destination "PyDeck-$version-source.$format"
        git archive "--format=$format" "--prefix=PyDeck-$version/" "--output=$archive" $head
        if ($LASTEXITCODE -ne 0) { throw "Source $format export failed." }
    }
    Copy-Item -LiteralPath 'docs/INSTALL.md','docs/INSTALL.zh-CN.md' -Destination $destination
    Copy-Item -LiteralPath 'scripts/Test-Prerequisites.ps1' -Destination $destination
    $hashLines = Get-ChildItem -LiteralPath $destination -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    }
    [IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'), $hashLines, [Text.UTF8Encoding]::new($false))
    Write-Output "Release source and checksums: $destination"
} finally { Pop-Location }
