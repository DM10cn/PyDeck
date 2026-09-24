[CmdletBinding()]
param([Parameter(Mandatory)][string]$ReleaseDirectory)
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
Push-Location $repoRoot
try {
    if (git status --porcelain --untracked-files=normal) { throw 'Commit reviewed changes before preparing release assets.' }
    $release = (Resolve-Path -LiteralPath $ReleaseDirectory).Path
    $metadata = Get-Content -LiteralPath (Join-Path $release 'build.json') -Raw | ConvertFrom-Json
    $head = (git rev-parse HEAD).Trim()
    if ($metadata.sourceDirty -or $metadata.sourceCommit -ne $head) { throw 'Packages must be rebuilt from this clean commit before preparing release assets.' }
    $destination = Join-Path $release 'assets'
    # GitHub provides source archives from the release tag. Keep this script name
    # for existing release commands, but never produce or checksum duplicate archives.
    if (Get-ChildItem -LiteralPath $destination -File | Where-Object Name -Match '^PyDeck-.+-source\.(zip|tar\.gz)$') {
        throw 'Duplicate source archives found in assets. Move them out before publication; use GitHub-generated source links.'
    }
    Copy-Item -LiteralPath 'docs/INSTALL.md','docs/INSTALL.zh-CN.md' -Destination $destination
    Copy-Item -LiteralPath 'scripts/Test-Prerequisites.ps1' -Destination $destination
    $hashLines = Get-ChildItem -LiteralPath $destination -File | Where-Object Name -ne 'SHA256SUMS.txt' | Sort-Object Name | ForEach-Object {
        (Get-FileHash -LiteralPath $_.FullName -Algorithm SHA256).Hash.ToLowerInvariant() + '  ' + $_.Name
    }
    [IO.File]::WriteAllLines((Join-Path $destination 'SHA256SUMS.txt'), $hashLines, [Text.UTF8Encoding]::new($false))
    Write-Output "Release guides and checksums: $destination (source archives are provided by GitHub)"
} finally { Pop-Location }
