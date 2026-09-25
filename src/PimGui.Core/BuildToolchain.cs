using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PimGui.Core;

public sealed record BuildToolchain(string VisualStudio, string MSBuild, string PlatformToolset, string CompilerVersion, string SdkVersion, string BootstrapPython)
{
    public void Validate()
    {
        if (PlatformToolset is not ("v141" or "v142" or "v143" or "v145") || !Regex.IsMatch(SdkVersion, @"\A10\.0\.\d+\.0\z") || !Regex.IsMatch(CompilerVersion, @"\A14\.[12345]\d\.\d+\z"))
            throw new ArgumentException("Unsupported compiler or Windows SDK");
        foreach (var path in new[] { MSBuild, BootstrapPython })
            if (!File.Exists(BuildRecipe.BatchPath(path))) throw new FileNotFoundException("Build dependency missing", path);
    }

    public static async Task<BuildToolchain> DetectAsync(string bootstrapPython, CancellationToken token = default)
        => (await DetectAllAsync(bootstrapPython, token))[0];

    public static async Task<IReadOnlyList<BuildToolchain>> DetectAllAsync(string bootstrapPython, CancellationToken token = default)
    {
        var results = new List<BuildToolchain>();
        if (!OperatingSystem.IsWindows() || RuntimeInformation.OSArchitecture != Architecture.X64)
            throw new PlatformNotSupportedException("Build Python requires Windows x64");
        BuildRecipe.BatchPath(bootstrapPython);
        var runner = new ProcessRunner();
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token); timeout.CancelAfter(TimeSpan.FromSeconds(30));
        var probe = await runner.RunAsync(bootstrapPython, ["-I", "-S", "-c", "import sys; assert (3,10) <= sys.version_info[:2] < (4,0); print('PYDECK_BOOTSTRAP_OK')"], cancellationToken: timeout.Token);
        if (probe.ExitCode != 0 || !probe.Output.Contains("PYDECK_BOOTSTRAP_OK")) throw new IOException("Choose an existing Python 3.10 or newer for build tools");
        var finder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Microsoft Visual Studio", "Installer", "vswhere.exe");
        if (!File.Exists(finder)) throw new IOException("Install Visual Studio C++ desktop tools and a Windows SDK");
        var found = await runner.RunAsync(finder, ["-products", "*", "-requires", "Microsoft.VisualStudio.Component.VC.Tools.x86.x64", "-format", "json", "-utf8"], cancellationToken: timeout.Token);
        if (found.ExitCode != 0) throw new IOException("Visual Studio detection failed");
        using var instances = JsonDocument.Parse(found.Output);
        var sdkRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86), "Windows Kits", "10");
        var sdk = Directory.Exists(Path.Combine(sdkRoot, "Include")) ? Directory.EnumerateDirectories(Path.Combine(sdkRoot, "Include"))
            .Select(Path.GetFileName).Where(v => v is not null && Regex.IsMatch(v, @"\A10\.0\.\d+\.0\z") &&
                File.Exists(Path.Combine(sdkRoot, "Include", v, "um", "Windows.h")) && File.Exists(Path.Combine(sdkRoot, "Include", v, "ucrt", "stdio.h")) &&
                File.Exists(Path.Combine(sdkRoot, "Lib", v, "um", "x64", "kernel32.lib")) && File.Exists(Path.Combine(sdkRoot, "Lib", v, "ucrt", "x64", "ucrt.lib")) &&
                File.Exists(Path.Combine(sdkRoot, "bin", v, "x64", "rc.exe")))
            .OrderByDescending(v => System.Version.Parse(v!)).ToArray() : [];
        if (sdk.Length == 0) throw new IOException("Install a Windows 10 or 11 SDK with x64 libraries");
        foreach (var instance in instances.RootElement.EnumerateArray())
        {
            var root = instance.GetProperty("installationPath").GetString()!;
            var msbuild = Path.Combine(root, "MSBuild", "Current", "Bin", "MSBuild.exe");
            if (!File.Exists(msbuild)) msbuild = Path.Combine(root, "MSBuild", "15.0", "Bin", "MSBuild.exe");
            var compilerRoot = Path.Combine(root, "VC", "Tools", "MSVC");
            if (!File.Exists(msbuild) || !Directory.Exists(compilerRoot)) continue;
            foreach (var compiler in Directory.EnumerateDirectories(compilerRoot).OrderByDescending(p => p, StringComparer.Ordinal))
            {
                var version = Path.GetFileName(compiler);
                var toolset = version.StartsWith("14.3", StringComparison.Ordinal) || version.StartsWith("14.4", StringComparison.Ordinal) ? "v143"
                    : version.StartsWith("14.5", StringComparison.Ordinal) ? "v145"
                    : version.StartsWith("14.2", StringComparison.Ordinal) ? "v142"
                    : version.StartsWith("14.1", StringComparison.Ordinal) ? "v141" : "";
                if (toolset.Length == 0 || !File.Exists(Path.Combine(compiler, "bin", "Hostx64", "x64", "cl.exe")) ||
                    !File.Exists(Path.Combine(compiler, "lib", "x64", "vcruntime.lib"))) continue;
                foreach (var sdkVersion in sdk)
                {
                    var result = new BuildToolchain(root, msbuild, toolset, version, sdkVersion!, bootstrapPython);
                    result.Validate(); results.Add(result);
                }
            }
        }
        if (results.Count == 0) throw new IOException("Install MSVC C++ x64 tools in Visual Studio Installer");
        return results;
    }
}
