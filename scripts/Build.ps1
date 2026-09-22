param(
    [ValidateSet('Debug', 'Release')][string]$Configuration = 'Release',
    [switch]$Publish,
    [switch]$Checks,
    [switch]$LiveChecks
)
$ErrorActionPreference = 'Stop'
$projectRoot = Split-Path -Parent $PSScriptRoot
$env:DOTNET_CLI_HOME = Join-Path $projectRoot '.local\dotnet'
$env:NUGET_PACKAGES = Join-Path $projectRoot '.local\packages'
$env:DOTNET_SKIP_FIRST_TIME_EXPERIENCE = '1'
$env:DOTNET_CLI_TELEMETRY_OPTOUT = '1'
$env:DOTNET_GENERATE_ASPNET_CERTIFICATE = 'false'
$env:DOTNET_ADD_GLOBAL_TOOLS_TO_PATH = 'false'
Push-Location $projectRoot
try {
    dotnet build src/PimGui.App/PimGui.App.csproj -c $Configuration -p:Platform=x64 --nologo
    if ($LASTEXITCODE -ne 0) { throw 'App build failed.' }
    if ($Checks -or $LiveChecks) {
        $checkArgs = @('run', '--project', 'tests/PimGui.Checks/PimGui.Checks.csproj', '-c', $Configuration)
        if ($LiveChecks) { $checkArgs += @('--', '--live') }
        & dotnet @checkArgs
        if ($LASTEXITCODE -ne 0) { throw 'Core checks failed.' }
    }
    if ($Publish) {
        $outputDirectory = Join-Path $projectRoot ('artifacts\dev-win-x64-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
        dotnet publish src/PimGui.App/PimGui.App.csproj -c $Configuration -p:Platform=x64 --self-contained false -o $outputDirectory --nologo
        if ($LASTEXITCODE -ne 0) { throw 'Development publish failed.' }
        $config = Get-Content -LiteralPath (Join-Path $outputDirectory 'PyDeck.runtimeconfig.json') -Raw | ConvertFrom-Json
        if ($config.runtimeOptions.framework.name -ne 'Microsoft.NETCore.App') { throw 'Expected an external .NET runtime reference.' }
        foreach ($forbidden in @('coreclr.dll', 'hostpolicy.dll', 'System.Private.CoreLib.dll', 'Microsoft.UI.Xaml.dll', 'onnxruntime.dll', 'DirectML.dll')) {
            if (Test-Path -LiteralPath (Join-Path $outputDirectory $forbidden)) { throw "Unexpected bundled runtime: $forbidden" }
        }
        if (!(Test-Path -LiteralPath (Join-Path $outputDirectory 'PyDeck.pri'))) { throw 'Compiled WinUI resources are missing.' }
        Set-Content -LiteralPath (Join-Path $projectRoot 'artifacts\latest-build.txt') -Value $outputDirectory -Encoding utf8
        Write-Output "Development build: $outputDirectory"
    }
} finally { Pop-Location }
