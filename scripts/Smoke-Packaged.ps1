# Runs the existing read-only GUI smoke checks through actual MSIX activation.
[CmdletBinding()]
param([string]$PackageName = 'DM10cn.PyDeck')
$ErrorActionPreference = 'Stop'
$repoRoot = Split-Path -Parent $PSScriptRoot
$package = Get-AppxPackage -Name $PackageName
if (!$package -or @($package).Count -ne 1) { throw 'Install or register exactly one matching MSIX package first.' }
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public static class PyDeckPackageActivation {
    [ComImport, Guid("2E941141-7F97-4756-BA1D-9DECDE894A3D"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface IActivationManager {
        [PreserveSig] int ActivateApplication([MarshalAs(UnmanagedType.LPWStr)] string id,
            [MarshalAs(UnmanagedType.LPWStr)] string arguments, uint options, out uint processId);
    }
    public static uint Start(string id, string arguments) {
        object instance = Activator.CreateInstance(Type.GetTypeFromCLSID(new Guid("45BA127D-10A8-46EA-8AB7-56EA9078943C")));
        try {
            uint processId;
            Marshal.ThrowExceptionForHR(((IActivationManager)instance).ActivateApplication(id, arguments, 2, out processId));
            return processId;
        } finally { Marshal.ReleaseComObject(instance); }
    }
}
'@
$output = Join-Path $repoRoot ('artifacts\smoke-msix-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$processId = [PyDeckPackageActivation]::Start(($package.PackageFamilyName + '!App'), ('--smoke-test "' + $output + '"'))
$process = Get-Process -Id $processId -ErrorAction SilentlyContinue
if ($process -and !$process.WaitForExit(60000)) { throw "Packaged smoke test is still running as process $processId" }
$resultPath = Join-Path $output 'result.json'
if (!(Test-Path -LiteralPath $resultPath)) { throw "Packaged GUI produced no result. Inspect activation/runtime errors for process $processId" }
$result = Get-Content -LiteralPath $resultPath -Raw | ConvertFrom-Json
if (!$result.passed) { throw $result.error }
$result.checks | ForEach-Object { Write-Output "PASS $_" }
Write-Output "Packaged GUI smoke check passed: $($package.PackageFullName)"
Write-Output "Local evidence: $output"
