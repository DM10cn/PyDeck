using System.Text.Json;

namespace PimGui.Core;

public sealed record HealthResult(bool Healthy, string Message);
public sealed record OperationVerification(bool Confirmed, string Message, IReadOnlyList<PythonRuntime> Runtimes);

public static class RuntimeHealth
{
    public static async Task<HealthResult> CheckAsync(PythonRuntime runtime, IProcessRunner? runner = null)
    {
        try
        {
            ExecutionPaths.Runtime(runtime);
            SafeFiles.RequireNoLinks(runtime.Executable);
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            var result = await (runner ?? new ProcessRunner()).RunAsync(runtime.Executable,
                ["-I", "-S", "-c", "import sys,json,encodings,ssl,sqlite3,ctypes; print(json.dumps({'version':sys.version.split()[0],'executable':sys.executable,'bits':64 if sys.maxsize>2**32 else 32}))"], cancellationToken: timeout.Token);
            if (result.ExitCode != 0 || result.OutputTruncated) return new(false, "The interpreter or a core module could not start");
            using var json = JsonDocument.Parse(result.Output.Trim());
            var version = json.RootElement.GetProperty("version").GetString();
            var executable = json.RootElement.GetProperty("executable").GetString();
            if (!string.Equals(Path.GetFullPath(executable!), Path.GetFullPath(runtime.Executable), StringComparison.OrdinalIgnoreCase) ||
                version != runtime.Version || json.RootElement.GetProperty("bits").GetInt32() != (runtime.Architecture == "x86" ? 32 : 64))
                return new(false, "The interpreter does not match its registered version or path");
            return new(true, "Interpreter and core modules are working");
        }
        catch (OperationCanceledException) { return new(false, "The interpreter check timed out"); }
        catch (Exception ex) when (ex is IOException or ArgumentException or JsonException or InvalidOperationException or System.ComponentModel.Win32Exception or UnauthorizedAccessException)
        { return new(false, "The interpreter could not be verified"); }
    }
    public static async Task<OperationVerification> VerifyAsync(PimClient client, RuntimeAction action, PythonRuntime expected)
    {
        var items = await client.ListAsync();
        var current = items.SingleOrDefault(r => r.Id.Equals(expected.Id, StringComparison.OrdinalIgnoreCase));
        if (action == RuntimeAction.Uninstall)
            return new(current is null && !File.Exists(expected.Executable) && !Directory.Exists(expected.Prefix), current is null && !File.Exists(expected.Executable) && !Directory.Exists(expected.Prefix)
                ? "Uninstall verified" : "The removed interpreter is still present", items);
        if (current is null) return new(false, "The expected interpreter is missing from the refreshed list", items);
        if (current.Version != expected.Version)
            return new(false, "The installed version differs from the requested version", items);
        var health = await CheckAsync(current);
        return new(health.Healthy, health.Message, items);
    }
}
