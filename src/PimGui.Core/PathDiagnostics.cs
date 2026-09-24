using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using Microsoft.Win32.SafeHandles;

namespace PimGui.Core;

public sealed record CommandResolution(string Command, string? Path, string? Version, bool MatchesDefault, IReadOnlyList<string> Candidates, string? ActualPath = null);
public sealed record PathReport(string? DefaultVersion, string? DefaultPath, IReadOnlyList<CommandResolution> Commands, bool PathChanged, bool WindowsAppsMissing, bool GlobalAliasesMissing);

public static class PathDiagnostics
{
    public static async Task<PathReport> ProbeKnownAsync(PathReport report, IReadOnlyList<PythonRuntime> runtimes, string? manager)
    {
        var results = new List<CommandResolution>();
        var managerTarget = AliasTarget(manager);
        foreach (var entry in report.Commands)
        {
            var target = AliasTarget(entry.Path);
            bool trustedAlias = managerTarget is not null && target is not null &&
                Path.GetDirectoryName(target)!.Equals(Path.GetDirectoryName(managerTarget), StringComparison.OrdinalIgnoreCase);
            if (entry.Path is null || entry.Command == "pymanager" || (!trustedAlias && !runtimes.Any(r => r.Executable.Equals(entry.Path, StringComparison.OrdinalIgnoreCase))))
            { results.Add(entry); continue; }
            try
            {
                using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(10));
                var result = await new ProcessRunner().RunAsync(entry.Path, ["-I", "-S", "-c", "import sys,json; print(json.dumps({'version':sys.version.split()[0],'executable':sys.executable}))"], cancellationToken: timeout.Token);
                using var json = JsonDocument.Parse(result.Output);
                var actual = json.RootElement.GetProperty("executable").GetString();
                results.Add(entry with { Version = json.RootElement.GetProperty("version").GetString(), ActualPath = actual,
                    MatchesDefault = result.ExitCode == 0 && string.Equals(actual, report.DefaultPath, StringComparison.OrdinalIgnoreCase) });
            }
            catch (Exception ex) when (ex is IOException or JsonException or OperationCanceledException or InvalidOperationException or System.ComponentModel.Win32Exception)
            { results.Add(entry with { Version = null }); }
        }
        return report with { Commands = results };
    }
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern SafeFileHandle CreateFileW(string path, uint access, uint share, IntPtr security, uint disposition, uint flags, IntPtr template);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool DeviceIoControl(SafeFileHandle handle, uint code, IntPtr input, int inputSize, byte[] output, int outputSize, out int returned, IntPtr overlapped);
    private static string? AliasTarget(string? path)
    {
        if (!OperatingSystem.IsWindows() || path is null) return null;
        using var handle = CreateFileW(path, 0, 7, IntPtr.Zero, 3, 0x02200000, IntPtr.Zero);
        var buffer = new byte[16384];
        if (handle.IsInvalid || !DeviceIoControl(handle, 0x000900A8, IntPtr.Zero, 0, buffer, buffer.Length, out var count, IntPtr.Zero) || count < 14 || BitConverter.ToUInt32(buffer) != 0x8000001B) return null;
        var strings = Encoding.Unicode.GetString(buffer, 12, (count - 12) & ~1).Split('\0');
        return strings.Length > 2 && Path.IsPathFullyQualified(strings[2]) ? strings[2] : null;
    }
    public static PathReport Inspect(IReadOnlyList<PythonRuntime> runtimes, string? manager, string? processPath = null, string? persistedPath = null, string? globalDirectory = null)
    {
        processPath ??= Environment.GetEnvironmentVariable("PATH") ?? "";
        persistedPath ??= OperatingSystem.IsWindows() ? string.Join(';', Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.Machine), Environment.GetEnvironmentVariable("PATH", EnvironmentVariableTarget.User)) : processPath;
        var windowsApps = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Microsoft", "WindowsApps");
        globalDirectory ??= Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Python", "bin");
        string[] Directories(string value) => value.Split(Path.PathSeparator).Select(p => Environment.ExpandEnvironmentVariables(p.Trim().Trim('"')))
            .Where(p => Path.IsPathFullyQualified(p) && !p.StartsWith(@"\\") && !p.Any(char.IsControl)).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        var directories = Directories(processPath);
        var active = runtimes.FirstOrDefault(r => r.IsDefault);
        var commands = new List<CommandResolution>();
        foreach (var command in new[] { "py", "python", "python3", "pymanager" })
        {
            // Never execute an arbitrary PATH hit. Known PIM aliases are reported as routing inferences.
            var candidates = directories.SelectMany(dir => new[] { ".com", ".exe", ".bat", ".cmd" }.Select(ext => Path.Combine(dir, command + ext)))
                .Where(File.Exists).ToArray();
            var first = candidates.FirstOrDefault();
            var runtime = runtimes.FirstOrDefault(r => string.Equals(r.Executable, first, StringComparison.OrdinalIgnoreCase));
            var knownManager = first is not null && string.Equals(first, manager, StringComparison.OrdinalIgnoreCase);
            // Merely living in WindowsApps does not prove an alias belongs to PIM.
            var matches = runtime is not null && runtime.Id == active?.Id;
            commands.Add(new(command, first, runtime?.Version ?? (knownManager ? "Python Install Manager" : null), matches, candidates));
        }
        return new(active?.Version, active?.Executable, commands,
            !directories.SequenceEqual(Directories(persistedPath), StringComparer.OrdinalIgnoreCase),
            !directories.Contains(windowsApps, StringComparer.OrdinalIgnoreCase),
            !directories.Contains(globalDirectory, StringComparer.OrdinalIgnoreCase));
    }
}
